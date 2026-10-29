using Microsoft.Extensions.Logging;

namespace Speckle.Connectors.Plant3dShared.Automation.Logging;

/// <summary>
/// Logger provider that forwards formatted log messages to the active AutoCAD editor's
/// command line, so automation classes can use standard <see cref="ILogger"/> abstractions
/// instead of depending directly on the editor.
/// </summary>
public sealed class EditorLoggerProvider(IMessageWriter editor) : ILoggerProvider
{
  public ILogger CreateLogger(string categoryName) => new EditorLogger(editor);

  public void Dispose() { }
}

internal sealed class EditorLogger(IMessageWriter editor) : ILogger
{
  public IDisposable? BeginScope<TState>(TState state)
    where TState : notnull => null;

  public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

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
    editor.WriteMessage(exception is null ? message : $"{message}: {exception.Message}");
  }
}
