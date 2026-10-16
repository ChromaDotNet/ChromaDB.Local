using Microsoft.Extensions.Logging;

namespace ChromaDotNet.Local;

// A line of the output of Chroma, with NO_COLOR set. The log lines of tracing look like
//   "  2026-10-16T01:08:23.207155Z  INFO  Frontend server listening on address, addr: 127.0.0.1:18950"
// each followed by "    at rust\frontend\src\server.rs:416" and an empty line, which are dropped. The other lines, like the logo and
// the links of the start, are debug. The logo keeps its colors with NO_COLOR: the ANSI escape sequences are removed.
internal readonly struct ChromaLogLine(LogLevel level, string message)
{
	public LogLevel Level { get; } = level;

	public string Message { get; } = message;

	public static ChromaLogLine? Parse(string line)
	{
		line = WithoutEscapes(line);
		var text = line.Trim();
		if (text.Length == 0)
			return null;
		if (text.StartsWith("at ", StringComparison.Ordinal) && line.StartsWith("    ", StringComparison.Ordinal))
			return null;
		// A timestamp like 2026-10-16T01:08:23.207155Z, then the level, then the message.
		if (text.Length > 20 && text[4] == '-' && text[7] == '-' && text[10] == 'T')
		{
			var afterTime = text.IndexOf(' ');
			if (afterTime > 0)
			{
				var rest = text.Substring(afterTime).TrimStart();
				var afterLevel = rest.IndexOf(' ');
				var levelText = afterLevel > 0 ? rest.Substring(0, afterLevel) : rest;
				LogLevel? level = levelText switch
				{
					"TRACE" => LogLevel.Trace,
					"DEBUG" => LogLevel.Debug,
					"INFO" => LogLevel.Information,
					"WARN" => LogLevel.Warning,
					"ERROR" => LogLevel.Error,
					_ => null,
				};
				if (level is not null)
					return new ChromaLogLine(level.Value, afterLevel > 0 ? rest.Substring(afterLevel).Trim() : string.Empty);
			}
		}
		return new ChromaLogLine(LogLevel.Debug, text);
	}

	// Removes the sequences ESC [ ... final byte, as "\u001b[38;5;069m".
	private static string WithoutEscapes(string line)
	{
		var escape = line.IndexOf('\u001b');
		if (escape < 0)
			return line;
		var builder = new System.Text.StringBuilder(line.Length);
		var i = 0;
		while (i < line.Length)
		{
			if (line[i] == '\u001b' && i + 1 < line.Length && line[i + 1] == '[')
			{
				i += 2;
				while (i < line.Length && (line[i] < '@' || line[i] > '~'))
					i++;
				i++;
				continue;
			}
			builder.Append(line[i]);
			i++;
		}
		return builder.ToString();
	}
}
