using ChromaDotNet.Local;

// Usage: GuardHostsApp <dataPath>. Starts the server without the download, so the executable and the guard come from the files of the
// packages, writes "ready <pid of the server>", and waits to be killed.
var server = await ChromaLocalServer.StartAsync(new ChromaLocalOptions { DataPath = args[0], DownloadExecutable = false });
Console.WriteLine($"ready {server.ProcessId}");
await Task.Delay(Timeout.Infinite);
