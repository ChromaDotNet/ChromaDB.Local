using Microsoft.Extensions.Logging;

namespace ChromaDotNet.Local;

/// <summary>
/// How <see cref="ChromaLocalServer.StartAsync(ChromaLocalOptions?, CancellationToken)"/> starts Chroma.
/// </summary>
public sealed class ChromaLocalOptions
{
	/// <summary>
	/// The directory where Chroma keeps its data, created when it does not exist. When it is <see langword="null"/>, the default,
	/// Chroma keeps its data in a new temporary directory, deleted when the server stops, as the ephemeral client of Python does.
	/// One server at a time on a directory: a second start on it throws an <see cref="InvalidOperationException"/>.
	/// </summary>
	public string? DataPath { get; set; }

	/// <summary>
	/// The port on 127.0.0.1. When it is <see langword="null"/>, the default, a free port is taken.
	/// </summary>
	public int? Port { get; set; }

	/// <summary>
	/// How long the start waits for Chroma to answer on <c>/api/v2/heartbeat</c>: 30 seconds by default.
	/// </summary>
	public TimeSpan StartupTimeout { get; set; } = TimeSpan.FromSeconds(30);

	/// <summary>
	/// On Linux and macOS, how long the stop waits for Chroma to end after SIGTERM, before SIGKILL: 5 seconds by default. On
	/// Windows the stop ends the process at once.
	/// </summary>
	public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromSeconds(5);

	/// <summary>
	/// The logger that receives the log of Chroma, with the level Chroma gives each line. When it is <see langword="null"/>, the
	/// default, the log is read and dropped.
	/// </summary>
	public ILogger? Logger { get; set; }

	/// <summary>
	/// The Chroma executable to start, instead of the one of the <c>ChromaDotNet.Local.Runtime</c> package for this platform. For an
	/// executable of another version of Chroma, or for a platform without a runtime package.
	/// </summary>
	public string? ExecutablePath { get; set; }
}
