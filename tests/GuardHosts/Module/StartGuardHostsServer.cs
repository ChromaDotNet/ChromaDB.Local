using System.Management.Automation;
using ChromaDotNet.Local;

namespace GuardHostsModule;

// Start-GuardHostsServer -DataPath <path>: starts the server from a binary module, which PowerShell loads from the folder of the
// module, and writes the pid of the server.
[Cmdlet(VerbsLifecycle.Start, "GuardHostsServer")]
public sealed class StartGuardHostsServer : PSCmdlet
{
	private static ChromaLocalServer? _server;

	[Parameter(Mandatory = true)]
	public string DataPath { get; set; } = "";

	protected override void ProcessRecord()
	{
		_server = ChromaLocalServer.StartAsync(new ChromaLocalOptions { DataPath = DataPath, DownloadExecutable = false }).GetAwaiter().GetResult();
		WriteObject(_server.ProcessId);
	}
}
