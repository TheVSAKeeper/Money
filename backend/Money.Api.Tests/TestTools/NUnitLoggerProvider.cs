using Microsoft.Extensions.Logging;

namespace Money.Api.Tests.TestTools;

public sealed class NUnitLoggerProvider : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName)
    {
        return new NUnitLogger(categoryName);
    }

    public void Dispose() { }
}

file sealed class NUnitLogger(string categoryName) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        return null;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return logLevel >= LogLevel.Information;
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        var message = formatter(state, exception);

        if (string.IsNullOrEmpty(message) && exception == null)
        {
            return;
        }

        var level = logLevel switch
        {
            LogLevel.Trace => "TRC",
            LogLevel.Debug => "DBG",
            LogLevel.Information => "INF",
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            LogLevel.Critical => "CRT",
            _ => "???",
        };

        var output = $"[{level}] {categoryName}: {message}";

        if (exception != null)
        {
            output += $"{Environment.NewLine}{exception}";
        }

        TestContext.Progress.WriteLine(output);
    }
}
