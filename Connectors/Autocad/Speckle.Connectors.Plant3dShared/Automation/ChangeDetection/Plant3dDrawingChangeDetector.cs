using Microsoft.Extensions.Logging;
using Speckle.Sdk;

namespace Speckle.Connectors.Plant3dShared.Automation;

/// <summary>
/// Decides whether a mapped drawing must be published, without opening it, by comparing the drawing file hash with
/// the fingerprint in the message of the target model's latest version.
/// </summary>
public sealed class Plant3dDrawingChangeDetector(
  Plant3dPublishOptions options,
  IPlant3dModelVersionService versionService,
  ILogger<Plant3dDrawingChangeDetector> logger
)
{
  public async Task<ChangeDecision> EvaluateAsync(DrawingToPublish drawing, CancellationToken cancellationToken)
  {
    if (!File.Exists(drawing.FullPath))
    {
      return new ChangeDecision(
        false,
        PublishReason.FileUnavailable,
        ErrorMessage: $"File '{drawing.FullPath}' does not exist."
      );
    }

    string contentHash;
    try
    {
      contentHash = await DrawingFingerprinter
        .ComputeHashAsync(drawing.FullPath, cancellationToken)
        .ConfigureAwait(false);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
      logger.LogWarning("Unable to read '{path}' for change detection: {error}", drawing.FullPath, ex.Message);
      return new ChangeDecision(
        false,
        PublishReason.FileUnavailable,
        ErrorMessage: $"Unable to read '{drawing.FullPath}': {ex.Message}"
      );
    }

    LatestVersionInfo? latest;
    try
    {
      latest = await versionService
        .GetLatestVersionAsync(drawing.AccountId, drawing.ProjectId, drawing.ModelId, cancellationToken)
        .ConfigureAwait(false);
    }
    catch (Exception ex)
      when (ex is SpeckleException or HttpRequestException or TimeoutException
        || ex is OperationCanceledException && !cancellationToken.IsCancellationRequested
      )
    {
      // Publishing needs the server too, so a drawing that cannot be checked is skipped and reported as an error.
      return new ChangeDecision(
        false,
        PublishReason.ServerCheckFailed,
        contentHash,
        $"Unable to check the latest version on the server: {ex.Message}"
      );
    }

    var fingerprint = VersionFingerprint.TryParse(latest?.Message);

    if (options.Force || drawing.AlwaysPublish)
    {
      // A retry within the same run must not publish a forced drawing a second time.
      var publishedInRun =
        options.TagRunId is not null
        && fingerprint?.RunId == options.TagRunId
        && fingerprint.ContentHash == contentHash;
      return publishedInRun
        ? new ChangeDecision(false, PublishReason.AlreadyPublishedInRun, contentHash)
        : new ChangeDecision(true, PublishReason.Forced, contentHash);
    }

    var reason = fingerprint switch
    {
      null => PublishReason.NeverPublished,
      { Schema: var schema } when schema != Plant3dPublishSchema.VERSION => PublishReason.SchemaChanged,
      { ContentHash: var hash } when hash != contentHash => PublishReason.ContentChanged,
      _ => PublishReason.Unchanged,
    };

    return new ChangeDecision(reason != PublishReason.Unchanged, reason, contentHash);
  }
}
