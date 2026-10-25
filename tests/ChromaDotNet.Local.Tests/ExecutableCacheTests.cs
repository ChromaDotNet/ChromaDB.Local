using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;

namespace ChromaDotNet.Local.Tests;

[TestFixture]
public class ExecutableCacheTests
{
	// A platform the engines have executables for, as their packages are built with all of them.
	private const string Rid = "linux-x64";

	private static readonly EnginePackage ChromaDB = EnginePackage.Find(ChromaLocalEngine.ChromaDB)!;
	private static readonly EnginePackage VecturaDB = EnginePackage.Find(ChromaLocalEngine.VecturaDB)!;

	private string _root = null!;

	[SetUp]
	public void SetUp()
	{
		_root = Path.Combine(Path.GetTempPath(), "chromadotnet-local-cache-tests-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(_root);
	}

	[TearDown]
	public void TearDown() => Directory.Delete(_root, recursive: true);

	// A lock file held elsewhere: the open waits, and succeeds once the file is free.
	[Test]
	public async Task LockWaitsWhileHeld()
	{
		var path = Path.Combine(_root, ".lock");
		var held = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
		var stopwatch = Stopwatch.StartNew();
		var waiting = ExecutableCache.LockAsync(path, CancellationToken.None);
		await Task.Delay(500);
		Assert.That(waiting.IsCompleted, Is.False);
		held.Dispose();
		using var acquired = await waiting.WaitAsync(TimeSpan.FromSeconds(5));
		Assert.That(stopwatch.ElapsedMilliseconds, Is.GreaterThanOrEqualTo(500));
	}

	// Any other error, like a directory that is not there, ends at once with the directory in the message.
	[Test]
	public void LockFailsAtOnceOnAnotherError()
	{
		var directory = Path.Combine(_root, "missing");
		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
		var stopwatch = Stopwatch.StartNew();
		var e = Assert.CatchAsync(() => ExecutableCache.LockAsync(Path.Combine(directory, ".lock"), timeout.Token));
		Assert.That(e, Is.TypeOf<IOException>(), e!.ToString());
		Assert.That(e.Message, Does.Contain(directory));
		Assert.That(stopwatch.ElapsedMilliseconds, Is.LessThan(2000));
	}

	// A cache directory that cannot be created: the start fails at once and names it.
	[Test]
	public void StartFailsAtOnceWhenTheCacheCannotBeCreated()
	{
		var file = Path.Combine(_root, "a-file");
		File.WriteAllText(file, "");
		var options = new ChromaLocalOptions { CacheDirectory = Path.Combine(file, "cache") };
		var e = Assert.CatchAsync(() => ExecutableCache.GetEngineAsync(ChromaDB, Rid, options, CancellationToken.None));
		Assert.That(e, Is.TypeOf<IOException>(), e!.ToString());
		Assert.That(e.Message, Does.Contain(file));
	}

	// The temporary files of downloads that an application killed during the download left: deleted by the next process that takes
	// the lock, and only those.
	[Test]
	public void LeftoversOfInterruptedDownloadsAreDeleted()
	{
		var directory = Path.Combine(_root, ChromaDB.CacheName, Rid);
		Directory.CreateDirectory(directory);
		var leftover = Path.Combine(directory, ChromaDB.ExecutableName(Rid) + "." + Guid.NewGuid().ToString("N") + ".tmp");
		var other = Path.Combine(directory, "notes.tmp");
		File.WriteAllText(leftover, "partial");
		File.WriteAllText(other, "");
		var options = new ChromaLocalOptions { CacheDirectory = _root, DownloadExecutable = false };
		Assert.CatchAsync(() => ExecutableCache.GetEngineAsync(ChromaDB, Rid, options, CancellationToken.None));
		Assert.That(File.Exists(leftover), Is.False, "leftover of a download");
		Assert.That(File.Exists(other), Is.True, "another file");
	}

	// A directory where the executable goes: an error that names it and says what to do.
	[Test]
	public void DirectoryInPlaceOfTheExecutable()
	{
		var directory = Path.Combine(_root, ChromaDB.CacheName, Rid, ChromaDB.ExecutableName(Rid)!);
		Directory.CreateDirectory(directory);
		var options = new ChromaLocalOptions { CacheDirectory = _root, DownloadUri = new Uri("http://127.0.0.1:1/") };
		var e = Assert.CatchAsync(() => ExecutableCache.GetEngineAsync(ChromaDB, Rid, options, CancellationToken.None));
		Assert.That(e, Is.TypeOf<IOException>(), e!.ToString());
		Assert.That(e.Message, Does.Contain(directory).And.Contain("delete it"));
	}

	// Each engine has its directory of the cache, of its version: two engines, or two versions of one, do not share their files.
	[Test]
	public void EachEngineHasItsDirectory()
	{
		Assert.That(ChromaDB.CacheName, Is.EqualTo("chromadb-v" + TestMetadata.Value("ChromaDBVersion")));
		Assert.That(VecturaDB.CacheName, Is.EqualTo("vecturadb-" + TestMetadata.Value("VecturaDBTag")));
		Assert.That(ExecutableCache.BaseCacheName, Is.EqualTo("v" + TestMetadata.Value("LocalVersion")));
	}

	// A download of Chroma that fails with an error whose message ends with a period, here a file that is not gzip: one period, not
	// two; and what to do without the network, with the property that takes the executables of the runtime packages out.
	[Test]
	public async Task DownloadErrorWithOnePeriod()
	{
		var (uri, served) = ServeOnce(Encoding.ASCII.GetBytes("this is not gzip"));
		var options = new ChromaLocalOptions { CacheDirectory = _root, DownloadUri = uri };
		var e = Assert.CatchAsync(() => ExecutableCache.GetEngineAsync(ChromaDB, Rid, options, CancellationToken.None));
		// An error before the request leaves the server waiting: the timeout of the wait ends it.
		await served.WaitAsync(TimeSpan.FromSeconds(10));
		Assert.That(e, Is.TypeOf<InvalidOperationException>(), e!.ToString());
		Assert.That(e.InnerException!.Message, Does.EndWith("."), "the case of the test");
		Assert.That(e.Message, Does.Contain(e.InnerException.Message).And.Not.Contain(".."));
		Assert.That(e.Message, Does.StartWith($"Could not download the Chroma executable for {Rid} from {uri}chromadb-{Rid}.gz"));
		Assert.That(e.Message, Does.Contain("ChromaDotNetLocalChromaDBDownload"));
	}

	// The files of the release of VecturaDB are the executables, not compressed: the download takes the bytes as they are, and checks
	// their hash.
	[Test]
	public async Task DownloadOfVecturaDBIsNotDecompressed()
	{
		var bytes = Encoding.ASCII.GetBytes("this is not gzip");
		var (uri, served) = ServeOnce(bytes);
		var options = new ChromaLocalOptions { CacheDirectory = _root, DownloadUri = uri };
		var e = Assert.CatchAsync(() => ExecutableCache.GetEngineAsync(VecturaDB, Rid, options, CancellationToken.None));
		await served.WaitAsync(TimeSpan.FromSeconds(10));
		Assert.That(e, Is.TypeOf<InvalidDataException>(), e!.ToString());
		Assert.That(e.Message, Does.Contain($"{uri}{VecturaDB.AssetName(Rid)}").And.Contain(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()));
		Assert.That(Directory.GetFiles(Path.Combine(_root, VecturaDB.CacheName, Rid), VecturaDB.ExecutableName(Rid) + "*"), Is.Empty, "nothing kept");
	}

	// Without the network, VecturaDB has no runtime package: the message says the mirror and ExecutablePath.
	[Test]
	public void DownloadErrorOfVecturaDB()
	{
		var options = new ChromaLocalOptions { CacheDirectory = _root, DownloadUri = new Uri("http://127.0.0.1:1/") };
		var e = Assert.CatchAsync(() => ExecutableCache.GetEngineAsync(VecturaDB, Rid, options, CancellationToken.None));
		Assert.That(e, Is.TypeOf<InvalidOperationException>(), e!.ToString());
		Assert.That(e.Message, Does.StartWith($"Could not download the VecturaDB executable for {Rid} from http://127.0.0.1:1/{VecturaDB.AssetName(Rid)}"));
		Assert.That(e.Message, Does.Contain($"DownloadUri to a mirror with the files of {VecturaDB.ReleaseUri}").And.Contain("ExecutablePath")
			.And.Not.Contain("runtime packages"));
	}

	// An HTTP server on 127.0.0.1 that answers one request with the bytes, and its address.
	private static (Uri Uri, Task Served) ServeOnce(byte[] body)
	{
		var listener = new TcpListener(IPAddress.Loopback, 0);
		listener.Start();
		var uri = new Uri($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/");
		var served = Task.Run(async () =>
		{
			try
			{
				using var client = await listener.AcceptTcpClientAsync();
				var stream = client.GetStream();
				var request = new byte[4096];
				var received = "";
				while (!received.Contains("\r\n\r\n", StringComparison.Ordinal))
					received += Encoding.ASCII.GetString(request, 0, await stream.ReadAsync(request));
				await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n"));
				await stream.WriteAsync(body);
			}
			finally
			{
				listener.Stop();
			}
		});
		return (uri, served);
	}
}
