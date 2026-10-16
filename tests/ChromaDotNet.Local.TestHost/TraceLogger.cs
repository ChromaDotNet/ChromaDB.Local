using System.Diagnostics;
using Microsoft.Extensions.Logging;

internal sealed class TraceLogger(Stopwatch stopwatch) : ILogger
{
	public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

	public bool IsEnabled(LogLevel logLevel) => true;

	public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
		=> Console.Error.WriteLine($"{stopwatch.ElapsedMilliseconds,6} ms {logLevel}: {formatter(state, exception)}");
}
