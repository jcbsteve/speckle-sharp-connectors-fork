namespace Speckle.Connectors.Plant3dShared.Automation;

/// <summary>
/// Why a drawing was or was not published.
/// </summary>
public enum PublishReason
{
  Forced,
  FileUnavailable,
  NeverPublished,
  SchemaChanged,
  ContentChanged,
  ServerCheckFailed,
  Unchanged,
}

/// <summary>
/// Outcome of change detection for a single drawing.
/// </summary>
/// <param name="ShouldPublish">True when the drawing must be published.</param>
/// <param name="Reason">The reason for the decision.</param>
/// <param name="ContentHash">
/// Hash of the drawing file taken before it is opened. Written to the version message after publishing.
/// </param>
/// <param name="ErrorMessage">Set when the drawing is skipped because the check could not be completed.</param>
public sealed record ChangeDecision(
  bool ShouldPublish,
  PublishReason Reason,
  string? ContentHash = null,
  string? ErrorMessage = null
);
