using Microsoft.Extensions.Logging;

namespace ComancheProxy.Logging;

/// <summary>
/// Lightweight file-based logger provider. NativeAOT-compatible, no external dependencies.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly StreamWriter _writer;
    private readonly LogLevel _minLevel;
    private readonly Lock _lock = new();

    /// <summary>
    /// Creates a file logger that appends to the specified path.
    /// </summary>
    public FileLoggerProvider(string filePath, LogLevel minLevel)
    {
        _minLevel = minLevel;
        string? dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        _writer = new StreamWriter(filePath, append: true) { AutoFlush = true };
    }

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => new FileLogger(this);

    /// <inheritdoc />
    public void Dispose() => _writer.Dispose();

    /// <summary>
    /// Minimal file logger. Formats each entry as a single timestamped line.
    /// </summary>
    private sealed class FileLogger(FileLoggerProvider provider) : ILogger
    {
        public bool IsEnabled(LogLevel logLevel) => logLevel >= provider._minLevel;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            string levelTag = logLevel switch
            {
                LogLevel.Trace => "TRC",
                LogLevel.Debug => "DBG",
                LogLevel.Information => "INF",
                LogLevel.Warning => "WRN",
                LogLevel.Error => "ERR",
                LogLevel.Critical => "CRT",
                _ => "???"
            };

            string message = formatter(state, exception);
            string line = $"{DateTime.Now:HH:mm:ss.fff} [{levelTag}] {message}";

            lock (provider._lock)
            {
                provider._writer.WriteLine(line);
                if (exception is not null)
                {
                    provider._writer.WriteLine(exception.ToString());
                }
            }
        }
    }
}
