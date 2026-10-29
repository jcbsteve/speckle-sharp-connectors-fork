namespace Speckle.Connectors.Plant3dShared.Automation;

/// <summary>
/// Maps a Plant 3D drawing (identified by its stable <see cref="DrawingGuid"/>) to the Speckle
/// model/project/workspace/account it should be published to.
/// </summary>
/// <param name="DrawingGuid">The stable GUID Plant 3D assigns to the drawing, used as the sole matching key.</param>
/// <param name="RelativePath">The drawing's path relative to the project root. Informational only; not used for matching.</param>
/// <param name="ModelId">FKA branch id.</param>
/// <param name="ModelName">Human readable model name, informational only.</param>
/// <param name="ProjectId">FKA stream id.</param>
/// <param name="WorkspaceId">Workspace id.</param>
/// <param name="WorkspaceSlug">Workspace slug.</param>
/// <param name="AccountId">Account id to publish with.</param>
/// <param name="ServerUrl">Server the model lives on.</param>
public sealed record DrawingModelMapping(
  Guid DrawingGuid,
  string RelativePath,
  string ModelId,
  string ModelName,
  string ProjectId,
  string WorkspaceId,
  string WorkspaceSlug,
  string AccountId,
  Uri ServerUrl
)
{
  /// <summary>
  /// Initializes a new instance of <see cref="DrawingModelMapping"/>, accepting <paramref name="serverUrl"/> as a
  /// <see cref="Uri"/> rather than a raw <see cref="string"/>. This overload exists to satisfy CA1056
  /// (Uri properties should not be strings) at call sites that already hold a validated <see cref="Uri"/>,
  /// while keeping <see cref="ServerUrl"/> as a <see cref="string"/> for straightforward serialization.
  /// </summary>
  [method: Newtonsoft.Json.JsonConstructor]
  public DrawingModelMapping(
    Guid drawingGuid,
    string relativePath,
    string modelId,
    string modelName,
    string projectId,
    string workspaceId,
    string workspaceSlug,
    string accountId,
    string serverUrl
  )
    : this(
      drawingGuid,
      relativePath,
      modelId,
      modelName,
      projectId,
      workspaceId,
      workspaceSlug,
      accountId,
      new Uri(serverUrl)
    ) { }
}
