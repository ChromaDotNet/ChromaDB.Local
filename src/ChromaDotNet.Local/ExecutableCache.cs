using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;

namespace ChromaDotNet.Local;

// The executables of the cache of the user, in <cache>/<directory of the version>/<rid>/:
// - the executable of an engine, downloaded from the release of its version for an application without its runtime package, or copied
//   from the runtime package when this user cannot execute the file of the package;
// - the guard, copied from the base package when this user cannot execute it.
// The release has the executable of each platform, compressed with gzip when the name of the file ends with .gz. The engine package
// has the SHA-256 hash of each executable, uncompressed, and the base package that of the guard: a file is checked against it after
// the download or the copy, and before each start. A lock file of the directory keeps a second process from downloading at the same
// time: it waits, then finds the file there.
internal static class ExecutableCache
{
	// v1.5.9.1-preview.6: the directory of the copies of the guard of this version of the base.
	public static string BaseCacheName { get; } = Metadata("ChromaDotNet.Local.CacheName") ?? "unknown";

	private static readonly Lazy<HttpClient> Http = new(() =>
	{
		var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
		client.DefaultRequestHeaders.UserAgent.ParseAdd($"ChromaDotNet.Local/{BaseCacheName.TrimStart('v')}");
		return client;
	});

	// The hash of the guard of a platform, or null when this build has none.
	public static string? GuardHash(string rid) => Metadata($"ChromaDotNet.Local.Guard:{rid}")?.ToLowerInvariant();

	public static string DefaultDirectory()
	{
		string? root;
		if (Platform.IsWindows)
		{
			root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
			return string.IsNullOrEmpty(root) ? throw NoDirectory() : Path.Combine(root, "ChromaDotNet.Local");
		}
		var home = Environment.GetEnvironmentVariable("HOME");
		if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
			return string.IsNullOrEmpty(home) ? throw NoDirectory() : Path.Combine(home, "Library", "Caches", "ChromaDotNet.Local");
		root = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
		if (string.IsNullOrEmpty(root))
			root = string.IsNullOrEmpty(home) ? throw NoDirectory() : Path.Combine(home, ".cache");
		return Path.Combine(root, "chromadotnet-local");
	}

	// The executable of the engine for a platform, in the cache: downloaded from the release when it is not there or does not match its
	// hash; with localSource, copied from that file when it has the hash, as the file of a runtime package another user owns.
	public static Task<string> GetEngineAsync(EnginePackage engine, string rid, ChromaLocalOptions options, CancellationToken cancellationToken, string? localSource = null)
	{
		var name = engine.ExecutableName(rid) ?? throw new InvalidOperationException($"{engine.PackageId} has no executable for {rid}.");
		var hash = engine.Hash(rid) ?? throw new InvalidOperationException($"{engine.PackageId} has no hash for the executable of {rid}.");
		var asset = engine.AssetName(rid);
		var download = asset is null ? null : new Uri(BaseUri(options, engine), asset);
		return GetAsync(engine.CacheName, rid, name, hash, download, engine, options, cancellationToken, localSource);
	}

	// The guard of a platform, in the cache: copied from the file of the base package, when it has the hash of this build.
	public static Task<string> GetGuardAsync(string rid, string localSource, ChromaLocalOptions options, CancellationToken cancellationToken)
	{
		var hash = GuardHash(rid) ?? throw new InvalidOperationException($"This build of ChromaDotNet.Local has no hash for the guard of {rid}.");
		return GetAsync(BaseCacheName, rid, ChromaLocalBinaries.GuardName, hash, null, null, options, cancellationToken, localSource);
	}

	internal static async Task<string> GetAsync(string cacheName, string rid, string name, string hash, Uri? download, EnginePackage? engine, ChromaLocalOptions options, CancellationToken cancellationToken, string? localSource = null)
	{
		var directory = Path.Combine(options.CacheDirectory is { } cache ? Path.GetFullPath(cache) : DefaultDirectory(), cacheName, rid);
		try
		{
			Directory.CreateDirectory(directory);
		}
		catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
		{
			throw CacheError(directory, e);
		}
		var path = Path.Combine(directory, name);

		using (await LockAsync(Path.Combine(directory, ".lock"), cancellationToken).ConfigureAwait(false))
		{
			DeleteLeftovers(directory, [name]);
			if (Directory.Exists(path))
				throw new IOException($"{path} is a directory, not the file {name} of the cache: delete it, or set ChromaLocalOptions.CacheDirectory to another directory.");
			if (File.Exists(path) && string.Equals(await HashFileAsync(path, cancellationToken).ConfigureAwait(false), hash, StringComparison.OrdinalIgnoreCase))
			{
				Platform.TryMakeExecutable(path);
				return path;
			}
			if (localSource is not null && string.Equals(await HashFileAsync(localSource, cancellationToken).ConfigureAwait(false), hash, StringComparison.OrdinalIgnoreCase))
			{
				await CopyAsync(localSource, path, hash, cancellationToken).ConfigureAwait(false);
				return path;
			}
			if (download is null)
				throw new InvalidDataException($"{localSource ?? name} does not have the SHA-256 hash {hash} of this package.");
			if (!options.DownloadExecutable)
				throw new InvalidOperationException($"{name} of {rid} is not in {directory}, and ChromaLocalOptions.DownloadExecutable is false.");
			await DownloadAsync(download, path, hash, rid, engine!, options.Logger, cancellationToken).ConfigureAwait(false);
			return path;
		}
	}

	private static Uri BaseUri(ChromaLocalOptions options, EnginePackage engine)
	{
		var uri = options.DownloadUri ?? engine.ReleaseUri;
		// Without the slash at the end, a relative name would replace the last segment.
		return uri.AbsoluteUri[uri.AbsoluteUri.Length - 1] == '/' ? uri : new Uri(uri.AbsoluteUri + "/");
	}

	private static async Task DownloadAsync(Uri uri, string path, string expectedHash, string rid, EnginePackage engine, ILogger? logger, CancellationToken cancellationToken)
	{
		var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
		var stopwatch = Stopwatch.StartNew();
		if (logger is not null)
			Log.Downloading(logger, engine.DisplayName, uri);
		try
		{
			string actualHash;
			long length;
			try
			{
				using var response = await Http.Value.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
				if (!response.IsSuccessStatusCode)
					throw new HttpRequestException($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
#if NET
				using var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
#else
				using var body = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
#endif
				// The files of the release ending with .gz are compressed with gzip; the others are the executables as they are.
				using var content = uri.AbsolutePath.EndsWith(".gz", StringComparison.OrdinalIgnoreCase) ? new GZipStream(body, CompressionMode.Decompress) : body;
				using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
				using (var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
				{
					var buffer = new byte[81920];
					int read;
					while ((read = await ReadAsync(content, buffer, cancellationToken).ConfigureAwait(false)) > 0)
					{
						sha.AppendData(buffer, 0, read);
						await WriteAsync(file, buffer, read, cancellationToken).ConfigureAwait(false);
					}
					actualHash = ToHex(sha.GetHashAndReset());
					length = file.Length;
				}
			}
			catch (Exception e) when (e is HttpRequestException || e is IOException || e is InvalidDataException || (e is TaskCanceledException && !cancellationToken.IsCancellationRequested))
			{
				throw new InvalidOperationException(
					$"Could not download the {engine.DisplayName} executable for {rid} from {uri}: {Sentence(e.Message)} Without the network: " +
					(engine.Engine == ChromaLocalEngine.ChromaDB
						? $"the runtime packages of {engine.PackageId} have the executable, unless ChromaDotNetLocalChromaDBDownload takes it out of the output; or set "
						: "set ") +
					$"DownloadUri to a mirror with the files of {engine.ReleaseUri}, or ExecutablePath to an executable.", e);
			}
			if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
				throw new InvalidDataException($"The file downloaded from {uri} has the SHA-256 hash {actualHash}, not {expectedHash}: it was not kept.");
			if (File.Exists(path))
				File.Delete(path);
			File.Move(temporary, path);
			Platform.TryMakeExecutable(path);
			if (logger is not null)
				Log.Downloaded(logger, path, length, stopwatch.ElapsedMilliseconds);
		}
		finally
		{
			if (File.Exists(temporary))
				File.Delete(temporary);
		}
	}

	// A copy checked against its hash after the copy too, then made executable.
	private static async Task CopyAsync(string source, string path, string expectedHash, CancellationToken cancellationToken)
	{
		var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
		try
		{
			using (var from = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true))
			using (var to = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
				await from.CopyToAsync(to, 81920, cancellationToken).ConfigureAwait(false);
			var actualHash = await HashFileAsync(temporary, cancellationToken).ConfigureAwait(false);
			if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
				throw new InvalidDataException($"The copy of {source} has the SHA-256 hash {actualHash}, not {expectedHash}: it was not kept.");
			if (File.Exists(path))
				File.Delete(path);
			File.Move(temporary, path);
			Platform.TryMakeExecutable(path);
		}
		finally
		{
			if (File.Exists(temporary))
				File.Delete(temporary);
		}
	}

	internal static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
	{
		using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
		using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
		var buffer = new byte[81920];
		int read;
		while ((read = await ReadAsync(file, buffer, cancellationToken).ConfigureAwait(false)) > 0)
			sha.AppendData(buffer, 0, read);
		return ToHex(sha.GetHashAndReset());
	}

	// The temporary files of downloads and copies that did not end, as when the application was killed during one: <name>.<32 hex
	// digits>.tmp. Under the lock no other process writes them. Best effort: a file that cannot be deleted stays.
	private static void DeleteLeftovers(string directory, string[] names)
	{
		foreach (var name in names)
		{
			List<string> files;
			try
			{
				files = Directory.EnumerateFiles(directory, name + ".*.tmp").ToList();
			}
			catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
			{
				continue;
			}
			foreach (var file in files)
			{
				var fileName = Path.GetFileName(file);
				var id = fileName.Substring(name.Length + 1, fileName.Length - name.Length - 1 - ".tmp".Length);
				if (id.Length != 32 || !id.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'))
					continue;
				try
				{
					File.Delete(file);
				}
				catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
				{
				}
			}
		}
	}

	// The lock file stays: deleted, a process could lock the old file while another creates a new one. The open is tried again only
	// while another process holds the file; any other error ends the start at once.
	internal static async Task<FileStream> LockAsync(string path, CancellationToken cancellationToken)
	{
		while (true)
		{
			try
			{
				return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
			}
			catch (IOException e) when (Platform.IsHeldByAnotherProcess(e))
			{
				await Task.Delay(100, cancellationToken).ConfigureAwait(false);
			}
			catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
			{
				throw CacheError(Path.GetDirectoryName(path)!, e);
			}
		}
	}

	private static IOException CacheError(string directory, Exception e) =>
		new($"Cannot use {directory} for the executables: {Sentence(e.Message)} Set ChromaLocalOptions.CacheDirectory to another directory.", e);

	// A message as a sentence, with one period at the end: those of .NET Framework have one, some of .NET do not.
	private static string Sentence(string message) => message.Length > 0 && message[message.Length - 1] == '.' ? message : message + ".";

#if NET
	private static ValueTask<int> ReadAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken) =>
		stream.ReadAsync(buffer.AsMemory(), cancellationToken);

	private static ValueTask WriteAsync(Stream stream, byte[] buffer, int count, CancellationToken cancellationToken) =>
		stream.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
#else
	private static Task<int> ReadAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken) =>
		stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);

	private static Task WriteAsync(Stream stream, byte[] buffer, int count, CancellationToken cancellationToken) =>
		stream.WriteAsync(buffer, 0, count, cancellationToken);
#endif

	private static string ToHex(byte[] bytes)
	{
		var chars = new char[bytes.Length * 2];
		for (var i = 0; i < bytes.Length; i++)
		{
			chars[i * 2] = "0123456789abcdef"[bytes[i] >> 4];
			chars[i * 2 + 1] = "0123456789abcdef"[bytes[i] & 0xF];
		}
		return new string(chars);
	}

	private static string? Metadata(string key) => typeof(ExecutableCache).Assembly
		.GetCustomAttributes<AssemblyMetadataAttribute>()
		.FirstOrDefault(a => a.Key == key)?.Value;

	private static InvalidOperationException NoDirectory() =>
		new("No directory of the user for the downloaded executables: set ChromaLocalOptions.CacheDirectory.");
}
