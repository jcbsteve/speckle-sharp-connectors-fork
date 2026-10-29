namespace Speckle.Connectors.Plant3dShared.Automation;

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
  public static Plant3dAutomationPublishResult NotOpened(Guid drawingGuid, string drawingPath, string reason) =>
    new(drawingGuid, drawingPath, ErrorMessage: $"Failed to open drawing: {reason}");

  public static Plant3dAutomationPublishResult NoObjects(Guid drawingGuid, string drawingPath) =>
    new(drawingGuid, drawingPath, ErrorMessage: "No objects found in model space.");

  public static Plant3dAutomationPublishResult Success(
    Guid drawingGuid,
    string drawingPath,
    string projectId,
    string modelId,
    string? versionId
  ) => new(drawingGuid, drawingPath, true, true, projectId, modelId, versionId);

  public static Plant3dAutomationPublishResult Failure(Guid drawingGuid, string drawingPath, string errorMessage) =>
    new(drawingGuid, drawingPath, true, ErrorMessage: errorMessage);
}
