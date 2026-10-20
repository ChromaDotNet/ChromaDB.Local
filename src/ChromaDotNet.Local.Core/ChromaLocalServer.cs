using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using ChromaDB.Client;
using Microsoft.Extensions.Logging;

namespace ChromaDotNet.Local;

/// <summary>
/// A Chroma server on this machine: the executable of the Chroma CLI, started as a child process on 127.0.0.1, with the data in a
/// directory. It stops with <see cref="StopAsync(CancellationToken)"/>, <see cref="DisposeAsync"/> or <see cref="Dispose"/>, and when
/// this process ends, also when it is killed: on Windows through a job object, on Linux and macOS through a small guard process.
/// </summary>
/// <example>
/// <code>
/// await using var chroma = await ChromaLocalServer.StartAsync(new() { DataPath = "./chroma-data" });
/// using var client = chroma.CreateClient();
/// </code>
/// </example>
public sealed class ChromaLocalServer : IAsyncDisposable, IDisposable
{
	private const string GuardPidPrefix = "chroma-local-guard: pid ";
	private static readonly TimeSpan MaxStartupTimeout = TimeSpan.FromMilliseconds(int.MaxValue);
	private static readonly TimeSpan MaxShutdownTimeout = TimeSpan.FromMilliseconds(int.MaxValue) - TimeSpan.FromSeconds(2);
	private const int MaxRecentLines = 40;

	private const string LockFileName = "chromadotnet-local.lock";
	private const string TemporaryPrefix = "chromadotnet-local-";

	private readonly Process _process;
	private FileStream? _dataLock;
	private readonly bool _guarded;
	private readonly bool _deleteDataPath;
	private readonly TimeSpan _shutdownTimeout;
	private readonly ILogger? _logger;
	private readonly TaskCompletionSource<bool> _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly TaskCompletionSource<bool> _listening = new(TaskCreationOptions.RunContinuationsAsynchronously);
	// The end of the standard output and of the standard error: the last lines of Chroma can arrive after its process has ended.
	private readonly TaskCompletionSource<bool> _outputEnded = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly TaskCompletionSource<bool> _errorEnded = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly TaskCompletionSource<int> _childPid = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly Queue<string> _recentLines = new();
	private readonly object _gate = new();
	private Task? _stop;

	/// <summary>
	/// The version of Chroma in the runtime packages of this version of ChromaDotNet.Local, like <c>1.5.9</c>.
	/// </summary>
	public static string ChromaVersion { get; } = typeof(ChromaLocalServer).Assembly
		.GetCustomAttributes<AssemblyMetadataAttribute>()
		.First(a => a.Key == "ChromaVersion").Value!;

	/// <summary>
	/// The URI of the server, like <c>http://127.0.0.1:52817</c>.
	/// </summary>
	public Uri Uri { get; }

	/// <summary>
	/// The port of the server on 127.0.0.1.
	/// </summary>
	public int Port { get; }

	/// <summary>
	/// The full path of the directory with the data of the server.
	/// </summary>
	public string DataPath { get; }

	/// <summary>
	/// The process id of Chroma.
	/// </summary>
	public int ProcessId { get; private set; }

	/// <summary>
	/// Whether Chroma has ended, stopped or on its own.
	/// </summary>
	public bool HasExited => _exited.Task.IsCompleted;

	private ChromaLocalServer(Process process, bool guarded, int port, string dataPath, bool deleteDataPath, ChromaLocalOptions options)
	{
		_process = process;
		_guarded = guarded;
		_deleteDataPath = deleteDataPath;
		_shutdownTimeout = options.ShutdownTimeout;
		_logger = options.Logger;
		Port = port;
		DataPath = dataPath;
		Uri = new Uri($"http://127.0.0.1:{port.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
	}

	/// <summary>
	/// Starts Chroma, and returns when it answers on <c>/api/v2/heartbeat</c>.
	/// </summary>
	/// <param name="options">How to start it; <see langword="null"/> for the defaults: temporary data and a free port.</param>
	/// <param name="cancellationToken">Stops the start, and Chroma with it.</param>
	/// <returns>The server, which answers.</returns>
	/// <exception cref="ArgumentOutOfRangeException">An option out of its range: a port, or a timeout.</exception>
	/// <exception cref="PlatformNotSupportedException">No Chroma executable for this platform, no
	/// <see cref="ChromaLocalOptions.ExecutablePath"/>, and the download turned off.</exception>
	/// <exception cref="FileNotFoundException"><see cref="ChromaLocalOptions.ExecutablePath"/> is not a file.</exception>
	/// <exception cref="InvalidOperationException">Chroma ended before it answered, and the message has the last lines of its log; or
	/// the port is taken; or <see cref="ChromaLocalOptions.DataPath"/> is the data of another server that runs; or the executable could
	/// not be downloaded, and the message has the address it tried and what to add to start without the network.</exception>
	/// <exception cref="InvalidDataException">The downloaded executable does not have the SHA-256 hash in this package.</exception>
	/// <exception cref="IOException">The directory of <see cref="ChromaLocalOptions.CacheDirectory"/> cannot be used.</exception>
	/// <exception cref="UnauthorizedAccessException">The executables cannot be executed by this user nor copied into its cache.</exception>
	/// <exception cref="TimeoutException">Chroma did not answer within <see cref="ChromaLocalOptions.StartupTimeout"/>.</exception>
	public static async Task<ChromaLocalServer> StartAsync(ChromaLocalOptions? options = null, CancellationToken cancellationToken = default)
	{
		options ??= new ChromaLocalOptions();
		if (options.StartupTimeout <= TimeSpan.Zero)
			throw new ArgumentOutOfRangeException(nameof(options), "StartupTimeout must be positive.");
		if (options.ShutdownTimeout < TimeSpan.Zero)
			throw new ArgumentOutOfRangeException(nameof(options), "ShutdownTimeout must not be negative.");
		// The waits of .NET take at most int.MaxValue milliseconds, about 24.8 days; the stop adds 2 seconds to ShutdownTimeout.
		if (options.StartupTimeout > MaxStartupTimeout)
			throw new ArgumentOutOfRangeException(nameof(options), $"StartupTimeout must be at most {MaxStartupTimeout}.");
		if (options.ShutdownTimeout > MaxShutdownTimeout)
			throw new ArgumentOutOfRangeException(nameof(options), $"ShutdownTimeout must be at most {MaxShutdownTimeout}.");
		if (options.Port is < 1 or > 65535)
			throw new ArgumentOutOfRangeException(nameof(options), "Port must be from 1 to 65535.");

		var binaries = await ChromaLocalBinaries.ResolveAsync(options, cancellationToken).ConfigureAwait(false);
		var deleteDataPath = options.DataPath is null;
		if (deleteDataPath)
			DeleteAbandonedTemporaryData();
		var dataPath = deleteDataPath
			? Path.Combine(Path.GetTempPath(), TemporaryPrefix + Guid.NewGuid().ToString("N"))
			: Path.GetFullPath(options.DataPath!);
		Directory.CreateDirectory(dataPath);
		// Chroma does not stop a second server on the same data, and two servers that write the same files can damage them: a file of
		// the directory, open without sharing while the server runs, stops a second ChromaLocalServer. The system closes it when this
		// process ends.
		var dataLock = LockDataPath(dataPath);

		// A free port can be taken by another process before Chroma binds it: Chroma then ends at once, and a new port is tried.
		const int MaxAttempts = 3;
		for (var attempt = 1; ; attempt++)
		{
			var port = options.Port ?? GetFreePort();
			ChromaLocalServer server;
			try
			{
				server = Launch(binaries, port, dataPath, deleteDataPath, options);
			}
			catch
			{
				await ReleaseAsync(dataLock, dataPath, deleteDataPath).ConfigureAwait(false);
				throw;
			}
			try
			{
				await server.WaitUntilReadyAsync(options.StartupTimeout, cancellationToken).ConfigureAwait(false);
				server._dataLock = dataLock;
				return server;
			}
			catch (PortNotAvailableException) when (options.Port is null && attempt < MaxAttempts)
			{
				await server.StopCoreAsync(deleteData: false).ConfigureAwait(false);
			}
			catch (PortNotAvailableException e)
			{
				await server.StopCoreAsync(deleteData: false).ConfigureAwait(false);
				await ReleaseAsync(dataLock, dataPath, deleteDataPath).ConfigureAwait(false);
				throw new InvalidOperationException(e.Message, e);
			}
			catch
			{
				await server.StopCoreAsync(deleteData: false).ConfigureAwait(false);
				await ReleaseAsync(dataLock, dataPath, deleteDataPath).ConfigureAwait(false);
				throw;
			}
		}
	}

	/// <summary>
	/// A client of this server, with an <c>HttpClient</c> of its own, which the <c>Dispose</c> of the client closes.
	/// </summary>
	/// <returns>The client.</returns>
	public ChromaClient CreateClient() => new(new ChromaConfigurationOptions(Uri));

	/// <summary>
	/// Stops Chroma, and waits until it has ended. On Linux and macOS Chroma gets SIGTERM, and SIGKILL after
	/// <see cref="ChromaLocalOptions.ShutdownTimeout"/>; on Windows it ends at once. The temporary data directory, when there is one,
	/// is deleted. Calling it again returns the same stop.
	/// </summary>
	/// <param name="cancellationToken">Stops the wait; Chroma still stops.</param>
	/// <returns>The stop.</returns>
	public Task StopAsync(CancellationToken cancellationToken = default)
	{
		Task stop;
		lock (_gate)
			stop = _stop ??= StopCoreAsync(_deleteDataPath);
		return cancellationToken.CanBeCanceled ? WaitAsync(stop, cancellationToken) : stop;
	}

	/// <summary>
	/// Stops Chroma, as <see cref="StopAsync(CancellationToken)"/>.
	/// </summary>
	/// <returns>The stop.</returns>
	public ValueTask DisposeAsync() => new(StopAsync());

	/// <summary>
	/// Stops Chroma, as <see cref="StopAsync(CancellationToken)"/>, and waits for it.
	/// </summary>
	public void Dispose() => StopAsync().GetAwaiter().GetResult();

	private static ChromaLocalServer Launch(ChromaLocalBinaries binaries, int port, string dataPath, bool deleteDataPath, ChromaLocalOptions options)
	{
		var chromaArgs = new[] { "run", "--path", dataPath, "--host", "127.0.0.1", "--port", port.ToString(System.Globalization.CultureInfo.InvariantCulture) };
		var guarded = binaries.GuardPath is not null;
		var start = guarded
			? CreateStartInfo(binaries.GuardPath!, ["--grace-ms", ((long)options.ShutdownTimeout.TotalMilliseconds).ToString(System.Globalization.CultureInfo.InvariantCulture), "--", binaries.ChromaPath, .. chromaArgs])
			: CreateStartInfo(binaries.ChromaPath, chromaArgs);
		start.WorkingDirectory = dataPath;
		// The guard stops Chroma when this pipe closes; Chroma itself gets /dev/null from the guard.
		start.RedirectStandardInput = guarded;
		start.RedirectStandardOutput = true;
		start.RedirectStandardError = true;
		start.Environment["NO_COLOR"] = "1";

		var process = new Process { StartInfo = start, EnableRaisingEvents = true };
		var server = new ChromaLocalServer(process, guarded, port, dataPath, deleteDataPath, options);
		process.Exited += (_, _) => server._exited.TrySetResult(true);
		process.OutputDataReceived += (_, e) =>
		{
			if (e.Data is null)
				server._outputEnded.TrySetResult(true);
			else
				server.OnLine(e.Data, isError: false);
		};
		process.ErrorDataReceived += (_, e) =>
		{
			if (e.Data is null)
				server._errorEnded.TrySetResult(true);
			else
				server.OnLine(e.Data, isError: true);
		};
		process.Start();
		if (Platform.IsWindows)
		{
			if (!WindowsJob.TryAssign(process, out var error))
				if (server._logger is not null)
					Log.NotInJob(server._logger, error);
			server.ProcessId = process.Id;
		}
		else if (!guarded)
		{
			if (server._logger is not null)
				Log.NoGuard(server._logger, binaries.ChromaPath);
			server.ProcessId = process.Id;
		}
		process.BeginOutputReadLine();
		process.BeginErrorReadLine();
		return server;
	}

	private static ProcessStartInfo CreateStartInfo(string fileName, string[] arguments)
	{
		var start = new ProcessStartInfo(fileName) { UseShellExecute = false, CreateNoWindow = true };
#if NET
		foreach (var argument in arguments)
			start.ArgumentList.Add(argument);
#else
		start.Arguments = CommandLine.Join(arguments);
#endif
		return start;
	}

	private async Task WaitUntilReadyAsync(TimeSpan timeout, CancellationToken cancellationToken)
	{
		using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeoutSource.CancelAfter(timeout);
		var token = timeoutSource.Token;
		try
		{
			if (_guarded && !Platform.IsWindows)
				ProcessId = await WaitAsync(_childPid.Task, _exited.Task, token).ConfigureAwait(false);
			// The line of Chroma that says it listens on our port: an answer on the port before it can come from another process.
			await WaitAsync(_listening.Task, _exited.Task, token).ConfigureAwait(false);
			using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
			var heartbeat = new Uri(Uri, "/api/v2/heartbeat");
			while (true)
			{
				try
				{
					using var response = await http.GetAsync(heartbeat, token).ConfigureAwait(false);
					if (response.IsSuccessStatusCode)
						return;
				}
				catch (HttpRequestException)
				{
				}
				catch (TaskCanceledException) when (!token.IsCancellationRequested)
				{
				}
				await WaitAsync(Task.Delay(20, token), _exited.Task, token).ConfigureAwait(false);
			}
		}
		catch (ExitedException)
		{
			// Chroma has ended: its last lines, like the one that says the port is taken, are read up to the end of its output.
			await Task.WhenAny(Task.WhenAll(_outputEnded.Task, _errorEnded.Task), Task.Delay(TimeSpan.FromSeconds(2), CancellationToken.None)).ConfigureAwait(false);
			var log = RecentLog();
			if (log.Contains($"127.0.0.1:{Port} is not available"))
				throw new PortNotAvailableException($"Port {Port} on 127.0.0.1 is not available.");
			throw new InvalidOperationException($"Chroma ended with code {ExitCodeOrUnknown()} before it answered. Its last lines:{Environment.NewLine}{log}");
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			throw new TimeoutException($"Chroma did not answer on {Uri} within {timeout.TotalSeconds} s. Its last lines:{Environment.NewLine}{RecentLog()}");
		}
	}

	// The result of the task, or ExitedException when Chroma ends first.
	private static async Task<T> WaitAsync<T>(Task<T> task, Task exited, CancellationToken token)
	{
		await WaitAsync((Task)task, exited, token).ConfigureAwait(false);
		return await task.ConfigureAwait(false);
	}

	private static async Task WaitAsync(Task task, Task exited, CancellationToken token)
	{
		var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		using (token.Register(() => cancelled.TrySetResult(true)))
		{
			var first = await Task.WhenAny(task, exited, cancelled.Task).ConfigureAwait(false);
			if (first == task)
			{
				await task.ConfigureAwait(false);
				return;
			}
			if (first == exited)
				throw new ExitedException();
			throw new OperationCanceledException(token);
		}
	}

	private static async Task WaitAsync(Task task, CancellationToken token)
	{
		var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		using (token.Register(() => cancelled.TrySetResult(true)))
		{
			if (await Task.WhenAny(task, cancelled.Task).ConfigureAwait(false) != task)
				throw new OperationCanceledException(token);
			await task.ConfigureAwait(false);
		}
	}

	private async Task StopCoreAsync(bool deleteData)
	{
		if (!_exited.Task.IsCompleted)
		{
			if (_guarded)
			{
				// The guard sends SIGTERM to Chroma when its standard input closes, and SIGKILL after the grace time.
				try
				{
					_process.StandardInput.Close();
				}
				catch (IOException)
				{
				}
				var margin = _shutdownTimeout + TimeSpan.FromSeconds(2);
				if (await Task.WhenAny(_exited.Task, Task.Delay(margin)).ConfigureAwait(false) != _exited.Task)
				{
					if (_logger is not null)
						Log.GuardDidNotEnd(_logger, margin.TotalSeconds);
					KillQuietly(_process);
					if (ProcessId != 0 && ProcessId != _process.Id)
						KillQuietly(ProcessId);
				}
			}
			else
			{
				KillQuietly(_process);
			}
			await _exited.Task.ConfigureAwait(false);
		}
		// The process has ended; the pipes of its output can still be read: WaitForExit waits for them too.
		_process.WaitForExit();
		_process.Dispose();
		var dataLock = _dataLock;
		_dataLock = null;
		if (dataLock is not null)
			await ReleaseAsync(dataLock, DataPath, deleteData).ConfigureAwait(false);
		else if (deleteData)
			await DeleteDirectoryAsync(DataPath).ConfigureAwait(false);
	}

	// The temporary data of applications killed before their stop: the directories with the prefix and a name of 32 hex digits,
	// older than a minute, whose lock nobody holds. The minute keeps a directory that another process has just made and not yet
	// locked. Best effort: a directory that cannot be read or deleted stays.
	private static void DeleteAbandonedTemporaryData()
	{
		IEnumerable<string> directories;
		try
		{
			directories = Directory.EnumerateDirectories(Path.GetTempPath(), TemporaryPrefix + "*").ToList();
		}
		catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
		{
			return;
		}
		foreach (var directory in directories)
		{
			var name = Path.GetFileName(directory).Substring(TemporaryPrefix.Length);
			if (name.Length != 32 || !name.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'))
				continue;
			try
			{
				if (DateTime.UtcNow - Directory.GetLastWriteTimeUtc(directory) < TimeSpan.FromMinutes(1))
					continue;
				using (new FileStream(Path.Combine(directory, LockFileName), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
				{
				}
				Directory.Delete(directory, recursive: true);
			}
			catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
			{
			}
		}
	}

	private static FileStream LockDataPath(string dataPath)
	{
		var path = Path.Combine(dataPath, LockFileName);
		try
		{
			return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
		}
		catch (IOException e)
		{
			throw new InvalidOperationException($"{dataPath} is the data of another ChromaLocalServer, which runs: two servers on the same data can damage it.", e);
		}
	}

	private static async Task ReleaseAsync(FileStream dataLock, string dataPath, bool deleteData)
	{
		dataLock.Dispose();
		if (deleteData)
			await DeleteDirectoryAsync(dataPath).ConfigureAwait(false);
	}

	private static void KillQuietly(Process process)
	{
		try
		{
			process.Kill();
		}
		catch (InvalidOperationException)
		{
		}
		catch (System.ComponentModel.Win32Exception)
		{
		}
	}

	private static void KillQuietly(int processId)
	{
		try
		{
			using var process = Process.GetProcessById(processId);
			process.Kill();
		}
		catch (ArgumentException)
		{
		}
		catch (InvalidOperationException)
		{
		}
		catch (System.ComponentModel.Win32Exception)
		{
		}
	}

	// On Windows the files of a process that has just ended can stay locked for a moment.
	private static async Task DeleteDirectoryAsync(string path)
	{
		for (var attempt = 1; ; attempt++)
		{
			try
			{
				if (Directory.Exists(path))
					Directory.Delete(path, recursive: true);
				return;
			}
			catch (Exception e) when ((e is IOException || e is UnauthorizedAccessException) && attempt < 20)
			{
				await Task.Delay(50).ConfigureAwait(false);
			}
		}
	}

	private void OnLine(string? line, bool isError)
	{
		if (line is null)
			return;
		if (isError && _guarded && line.StartsWith(GuardPidPrefix, StringComparison.Ordinal))
		{
#if NET
			var pidText = line.AsSpan(GuardPidPrefix.Length);
#else
			var pidText = line.Substring(GuardPidPrefix.Length);
#endif
			if (int.TryParse(pidText, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var pid))
				_childPid.TrySetResult(pid);
			return;
		}
		var entry = ChromaLogLine.Parse(line);
		if (entry is null)
			return;
		lock (_recentLines)
		{
			_recentLines.Enqueue(line.Trim());
			while (_recentLines.Count > MaxRecentLines)
				_recentLines.Dequeue();
		}
		if (entry.Value.Message.StartsWith("Frontend server listening on address", StringComparison.Ordinal)
			&& entry.Value.Message.EndsWith($"127.0.0.1:{Port}", StringComparison.Ordinal))
			_listening.TrySetResult(true);
		if (_logger is not null)
			Log.Chroma(_logger, entry.Value.Level, entry.Value.Message);
	}

	private string RecentLog()
	{
		lock (_recentLines)
			return string.Join(Environment.NewLine, _recentLines);
	}

	private string ExitCodeOrUnknown()
	{
		try
		{
			return _process.ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture);
		}
		catch (InvalidOperationException)
		{
			return "unknown";
		}
	}

	private static int GetFreePort()
	{
		var listener = new TcpListener(IPAddress.Loopback, 0);
		listener.Start();
		try
		{
			return ((IPEndPoint)listener.LocalEndpoint).Port;
		}
		finally
		{
			listener.Stop();
		}
	}

	private sealed class ExitedException : Exception
	{
	}

	private sealed class PortNotAvailableException(string message) : Exception(message)
	{
	}
}
