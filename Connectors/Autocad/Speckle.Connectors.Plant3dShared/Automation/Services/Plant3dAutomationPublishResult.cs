namespace Speckle.Connectors.Plant3dShared.Automation;

/// <summary>
/// How a drawing failed, which decides whether it is retried.
/// </summary>
public enum FailureKind
{
  None,
  Transient,
  Fatal,
  Cancelled,
}

/// <summary>
/// The outcome of attempting to publish a single Plant 3D drawing.
/// </summary>
public sealed record Plant3dAutomationPublishResult(
  Guid DrawingGuid,
  string DrawingPath,
  bool Executed = false,
  bool Succeeded = false,
  string? ProjectId = null,
  string? ModelId = null,
  string? VersionId = null,
  string? ErrorMessage = null
)
{
  /// <summary>The change detection reason that led to this result.</summary>
  public PublishReason? Reason { get; init; }

  /// <summary>How the drawing failed. <see cref="FailureKind.None"/> when it succeeded.</summary>
  public FailureKind FailureKind { get; init; }

  /// <summary>True when the drawing was skipped because it has not changed since the last publish.</summary>
  public bool IsUnchanged => Reason == PublishReason.Unchanged;

  /// <summary>True when the drawing was skipped because this run has already published it.</summary>
  public bool IsAlreadyPublishedInRun => Reason == PublishReason.AlreadyPublishedInRun;

  public static Plant3dAutomationPublishResult Unchanged(Guid drawingGuid, string drawingPath) =>
    new(drawingGuid, drawingPath, Succeeded: true) { Reason = PublishReason.Unchanged };

  public static Plant3dAutomationPublishResult AlreadyPublishedInRun(Guid drawingGuid, string drawingPath) =>
    new(drawingGuid, drawingPath, Succeeded: true) { Reason = PublishReason.AlreadyPublishedInRun };

  public static Plant3dAutomationPublishResult Cancelled(Guid drawingGuid, string drawingPath) =>
    new(drawingGuid, drawingPath, ErrorMessage: "Deadline reached") { FailureKind = FailureKind.Cancelled };

  public static Plant3dAutomationPublishResult ServerCheckFailed(Guid drawingGuid, string drawingPath, string error) =>
    new(drawingGuid, drawingPath, ErrorMessage: error)
    {
      Reason = PublishReason.ServerCheckFailed,
      FailureKind = FailureKind.Transient,
    };

  public static Plant3dAutomationPublishResult NotOpened(Guid drawingGuid, string drawingPath, string reason) =>
    new(drawingGuid, drawingPath, ErrorMessage: $"Failed to open drawing: {reason}") { FailureKind = FailureKind.Fatal };

  public static Plant3dAutomationPublishResult NoObjects(Guid drawingGuid, string drawingPath) =>
    new(drawingGuid, drawingPath, ErrorMessage: "No objects found in model space.") { FailureKind = FailureKind.Fatal };

  public static Plant3dAutomationPublishResult Success(
    Guid drawingGuid,
    string drawingPath,
    string projectId,
    string modelId,
    string? versionId
  ) => new(drawingGuid, drawingPath, true, true, projectId, modelId, versionId);

  public static Plant3dAutomationPublishResult Failure(
    Guid drawingGuid,
    string drawingPath,
    string errorMessage,
    FailureKind failureKind
  ) => new(drawingGuid, drawingPath, true, ErrorMessage: errorMessage) { FailureKind = failureKind };
}
