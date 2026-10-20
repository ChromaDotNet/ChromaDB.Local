using System.Diagnostics;
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

	// A platform this build has hashes for, as the package is built with all of them.
	private static string ExecutableCacheTestRid() => "linux-x64";
}
