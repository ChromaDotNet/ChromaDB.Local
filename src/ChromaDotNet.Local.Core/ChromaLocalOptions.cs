using Microsoft.Extensions.Logging;

namespace ChromaDotNet.Local;

/// <summary>
/// How <see cref="ChromaLocalServer.StartAsync(ChromaLocalOptions?, CancellationToken)"/> starts Chroma.
/// </summary>
public sealed class ChromaLocalOptions
{
	/// <summary>
	/// The directory where Chroma keeps its data, created when it does not exist. When it is <see langword="null"/>, the default,
	/// Chroma keeps its data in a new temporary directory, deleted when the server stops; when the application is killed before, a
	/// later start with temporary data deletes it.
	/// One server at a time on a directory: a second start on it throws an <see cref="InvalidOperationException"/>.
	/// </summary>
	public string? DataPath { get; set; }

	/// <summary>
	/// The port on 127.0.0.1. When it is <see langword="null"/>, the default, a free port is taken.
	/// </summary>
	public int? Port { get; set; }

	/// <summary>
	/// How long the start waits for Chroma to answer on <c>/api/v2/heartbeat</c>: 30 seconds by default, at most
	/// <see cref="int.MaxValue"/> milliseconds, about 24.8 days.
	/// </summary>
	public TimeSpan StartupTimeout { get; set; } = TimeSpan.FromSeconds(30);

	/// <summary>
	/// On Linux and macOS, how long the stop waits for Chroma to end after SIGTERM, before SIGKILL: 5 seconds by default, at most
	/// 2 seconds less than <see cref="int.MaxValue"/> milliseconds. On Windows the stop ends the process at once.
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

	/// <summary>
	/// Whether the start downloads the Chroma executable when no <c>ChromaDotNet.Local.Runtime</c> package has the one of this
	/// platform: from the GitHub release of this version of ChromaDotNet.Local, or from <see cref="DownloadUri"/>, into
	/// <see cref="CacheDirectory"/>. The file is checked against the SHA-256 hash in this package after the download and before each
	/// start. <see langword="true"/> by default; with <see langword="false"/> the start needs a runtime package or
	/// <see cref="ExecutablePath"/>.
	/// </summary>
	public bool DownloadExecutable { get; set; } = true;

	/// <summary>
	/// The directory of the downloaded executables. When it is <see langword="null"/>, the default, a directory of the user:
	/// <c>%LOCALAPPDATA%\ChromaDotNet.Local</c> on Windows, <c>~/Library/Caches/ChromaDotNet.Local</c> on macOS,
	/// <c>$XDG_CACHE_HOME/chromadotnet-local</c> or <c>~/.cache/chromadotnet-local</c> on Linux.
	/// </summary>
	public string? CacheDirectory { get; set; }

	/// <summary>
	/// The address the executables are downloaded from, like a mirror: the files of the release are taken by their names under it,
	/// like <c>chroma-linux-x64.gz</c>. When it is <see langword="null"/>, the default, the GitHub release of this version of
	/// ChromaDotNet.Local. The hash in this package is checked anyway.
	/// </summary>
	public Uri? DownloadUri { get; set; }
}
