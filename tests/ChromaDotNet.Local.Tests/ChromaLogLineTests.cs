using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace ChromaDotNet.Local.Tests;

[TestFixture]
public class ChromaLogLineTests
{
	[TestCase("  2026-10-16T01:08:23.207155Z  INFO  Frontend server listening on address, addr: 127.0.0.1:18950", LogLevel.Information, "Frontend server listening on address, addr: 127.0.0.1:18950")]
	[TestCase("  2026-10-16T01:08:23.207155Z  WARN  slow", LogLevel.Warning, "slow")]
	[TestCase("  2026-10-16T01:08:23.207155Z ERROR  broken", LogLevel.Error, "broken")]
	[TestCase("  2026-10-16T01:08:23.207155Z DEBUG  detail", LogLevel.Debug, "detail")]
	[TestCase("  2026-10-16T01:08:23.207155Z TRACE  more", LogLevel.Trace, "more")]
	[TestCase("Saving data to: ./d", LogLevel.Debug, "Saving data to: ./d")]
	[TestCase("Address 127.0.0.1:18951 is not available", LogLevel.Debug, "Address 127.0.0.1:18951 is not available")]
	public void Parses(string line, LogLevel level, string message)
	{
		var entry = ChromaLogLine.Parse(line);
		Assert.That(entry, Is.Not.Null);
		Assert.That(entry!.Value.Level, Is.EqualTo(level));
		Assert.That(entry.Value.Message, Is.EqualTo(message));
	}

	[TestCase("")]
	[TestCase("   ")]
	[TestCase(@"    at rust\frontend\src\server.rs:416")]
	[TestCase("    at rust/frontend/src/server.rs:416")]
	public void Drops(string line) => Assert.That(ChromaLogLine.Parse(line), Is.Null);

	[Test]
	public void RemovesColors()
	{
		var entry = ChromaLogLine.Parse("                \u001b[38;5;069m(((((((((    \u001b[38;5;203m(((((\u001b[38;5;220m####\u001b[0m");
		Assert.That(entry!.Value.Message, Is.EqualTo("(((((((((    (((((####"));
	}
}
