namespace Speckle.Connectors.Plant3dShared.Automation;

/// <summary>
/// A published version's id and message.
/// </summary>
public sealed record LatestVersionInfo(string Id, string? Message);

/// <summary>
/// Access to Speckle model versions, used to read and write the publish fingerprint.
/// </summary>
public interface IPlant3dModelVersionService
{
  /// <summary>Returns the latest version of the model, or null when the model has no versions.</summary>
  Task<LatestVersionInfo?> GetLatestVersionAsync(
    string accountId,
    string projectId,
    string modelId,
    CancellationToken cancellationToken
  );

  /// <summary>Replaces the message of a version.</summary>
  Task UpdateMessageAsync(
    string accountId,
    string projectId,
    string versionId,
    string message,
    CancellationToken cancellationToken
  );
}
