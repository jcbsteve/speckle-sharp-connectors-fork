namespace Speckle.Connectors.Plant3dShared.Automation;

/// <summary>
/// A project drawing matched against a configured drawing-to-model mapping, ready to be published.
/// </summary>
public sealed record DrawingToPublish(
  Guid DrawingGuid,
  string FullPath,
  string Name,
  string AccountId,
  string ProjectId,
  string ModelId,
  string WorkspaceId = "",
  string WorkspaceSlug = "",
  Uri? ServerUrl = null
);
