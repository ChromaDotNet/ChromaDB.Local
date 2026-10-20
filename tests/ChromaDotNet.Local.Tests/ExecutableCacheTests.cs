using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using NUnit.Framework;

namespace ChromaDotNet.Local.Tests;

[TestFixture]
public class ExecutableCacheTests
{
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
		var e = Assert.CatchAsync(() => ExecutableCache.GetAsync(ExecutableCacheTestRid(), ["chroma"], options, CancellationToken.None));
		Assert.That(e, Is.TypeOf<IOException>(), e!.ToString());
		Assert.That(e.Message, Does.Contain(file));
	}

	// The temporary files of downloads that an application killed during the download left: deleted by the next process that takes
	// the lock, and only those.
	[Test]
	public void LeftoversOfInterruptedDownloadsAreDeleted()
	{
		var directory = Path.Combine(_root, ExecutableCache.ReleaseTag, ExecutableCacheTestRid());
		Directory.CreateDirectory(directory);
		var leftover = Path.Combine(directory, "chroma." + Guid.NewGuid().ToString("N") + ".tmp");
		var other = Path.Combine(directory, "notes.tmp");
		File.WriteAllText(leftover, "partial");
		File.WriteAllText(other, "");
		var options = new ChromaLocalOptions { CacheDirectory = _root, DownloadExecutable = false };
		Assert.CatchAsync(() => ExecutableCache.GetAsync(ExecutableCacheTestRid(), ["chroma"], options, CancellationToken.None));
		Assert.That(File.Exists(leftover), Is.False, "leftover of a download");
		Assert.That(File.Exists(other), Is.True, "another file");
	}

	// A directory where the executable goes: an error that names it and says what to do.
	[Test]
	public void DirectoryInPlaceOfTheExecutable()
	{
		var directory = Path.Combine(_root, ExecutableCache.ReleaseTag, ExecutableCacheTestRid(), "chroma");
		Directory.CreateDirectory(directory);
		var options = new ChromaLocalOptions { CacheDirectory = _root, DownloadUri = new Uri("http://127.0.0.1:1/") };
		var e = Assert.CatchAsync(() => ExecutableCache.GetAsync(ExecutableCacheTestRid(), ["chroma"], options, CancellationToken.None));
		Assert.That(e, Is.TypeOf<IOException>(), e!.ToString());
		Assert.That(e.Message, Does.Contain(directory).And.Contain("delete it"));
	}

	// A download that fails with an error whose message ends with a period, here a file that is not gzip: one period, not two.
	[Test]
	public async Task DownloadErrorWithOnePeriod()
	{
		var listener = new TcpListener(IPAddress.Loopback, 0);
		listener.Start();
		try
		{
			var port = ((IPEndPoint)listener.LocalEndpoint).Port;
			var serve = Task.Run(async () =>
			{
				using var client = await listener.AcceptTcpClientAsync();
				var stream = client.GetStream();
				var request = new byte[4096];
				var received = "";
				while (!received.Contains("\r\n\r\n", StringComparison.Ordinal))
					received += Encoding.ASCII.GetString(request, 0, await stream.ReadAsync(request));
				await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 16\r\nConnection: close\r\n\r\nthis is not gzip"));
			});
			var options = new ChromaLocalOptions { CacheDirectory = _root, DownloadUri = new Uri($"http://127.0.0.1:{port}/") };
			var e = Assert.CatchAsync(() => ExecutableCache.GetAsync(ExecutableCacheTestRid(), ["chroma"], options, CancellationToken.None));
			// An error before the request leaves the server waiting: the finally stops it.
			await serve.WaitAsync(TimeSpan.FromSeconds(10));
			Assert.That(e, Is.TypeOf<InvalidOperationException>(), e!.ToString());
			Assert.That(e.InnerException!.Message, Does.EndWith("."), "the case of the test");
			Assert.That(e.Message, Does.Contain(e.InnerException.Message).And.Not.Contain(".."));
		}
		finally
		{
			listener.Stop();
		}
	}

	// A platform this build has hashes for, as the package is built with all of them.
	private static string ExecutableCacheTestRid() => "linux-x64";
}
