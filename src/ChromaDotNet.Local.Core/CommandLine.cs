#if !NET
using System.Text;

namespace ChromaDotNet.Local;

// ProcessStartInfo.Arguments for the targets without ArgumentList: each argument quoted with the rules of CommandLineToArgvW, which
// .NET also follows on Linux and macOS to split the string again.
internal static class CommandLine
{
	public static string Join(IEnumerable<string> arguments) => string.Join(" ", arguments.Select(Quote));

	private static string Quote(string argument)
	{
		if (argument.Length > 0 && argument.IndexOfAny([' ', '\t', '\n', '\v', '"']) < 0)
			return argument;
		var builder = new StringBuilder("\"");
		var backslashes = 0;
		foreach (var c in argument)
		{
			if (c == '\\')
			{
				backslashes++;
				continue;
			}
			// Backslashes before a quote are doubled, and the quote escaped.
			builder.Append('\\', c == '"' ? backslashes * 2 + 1 : backslashes);
			backslashes = 0;
			builder.Append(c);
		}
		// Backslashes before the closing quote are doubled.
		builder.Append('\\', backslashes * 2).Append('"');
		return builder.ToString();
	}
}
#endif
