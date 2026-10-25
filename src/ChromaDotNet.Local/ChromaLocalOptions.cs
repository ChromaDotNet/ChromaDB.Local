using Microsoft.Extensions.Logging;

namespace ChromaDotNet.Local;

/// <summary>
/// How <see cref="ChromaLocalServer.StartAsync(ChromaLocalOptions?, CancellationToken)"/> starts the server of an engine.
/// </summary>
public sealed class ChromaLocalOptions
{
	/// <summary>
	/// The engine to start: needed only when the application references both engine packages, ChromaDotNet.Local.ChromaDB and
	/// ChromaDotNet.Local.VecturaDB. When it is <see langword="null"/>, the default, the start takes the only engine package of the
	/// application, and fails when there are two, or none and no <see cref="ExecutablePath"/>.
	/// </summary>
	public ChromaLocalEngine? Engine { get; set; }

	/// <summary>
	/// The directory where the server keeps its data, created when it does not exist. When it is <see langword="null"/>, the default,
	/// the server keeps its data in a new temporary directory, deleted when it stops; when the application is killed before, a later
	/// start with temporary data deletes it.
	/// One server at a time on a directory: a second start on it throws an <see cref="InvalidOperationException"/>.
	/// On Windows the server works with a full path of at most 206 characters with Chroma, and 200 with VecturaDB: a longer one throws a
	/// <see cref="PathTooLongException"/>. With an <see cref="ExecutablePath"/> and no engine package, the limit is that of Chroma.
	/// </summary>
	public string? DataPath { get; set; }

	/// <summary>
	/// The port on 127.0.0.1. When it is <see langword="null"/>, the default, a free port is taken.
	/// </summary>
	public int? Port { get; set; }

	/// <summary>
	/// How long the start waits for the server to answer on <c>/api/v2/heartbeat</c>: 30 seconds by default, at most
	/// <see cref="int.MaxValue"/> milliseconds, about 24.8 days.
	/// </summary>
	public TimeSpan StartupTimeout { get; set; } = TimeSpan.FromSeconds(30);

	/// <summary>
	/// On Linux and macOS, how long the stop waits for the server to end after SIGTERM, before SIGKILL: 5 seconds by default, at most
	/// 2 seconds less than <see cref="int.MaxValue"/> milliseconds. On Windows the stop ends the process at once.
	/// </summary>
	public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromSeconds(5);

	/// <summary>
	/// The logger that receives the log of the server, with the level the server gives each line. When it is <see langword="null"/>,
	/// the default, the log is read and dropped.
	/// </summary>
	public ILogger? Logger { get; set; }

	/// <summary>
	/// The executable to start, instead of the one of the engine package for this platform: an executable of another version of the
	/// server, or for a platform the engine package does not have. It needs no engine package; with one, <see cref="Engine"/> and
	/// <see cref="ChromaLocalServer.EngineVersion"/> name its engine.
	/// </summary>
	public string? ExecutablePath { get; set; }

	/// <summary>
	/// Whether the start downloads the executable of the engine when the application does not have it: from the release of the
	/// version of the engine package, or from <see cref="DownloadUri"/>, into <see cref="CacheDirectory"/>. ChromaDotNet.Local.VecturaDB
	/// always downloads it; ChromaDotNet.Local.ChromaDB brings it in its runtime packages, unless the property
	/// <c>ChromaDotNetLocalChromaDBDownload</c> of the project takes them out of the output. The file is checked against the SHA-256
	/// hash in the engine package after the download and before each start. <see langword="true"/> by default; with
	/// <see langword="false"/> the start needs the executable in the application, or <see cref="ExecutablePath"/>.
	/// </summary>
	public bool DownloadExecutable { get; set; } = true;

	/// <summary>
	/// The directory of the downloaded executables. When it is <see langword="null"/>, the default, a directory of the user:
	/// <c>%LOCALAPPDATA%\ChromaDotNet.Local</c> on Windows, <c>~/Library/Caches/ChromaDotNet.Local</c> on macOS,
	/// <c>$XDG_CACHE_HOME/chromadotnet-local</c> or <c>~/.cache/chromadotnet-local</c> on Linux.
	/// </summary>
	public string? CacheDirectory { get; set; }

	/// <summary>
	/// The address the executable is downloaded from, like a mirror: the files of the release of the engine are taken by their names
	/// under it, like <c>chromadb-linux-x64.gz</c> for ChromaDotNet.Local.ChromaDB and <c>chroma-linux-x86_64</c> for
	/// ChromaDotNet.Local.VecturaDB. When it is <see langword="null"/>, the default, the release of the version of the engine package.
	/// The hash in the engine package is checked anyway.
	/// </summary>
	public Uri? DownloadUri { get; set; }
}
