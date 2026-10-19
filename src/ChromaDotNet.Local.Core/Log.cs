using Microsoft.Extensions.Logging;

namespace ChromaDotNet.Local;

internal static partial class Log
{
	// A line of the log of Chroma, with the level Chroma gave it.
	[LoggerMessage(EventId = 1, Message = "{ChromaMessage}")]
	public static partial void Chroma(ILogger logger, LogLevel level, string chromaMessage);

	[LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "Chroma is not in the job object, error {Error}: it does not stop if this process is killed.")]
	public static partial void NotInJob(ILogger logger, int error);

	[LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "No chroma-local-guard next to {Executable}: Chroma does not stop if this process is killed.")]
	public static partial void NoGuard(ILogger logger, string executable);

	[LoggerMessage(EventId = 4, Level = LogLevel.Warning, Message = "chroma-local-guard did not end within {Seconds} s: killing it and Chroma.")]
	public static partial void GuardDidNotEnd(ILogger logger, double seconds);

	[LoggerMessage(EventId = 5, Level = LogLevel.Information, Message = "Downloading the Chroma executable from {Uri}.")]
	public static partial void Downloading(ILogger logger, Uri uri);

	[LoggerMessage(EventId = 6, Level = LogLevel.Information, Message = "Downloaded {Path}, {Length} bytes, in {Milliseconds} ms; its SHA-256 hash matches.")]
	public static partial void Downloaded(ILogger logger, string path, long length, long milliseconds);
}
