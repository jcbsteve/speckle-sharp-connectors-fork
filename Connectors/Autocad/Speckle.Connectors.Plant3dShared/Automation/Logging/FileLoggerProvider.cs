using Microsoft.Extensions.Logging;
using Speckle.Sdk;

namespace Speckle.Connectors.Plant3dShared.Automation.Logging;

/// <summary>
/// Logger provider that writes automation logs to a local file.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
  private readonly string _logFilePath = CreateLogFilePath();

  /// <summary>
  /// Creates a logger instance for the given category.
  /// </summary>
  public ILogger CreateLogger(string categoryName) => new FileLogger(_logFilePath);

  /// <summary>
  /// Releases provider resources.
  /// </summary>
  public void Dispose() { }

  /// <summary>
  /// Creates and returns the full path to the log file in a typical per-user location.
  /// </summary>
  private static string CreateLogFilePath()
  {
    var logsDirectory = Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
      "Speckle",
      "Logs"
    );

    Directory.CreateDirectory(logsDirectory);

    // TODO We may need to check the file for success and retry - therefore need to create a new file each time
    // File will need to have a predictable name, e.g. project and date?
    var logFilePath = Path.Combine(logsDirectory, "plant3d-automation.log");
    if (!File.Exists(logFilePath))
    {
      using var _ = File.Create(logFilePath);
    }

    return logFilePath;
  }
}

internal sealed class FileLogger(string logFilePath) : ILogger
{
  private static readonly object s_writeLock = new();

  /// <summary>
  /// Scopes are not used by this logger.
  /// </summary>
  public IDisposable? BeginScope<TState>(TState state)
    where TState : notnull => null;

  /// <summary>
  /// Enables Information and above.
  /// </summary>
  public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

  /// <summary>
  /// Writes a formatted log line including level, message, and exception details when available.
  /// </summary>
  public void Log<TState>(
    LogLevel logLevel,
    EventId eventId,
    TState state,
    Exception? exception,
    Func<TState, Exception?, string> formatter
  )
  {
    if (!IsEnabled(logLevel))
    {
      return;
    }

    var message = formatter(state, exception);
    var timestamp = DateTimeOffset.Now.ToString("O");

    var line = exception is null
      ? $"{timestamp} [{logLevel}] {message}"
      : $"{timestamp} [{logLevel}] {message}{Environment.NewLine}{exception}";

    try
    {
      lock (s_writeLock)
      {
        File.AppendAllText(logFilePath, line + Environment.NewLine);
      }
    }
    catch (Exception e) when (!e.IsFatal())
    {
      // Logging must never throw into application flow.
    }
  }
}
