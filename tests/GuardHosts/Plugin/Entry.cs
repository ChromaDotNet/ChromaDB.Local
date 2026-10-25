using ChromaDotNet.Local;

namespace GuardHostsPlugin;

// Started by a host in another directory, which loads this assembly in a load context of its own: AppContext.BaseDirectory is the
// directory of the host, and the files of the packages are next to this assembly.
public static class Entry
{
	private static ChromaLocalServer? _server;

	public static int Start(string dataPath)
	{
		_server = ChromaLocalServer.StartAsync(new ChromaLocalOptions { DataPath = dataPath, DownloadExecutable = false }).GetAwaiter().GetResult();
		return _server.ProcessId;
	}
}
