using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using ChromaDB.Client;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace ChromaDotNet.Local.Tests;

[TestFixture]
public class ChromaLocalServerTests
{
	private static readonly string TestHostDirectory = typeof(ChromaLocalServerTests).Assembly
		.GetCustomAttributes<AssemblyMetadataAttribute>().First(a => a.Key == "TestHostDirectory").Value!;

	private string _root = null!;
	private DateTime _started;

	[SetUp]
	public void SetUp()
	{
		_started = DateTime.Now;
		_root = Path.Combine(Path.GetTempPath(), "chromadotnet-local-tests-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(_root);
	}

	[TearDown]
	public void TearDown()
	{
		try
		{
			Directory.Delete(_root, recursive: true);
		}
		catch (IOException)
		{
		}
	}

	[Test]
	public async Task StartsOnLoopbackAndAnswers()
	{
		await using var chroma = await ChromaLocalServer.StartAsync();
		using var client = chroma.CreateClient();

		Assert.That(chroma.Uri.Host, Is.EqualTo("127.0.0.1"));
		Assert.That(chroma.Uri.Port, Is.EqualTo(chroma.Port));
		Assert.That(await client.GetVersionAsync(), Is.Not.Empty);
		Assert.That(IsAlive(chroma.ProcessId), Is.True);
		Assert.That(Directory.Exists(chroma.DataPath), Is.True);
		Assert.That(ChromaLocalServer.ChromaVersion, Is.EqualTo("1.5.9"));
	}

	[Test]
	public async Task TemporaryDataIsDeletedAtTheStop()
	{
		var chroma = await ChromaLocalServer.StartAsync();
		var pid = chroma.ProcessId;
		await AddRecordsAsync(chroma, "temporary", 3);

		await chroma.DisposeAsync();

		Assert.That(chroma.HasExited, Is.True);
		Assert.That(IsAlive(pid), Is.False);
		Assert.That(Directory.Exists(chroma.DataPath), Is.False);
	}

	// The temporary data of an application that was killed: a later start with temporary data deletes it, once nobody holds its lock
	// and it is a minute old. It keeps the directories that are newer, or locked by a server that runs.
	[Test]
	public async Task TemporaryDataOfAKilledApplicationIsDeletedLater()
	{
		string Make(string state)
		{
			var directory = Path.Combine(Path.GetTempPath(), "chromadotnet-local-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(directory);
			File.WriteAllText(Path.Combine(directory, "chroma.sqlite3"), state);
			if (state != "new")
				Directory.SetLastWriteTimeUtc(directory, DateTime.UtcNow - TimeSpan.FromMinutes(5));
			return directory;
		}
		var abandoned = Make("abandoned");
		var recent = Make("new");
		var held = Make("held");
		using var lockOfHeld = new FileStream(Path.Combine(held, "chromadotnet-local.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
		Directory.SetLastWriteTimeUtc(held, DateTime.UtcNow - TimeSpan.FromMinutes(5));
		try
		{
			await using (var chroma = await ChromaLocalServer.StartAsync())
				Assert.That(Directory.Exists(chroma.DataPath), Is.True);
			Assert.That(Directory.Exists(abandoned), Is.False, "abandoned");
			Assert.That(Directory.Exists(recent), Is.True, "recent");
			Assert.That(Directory.Exists(held), Is.True, "held");
		}
		finally
		{
			lockOfHeld.Dispose();
			foreach (var directory in new[] { abandoned, recent, held })
				if (Directory.Exists(directory))
					Directory.Delete(directory, recursive: true);
		}
	}

	[Test]
	public async Task DataPathIsKeptAndReadAgain()
	{
		var path = Path.Combine(_root, "data with spaces");
		await using (var first = await ChromaLocalServer.StartAsync(new() { DataPath = path }))
			await AddRecordsAsync(first, "kept", 5);

		Assert.That(Directory.Exists(path), Is.True);
		await using var second = await ChromaLocalServer.StartAsync(new() { DataPath = path });
		Assert.That(await CountAsync(second, "kept"), Is.EqualTo(5));
	}

	[Test]
	public async Task RelativeDataPathIsMadeFull()
	{
		var relative = Path.GetRelativePath(Environment.CurrentDirectory, Path.Combine(_root, "relative"));
		await using var chroma = await ChromaLocalServer.StartAsync(new() { DataPath = relative });
		Assert.That(chroma.DataPath, Is.EqualTo(Path.Combine(_root, "relative")));
	}

	[Test]
	public async Task TwoServersAtTheSameTime()
	{
		var starts = await Task.WhenAll(
			ChromaLocalServer.StartAsync(new() { DataPath = Path.Combine(_root, "a") }),
			ChromaLocalServer.StartAsync(new() { DataPath = Path.Combine(_root, "b") }));
		await using var a = starts[0];
		await using var b = starts[1];

		Assert.That(a.Port, Is.Not.EqualTo(b.Port));
		await AddRecordsAsync(a, "shared_name", 2);
		await AddRecordsAsync(b, "shared_name", 7);
		Assert.That(await CountAsync(a, "shared_name"), Is.EqualTo(2));
		Assert.That(await CountAsync(b, "shared_name"), Is.EqualTo(7));
	}

	[Test]
	public async Task SameDataPathTwice()
	{
		var path = Path.Combine(_root, "same");
		await using (var first = await ChromaLocalServer.StartAsync(new() { DataPath = path }))
		{
			var before = ChromaProcessesWith(path).Count;
			var e = Assert.ThrowsAsync<InvalidOperationException>(() => ChromaLocalServer.StartAsync(new() { DataPath = path }));
			Assert.That(e!.Message, Does.Contain("another ChromaLocalServer"));
			// The second start does not start a process.
			Assert.That(ChromaProcessesWith(path), Has.Count.EqualTo(before));
			await AddRecordsAsync(first, "still_fine", 3);
		}
		// Free again after the stop.
		await using var again = await ChromaLocalServer.StartAsync(new() { DataPath = path });
		Assert.That(await CountAsync(again, "still_fine"), Is.EqualTo(3));
	}

	// A lock file that cannot be created for another reason than another server, here a directory that is not there: the real error,
	// with the path.
	[Test]
	public void LockOfTheDataFailsWithTheRealError()
	{
		var path = Path.Combine(_root, "missing");
		var e = Assert.Catch(() => ChromaLocalServer.LockDataPath(path));
		Assert.That(e, Is.InstanceOf<IOException>(), e!.ToString());
		Assert.That(e.Message, Does.Contain(path).And.Not.Contain("another ChromaLocalServer"));
	}

	// On Windows the index of Chroma opens its files with paths of at most 259 characters: a longer DataPath is refused before anything
	// starts, and no directory is made.
	[Test]
	public void DataPathTooLongOnWindows()
	{
		if (!OperatingSystem.IsWindows())
			Assert.Ignore("Windows only.");
		var path = PathOfLength(207);
		var e = Assert.ThrowsAsync<PathTooLongException>(() => ChromaLocalServer.StartAsync(new() { DataPath = path }));
		Assert.That(e!.Message, Does.Contain(path).And.Contain("206"));
		Assert.That(Directory.Exists(Path.GetDirectoryName(path)), Is.False, "no directory made");
	}

	// The longest DataPath on Windows keeps the index of a collection across a restart.
	[Test]
	public async Task LongestDataPathOnWindows()
	{
		if (!OperatingSystem.IsWindows())
			Assert.Ignore("Windows only.");
		var path = PathOfLength(206);
		await using (var chroma = await ChromaLocalServer.StartAsync(new() { DataPath = path }))
		{
			using var client = chroma.CreateClient();
			var records = client.GetCollectionClient(await client.GetOrCreateCollectionAsync("long"));
			for (var start = 0; start < 3000; start += 500)
				await records.AddAsync(
					Enumerable.Range(start, 500).Select(i => "id" + i).ToList(),
					Enumerable.Range(start, 500).Select(i => new ReadOnlyMemory<float>([i, 1, i % 7, i % 3])).ToList());
		}
		await using var again = await ChromaLocalServer.StartAsync(new() { DataPath = path });
		using var againClient = again.CreateClient();
		var found = await againClient.GetCollectionClient(await againClient.GetCollectionAsync("long"))
			.QueryAsync([new ReadOnlyMemory<float>([5, 1, 5, 2])], nResults: 3);
		Assert.That(found[0], Has.Count.EqualTo(3));
	}

	// Each server reads the output of Chroma with threads of its own, not with threads of the pool: with the pool held to two threads,
	// three servers that run at the same time start.
	[Test]
	public async Task ServersLeaveThePoolFree()
	{
		var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
		start.ArgumentList.Add(Path.Combine(TestHostDirectory, "ChromaDotNet.Local.TestHost.dll"));
		start.ArgumentList.Add("many");
		start.ArgumentList.Add("3");
		start.Environment["DOTNET_PROCESSOR_COUNT"] = "2";
		using var host = Process.Start(start)!;
		var output = host.StandardOutput.ReadToEndAsync();
		var error = host.StandardError.ReadToEndAsync();
		using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90)))
		{
			try
			{
				await host.WaitForExitAsync(timeout.Token);
			}
			catch (OperationCanceledException)
			{
				host.Kill(entireProcessTree: true);
			}
		}
		var text = await output + await error;
		TestContext.Out.WriteLine(text);
		Assert.That(text, Does.Contain("started 3").And.Contain("stopped"));
	}

	// An executable deleted after it was found and before Chroma started: an error that says so, with the path. Without the guard the
	// start of the process fails; with it, on Linux and macOS, the guard ends.
	[TestCase(false)]
	[TestCase(true)]
	public async Task ExecutableDeletedBeforeTheStart(bool guarded)
	{
		if (guarded && OperatingSystem.IsWindows())
			Assert.Ignore("No guard on Windows.");
		var missing = Path.Combine(_root, OperatingSystem.IsWindows() ? "chroma.exe" : "chroma");
		var guard = guarded ? Directory.GetFiles(AppContext.BaseDirectory, "chroma-local-guard", SearchOption.AllDirectories)
			.First(f => f.Contains(System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier, StringComparison.Ordinal)) : null;
		var binaries = new ChromaLocalBinaries(missing, guard);
		var dataPath = Path.Combine(_root, "data");
		Directory.CreateDirectory(dataPath);
		Exception? e;
		if (!guarded)
		{
			e = Assert.Catch(() => ChromaLocalServer.Launch(binaries, FreePort(), dataPath, false, new ChromaLocalOptions()));
		}
		else
		{
			var server = ChromaLocalServer.Launch(binaries, FreePort(), dataPath, false, new ChromaLocalOptions());
			e = Assert.CatchAsync(() => server.WaitUntilReadyAsync(TimeSpan.FromSeconds(30), CancellationToken.None));
			await server.StopAsync();
		}
		Assert.That(e, Is.TypeOf<FileNotFoundException>(), e!.ToString());
		Assert.That(e.Message, Does.Contain(missing).And.Contain("deleted"));
	}

	[Test]
	public async Task GivenPort()
	{
		var port = FreePort();
		await using var chroma = await ChromaLocalServer.StartAsync(new() { Port = port });
		Assert.That(chroma.Port, Is.EqualTo(port));
		Assert.That(chroma.Uri, Is.EqualTo(new Uri($"http://127.0.0.1:{port}")));
	}

	[Test]
	public async Task GivenPortInUse()
	{
		var listener = new TcpListener(IPAddress.Loopback, 0);
		listener.Start();
		try
		{
			var port = ((IPEndPoint)listener.LocalEndpoint).Port;
			var path = Path.Combine(_root, "busy");
			var e = Assert.ThrowsAsync<InvalidOperationException>(() => ChromaLocalServer.StartAsync(new() { Port = port, DataPath = path }));
			Assert.That(e!.Message, Does.Contain($"Port {port}"));
		}
		finally
		{
			listener.Stop();
		}
	}

	// Chroma writes that the port is taken and ends at once: the message must have it every time, also when the process ends before its
	// output has been read.
	[Test]
	public void GivenPortInUseThirtyTimes()
	{
		var listener = new TcpListener(IPAddress.Loopback, 0);
		listener.Start();
		try
		{
			var port = ((IPEndPoint)listener.LocalEndpoint).Port;
			var messages = new List<string>();
			for (var attempt = 0; attempt < 30; attempt++)
			{
				var e = Assert.ThrowsAsync<InvalidOperationException>(() => ChromaLocalServer.StartAsync(new() { Port = port, DataPath = Path.Combine(_root, "busy" + attempt) }));
				if (!e!.Message.Contains($"Port {port}", StringComparison.Ordinal))
					messages.Add($"{attempt}: {e.Message}");
			}
			Assert.That(messages, Is.Empty);
		}
		finally
		{
			listener.Stop();
		}
	}

	[Test]
	public async Task StopTwiceAndDisposeAfterTheStop()
	{
		var chroma = await ChromaLocalServer.StartAsync();
		var first = chroma.StopAsync();
		var second = chroma.StopAsync();
		await Task.WhenAll(first, second);
		await chroma.DisposeAsync();
		chroma.Dispose();
		Assert.That(chroma.HasExited, Is.True);
	}

	[Test]
	public async Task SynchronousDispose()
	{
		var chroma = await ChromaLocalServer.StartAsync();
		var pid = chroma.ProcessId;
		chroma.Dispose();
		Assert.That(IsAlive(pid), Is.False);
	}

	[Test]
	public async Task ChromaKilledFromOutside()
	{
		var path = Path.Combine(_root, "killed");
		var chroma = await ChromaLocalServer.StartAsync(new() { DataPath = path });
		await AddRecordsAsync(chroma, "after_kill", 4);
		using (var process = Process.GetProcessById(chroma.ProcessId))
			process.Kill();

		Assert.That(await WaitUntilAsync(() => chroma.HasExited, TimeSpan.FromSeconds(10)), Is.True);
		await chroma.DisposeAsync();

		await using var again = await ChromaLocalServer.StartAsync(new() { DataPath = path });
		Assert.That(await CountAsync(again, "after_kill"), Is.EqualTo(4));
	}

	[Test]
	public async Task LogGoesToTheLogger()
	{
		var logger = new ListLogger();
		await using (var chroma = await ChromaLocalServer.StartAsync(new() { Logger = logger }))
		{
			var line = logger.Snapshot().First(e => e.Message.StartsWith("Frontend server listening on address", StringComparison.Ordinal));
			Assert.That(line.Level, Is.EqualTo(LogLevel.Information));
			Assert.That(line.Message, Does.EndWith($"127.0.0.1:{chroma.Port}"));
		}
		Assert.That(logger.Snapshot().Any(e => e.Message.StartsWith("at ", StringComparison.Ordinal)), Is.False);
		Assert.That(logger.Snapshot().Any(e => e.Message.Contains('\u001b')), Is.False);
	}

	[Test]
	public void StartupTimeoutLeavesNothing()
	{
		var path = Path.Combine(_root, "timeout");
		Assert.ThrowsAsync<TimeoutException>(() => ChromaLocalServer.StartAsync(new() { DataPath = path, StartupTimeout = TimeSpan.FromMilliseconds(1) }));
		Assert.That(ChromaProcessesWith(path), Is.Empty);
	}

	// A timeout shorter than the start, twenty times: a TimeoutException each time, also when the timer fires late.
	[Test]
	public void StartupTimeoutTwentyTimes()
	{
		var results = new List<string>();
		for (var attempt = 0; attempt < 20; attempt++)
		{
			try
			{
				using var chroma = ChromaLocalServer.StartAsync(new() { DataPath = Path.Combine(_root, "timeout" + attempt), StartupTimeout = TimeSpan.FromMilliseconds(1) }).GetAwaiter().GetResult();
				results.Add($"{attempt}: started");
			}
			catch (TimeoutException)
			{
			}
			catch (Exception e)
			{
				results.Add($"{attempt}: {e.GetType().Name}");
			}
		}
		Assert.That(results, Is.Empty);
	}

	[Test]
	public void CancelledStartLeavesNothing()
	{
		var path = Path.Combine(_root, "cancelled");
		using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(5));
		Assert.CatchAsync<OperationCanceledException>(() => ChromaLocalServer.StartAsync(new() { DataPath = path }, cancel.Token));
		Assert.That(ChromaProcessesWith(path), Is.Empty);
	}

	[Test]
	public void MissingExecutable()
	{
		Assert.ThrowsAsync<FileNotFoundException>(() => ChromaLocalServer.StartAsync(new() { ExecutablePath = Path.Combine(_root, "nothing") }));
	}

	[Test]
	public void BadOptions()
	{
		Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => ChromaLocalServer.StartAsync(new() { Port = 0 }));
		Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => ChromaLocalServer.StartAsync(new() { StartupTimeout = TimeSpan.Zero }));
		Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => ChromaLocalServer.StartAsync(new() { ShutdownTimeout = TimeSpan.FromSeconds(-1) }));
	}

	// Timeouts longer than the waits of .NET can take: rejected before Chroma starts, with the name of the option and the maximum.
	[TestCase("StartupTimeout")]
	[TestCase("ShutdownTimeout")]
	public void TooLongTimeouts(string option)
	{
		var path = Path.Combine(_root, "long-" + option);
		var options = new ChromaLocalOptions { DataPath = path };
		if (option == "StartupTimeout")
			options.StartupTimeout = TimeSpan.MaxValue;
		else
			options.ShutdownTimeout = TimeSpan.MaxValue;
		var e = Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => ChromaLocalServer.StartAsync(options));
		Assert.That(e!.Message, Does.Contain(option).And.Contain("at most"));
		Assert.That(Directory.Exists(path), Is.False, "nothing started");
	}

	// The longest timeouts accepted start and stop.
	[Test]
	public async Task LongestTimeouts()
	{
		var chroma = await ChromaLocalServer.StartAsync(new() { StartupTimeout = TimeSpan.FromMilliseconds(int.MaxValue), ShutdownTimeout = TimeSpan.FromMilliseconds(int.MaxValue - 2000) });
		await chroma.DisposeAsync();
		Assert.That(chroma.HasExited, Is.True);
	}

	// The application ends in three ways: it stops Chroma, it ends without stopping it, it is killed. Chroma must end each time.
	[TestCase("dispose")]
	[TestCase("exit")]
	[TestCase("kill")]
	public async Task ApplicationEnds(string how)
	{
		var path = Path.Combine(_root, "app-" + how);
		using var host = StartTestHost(path);
		var ready = (await host.StandardOutput.ReadLineAsync())!.Split(' ');
		Assert.That(ready[0], Is.EqualTo("ready"), string.Join(" ", ready));
		var chromaPid = int.Parse(ready[2], System.Globalization.CultureInfo.InvariantCulture);
		Assert.That(IsAlive(chromaPid), Is.True);

		var stopwatch = Stopwatch.StartNew();
		if (how == "kill")
			host.Kill();
		else
			await host.StandardInput.WriteLineAsync(how);
		var gone = await WaitUntilAsync(() => !IsAlive(chromaPid), TimeSpan.FromSeconds(15));
		TestContext.Out.WriteLine($"{how}: Chroma gone after {stopwatch.ElapsedMilliseconds} ms");
		Assert.That(gone, Is.True, $"Chroma {chromaPid} still alive after the application ended with {how}");
		host.WaitForExit(10_000);

		// The data is still there, and readable.
		await using var again = await ChromaLocalServer.StartAsync(new() { DataPath = path });
		Assert.That(await again.CreateClient().GetVersionAsync(), Is.Not.Empty);
	}

	private static Process StartTestHost(string dataPath)
	{
		var dll = Path.Combine(TestHostDirectory, "ChromaDotNet.Local.TestHost.dll");
		var start = new ProcessStartInfo("dotnet") { RedirectStandardInput = true, RedirectStandardOutput = true, UseShellExecute = false };
		start.ArgumentList.Add(dll);
		start.ArgumentList.Add("serve");
		start.ArgumentList.Add(dataPath);
		return Process.Start(start)!;
	}

	private static async Task AddRecordsAsync(ChromaLocalServer chroma, string name, int count)
	{
		using var client = chroma.CreateClient();
		var collection = await client.GetOrCreateCollectionAsync(name);
		var records = client.GetCollectionClient(collection);
		var ids = Enumerable.Range(0, count).Select(i => "id" + i).ToList();
		var embeddings = Enumerable.Range(0, count).Select(i => new ReadOnlyMemory<float>([i, 1])).ToList();
		await records.AddAsync(ids, embeddings);
	}

	private static async Task<int> CountAsync(ChromaLocalServer chroma, string name)
	{
		using var client = chroma.CreateClient();
		return await client.GetCollectionClient(await client.GetCollectionAsync(name)).CountAsync();
	}

	private static bool IsAlive(int pid)
	{
		try
		{
			using var process = Process.GetProcessById(pid);
			return !process.HasExited;
		}
		catch (ArgumentException)
		{
			return false;
		}
		catch (InvalidOperationException)
		{
			return false;
		}
	}

	// The chroma processes of the test: on Linux those whose command line has the path, from /proc; on Windows and macOS those
	// named chroma started since the test began.
	private List<int> ChromaProcessesWith(string path)
	{
		if (OperatingSystem.IsLinux())
		{
			return Directory.GetDirectories("/proc")
				.Select(d => int.TryParse(Path.GetFileName(d), out var pid) ? pid : 0)
				.Where(pid => pid > 0 && CommandLineHas(pid, path))
				.ToList();
		}
		return Process.GetProcessesByName("chroma").Where(p => !p.HasExited && p.StartTime >= _started).Select(p => p.Id).ToList();
	}

	private static bool CommandLineHas(int pid, string text)
	{
		try
		{
			return File.ReadAllText($"/proc/{pid}/cmdline").Contains(text, StringComparison.Ordinal);
		}
		catch (IOException)
		{
			return false;
		}
		catch (UnauthorizedAccessException)
		{
			return false;
		}
	}

	private static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
	{
		var stopwatch = Stopwatch.StartNew();
		while (stopwatch.Elapsed < timeout)
		{
			if (condition())
				return true;
			await Task.Delay(25);
		}
		return condition();
	}

	// A path under the root of the test, of exactly that many characters, in directories of at most 80 characters.
	private string PathOfLength(int length)
	{
		var path = _root;
		while (path.Length < length)
		{
			var remaining = length - path.Length;
			var take = remaining <= 81 ? remaining : Math.Min(81, remaining - 2);
			path = Path.Combine(path, new string('d', take - 1));
		}
		Assert.That(path, Has.Length.EqualTo(length));
		return path;
	}

	private static int FreePort()
	{
		var listener = new TcpListener(IPAddress.Loopback, 0);
		listener.Start();
		var port = ((IPEndPoint)listener.LocalEndpoint).Port;
		listener.Stop();
		return port;
	}

	private sealed class ListLogger : ILogger
	{
		private readonly List<(LogLevel Level, string Message)> _entries = [];

		public List<(LogLevel Level, string Message)> Snapshot()
		{
			lock (_entries)
				return [.. _entries];
		}

		public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

		public bool IsEnabled(LogLevel logLevel) => true;

		public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
		{
			lock (_entries)
				_entries.Add((logLevel, formatter(state, exception)));
		}
	}
}
