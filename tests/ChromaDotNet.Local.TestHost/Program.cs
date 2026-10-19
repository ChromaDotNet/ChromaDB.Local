using System.Diagnostics;
using ChromaDB.Client;
using ChromaDotNet.Local;

// An application that uses ChromaDotNet.Local, for the tests that need a process of their own.
//   serve [dataPath] [stopFile]
//                     starts Chroma and writes "ready <uri> <pid of chroma> <ms>". Then, from standard input: "dispose" stops Chroma
//                     and ends; "exit" ends without stopping it; the end of the input stops Chroma and ends. With a stop file, the
//                     host stops Chroma and ends when that file appears, instead.
//   smoke [dataPath]  starts Chroma, adds two records, counts them, stops, and writes the times.
//   bench <dir>       measures: five starts and stops, a restart on 10,000 records, two starts at the same time.
var mode = args.Length > 0 ? args[0] : "smoke";
var dataPath = args.Length > 1 ? args[1] : null;
if (mode == "bench")
	return await Bench.RunAsync(dataPath!);
var stopwatch = Stopwatch.StartNew();
// CHROMA_LOCAL_TRACE=1 writes each line of the log of Chroma with the milliseconds since the start.
var logger = Environment.GetEnvironmentVariable("CHROMA_LOCAL_TRACE") == "1" ? new TraceLogger(stopwatch) : null;
// The options of the download, for the tests that use ChromaDotNet.Local.Core without a runtime package.
var options = new ChromaLocalOptions { DataPath = dataPath, Logger = logger };
if (Environment.GetEnvironmentVariable("CHROMA_LOCAL_DOWNLOAD_URI") is { Length: > 0 } downloadUri)
	options.DownloadUri = new Uri(downloadUri);
if (Environment.GetEnvironmentVariable("CHROMA_LOCAL_CACHE") is { Length: > 0 } cache)
	options.CacheDirectory = cache;
if (Environment.GetEnvironmentVariable("CHROMA_LOCAL_NO_DOWNLOAD") == "1")
	options.DownloadExecutable = false;
var chroma = await ChromaLocalServer.StartAsync(options);
var started = stopwatch.ElapsedMilliseconds;

if (mode == "serve")
{
	Console.WriteLine($"ready {chroma.Uri} {chroma.ProcessId} {started}");
	if (args.Length > 2)
	{
		while (!File.Exists(args[2]))
			await Task.Delay(100);
	}
	else
	{
		string? line;
		while ((line = Console.ReadLine()) is not null)
		{
			if (line == "exit")
				Environment.Exit(0);
			if (line == "dispose")
				break;
		}
	}
	stopwatch.Restart();
	await chroma.DisposeAsync();
	Console.WriteLine($"stopped {stopwatch.ElapsedMilliseconds}");
	return 0;
}

using (var client = chroma.CreateClient())
{
	var collection = await client.GetOrCreateCollectionAsync("smoke_test");
	var records = client.GetCollectionClient(collection);
	await records.UpsertAsync(["a", "b"], [new float[] { 1, 0 }, new float[] { 0, 1 }], documents: ["one", "two"]);
	var count = await records.CountAsync();
	Console.WriteLine($"framework {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}, chroma {ChromaLocalServer.ChromaVersion}, pid {chroma.ProcessId}, started in {started} ms, count {count}");
}
stopwatch.Restart();
await chroma.DisposeAsync();
Console.WriteLine($"stopped in {stopwatch.ElapsedMilliseconds} ms, data deleted: {!Directory.Exists(chroma.DataPath)}");
return 0;
