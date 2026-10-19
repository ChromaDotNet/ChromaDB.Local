using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;

namespace ChromaDotNet.Local;

// The executables downloaded from the GitHub release of this version, for an application with ChromaDotNet.Local.Core and no runtime
// package. The release has each file compressed with gzip, as <name>-<rid>.gz; this assembly has the SHA-256 hash of each file
// uncompressed, written at the build from the files that went into the runtime packages. A file is checked against it after the
// download and before each start. A lock file of the directory keeps a second process from downloading at the same time: it waits,
// then finds the file there.
internal static class ExecutableCache
{
	public const string DefaultReleaseUri = "https://github.com/ChromaDotNet/ChromaDB.Local/releases/download/";
	private const string HashPrefix = "ChromaExecutable:";

	// v1.5.9.1-preview.3: the tag of the release with the executables of this version.
	public static string ReleaseTag { get; } = Metadata("ReleaseTag") ?? "unknown";

	private static readonly Lazy<HttpClient> Http = new(() =>
	{
		var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
		client.DefaultRequestHeaders.UserAgent.ParseAdd($"ChromaDotNet.Local/{ReleaseTag.TrimStart('v')}");
		return client;
	});

	// The hash of a file of a platform, like ("linux-x64", "chroma"), or null when this build has none.
	public static string? Hash(string rid, string name) => Metadata($"{HashPrefix}{rid}:{name}")?.ToLowerInvariant();

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

	// The paths of the files of the platform in the cache, downloaded when they are not there or do not match their hash.
	public static async Task<string[]> GetAsync(string rid, string[] names, ChromaLocalOptions options, CancellationToken cancellationToken)
	{
		var directory = Path.Combine(options.CacheDirectory is { } cache ? Path.GetFullPath(cache) : DefaultDirectory(), ReleaseTag, rid);
		Directory.CreateDirectory(directory);
		var paths = names.Select(name => Path.Combine(directory, name)).ToArray();
		var hashes = names.Select(name => Hash(rid, name) ?? throw new InvalidOperationException($"This build of ChromaDotNet.Local.Core has no hash for {name} of {rid}.")).ToArray();

		using (await LockAsync(Path.Combine(directory, ".lock"), cancellationToken).ConfigureAwait(false))
		{
			for (var i = 0; i < names.Length; i++)
			{
				if (File.Exists(paths[i]) && string.Equals(await HashFileAsync(paths[i], cancellationToken).ConfigureAwait(false), hashes[i], StringComparison.OrdinalIgnoreCase))
					continue;
				var asset = AssetName(rid, names[i]);
				var uri = new Uri(BaseUri(options), asset);
				await DownloadAsync(uri, paths[i], hashes[i], rid, options.Logger, cancellationToken).ConfigureAwait(false);
			}
		}
		return paths;
	}

	// chroma.exe of win-x64 is chroma-win-x64.exe.gz; chroma of linux-x64 is chroma-linux-x64.gz.
	public static string AssetName(string rid, string name)
	{
		var extension = Path.GetExtension(name);
		return $"{Path.GetFileNameWithoutExtension(name)}-{rid}{extension}.gz";
	}

	private static Uri BaseUri(ChromaLocalOptions options)
	{
		var uri = options.DownloadUri ?? new Uri(DefaultReleaseUri + ReleaseTag + "/");
		// Without the slash at the end, a relative name would replace the last segment.
		return uri.AbsoluteUri[uri.AbsoluteUri.Length - 1] == '/' ? uri : new Uri(uri.AbsoluteUri + "/");
	}

	private static async Task DownloadAsync(Uri uri, string path, string expectedHash, string rid, ILogger? logger, CancellationToken cancellationToken)
	{
		var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
		var stopwatch = Stopwatch.StartNew();
		if (logger is not null)
			Log.Downloading(logger, uri);
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
				using var gzip = new GZipStream(body, CompressionMode.Decompress);
				using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
				using (var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
				{
					var buffer = new byte[81920];
					int read;
					while ((read = await ReadAsync(gzip, buffer, cancellationToken).ConfigureAwait(false)) > 0)
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
					$"Could not download the Chroma executable for {rid} from {uri}: {e.Message}. Without the network: reference ChromaDotNet.Local, " +
					$"or ChromaDotNet.Local.Runtime.{rid}, which have the executable; or set DownloadUri to a mirror with the files of the release " +
					$"{ReleaseTag}, or ExecutablePath to an executable.", e);
			}
			if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
				throw new InvalidDataException($"The file downloaded from {uri} has the SHA-256 hash {actualHash}, not {expectedHash}: it was not kept.");
			if (File.Exists(path))
				File.Delete(path);
			File.Move(temporary, path);
			if (logger is not null)
				Log.Downloaded(logger, path, length, stopwatch.ElapsedMilliseconds);
		}
		finally
		{
			if (File.Exists(temporary))
				File.Delete(temporary);
		}
	}

	private static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
	{
		using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
		using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
		var buffer = new byte[81920];
		int read;
		while ((read = await ReadAsync(file, buffer, cancellationToken).ConfigureAwait(false)) > 0)
			sha.AppendData(buffer, 0, read);
		return ToHex(sha.GetHashAndReset());
	}

	// The lock file stays: deleted, a process could lock the old file while another creates a new one.
	private static async Task<FileStream> LockAsync(string path, CancellationToken cancellationToken)
	{
		while (true)
		{
			try
			{
				return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
			}
			catch (IOException)
			{
				await Task.Delay(100, cancellationToken).ConfigureAwait(false);
			}
		}
	}

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
		new("No directory of the user for the downloaded Chroma executables: set ChromaLocalOptions.CacheDirectory.");
}
