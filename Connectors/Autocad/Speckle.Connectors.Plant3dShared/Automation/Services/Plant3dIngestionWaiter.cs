using Microsoft.Extensions.Logging;
using Speckle.Sdk;
using Speckle.Sdk.Api;
using Speckle.Sdk.Api.GraphQL.Enums;
using Speckle.Sdk.Api.GraphQL.Models;
using Speckle.Sdk.Common;
using Speckle.Sdk.Credentials;

namespace Speckle.Connectors.Plant3dShared.Automation;

/// <summary>
/// Waits for a model ingestion to be processed by the server so that its version exists.
/// </summary>
public interface IPlant3dIngestionWaiter
{
  /// <summary>
  /// Polls the ingestion until it reaches a terminal state and returns the created version id.
  /// </summary>
  /// <exception cref="OperationCanceledException">
  /// The server cancelled the ingestion, or the deadline or maximum wait was reached.
  /// </exception>
  Task<string> WaitForVersionAsync(string accountId, string projectId, string ingestionId, CancellationToken ct);
}

/// <summary>
/// The send result fires once the upload finishes, before the server has created the version,
/// so the ingestion is polled until it succeeds.
/// </summary>
public sealed class Plant3dIngestionWaiter(
  IAccountManager accountManager,
  IClientFactory clientFactory,
  Plant3dPublishOptions options,
  ILogger<Plant3dIngestionWaiter> logger
) : IPlant3dIngestionWaiter
{
  private static readonly TimeSpan s_pollInterval = TimeSpan.FromSeconds(2);
  private static readonly TimeSpan s_maxWait = TimeSpan.FromMinutes(30);

  public async Task<string> WaitForVersionAsync(
    string accountId,
    string projectId,
    string ingestionId,
    CancellationToken ct
  )
  {
    using var client = clientFactory.Create(accountManager.GetAccount(accountId));
    var giveUpAt = DateTimeOffset.UtcNow + s_maxWait;
    ModelIngestionStatus? lastStatus = null;
    while (true)
    {
      ct.ThrowIfCancellationRequested();
      var now = DateTimeOffset.UtcNow;
      if (options.IsPastCutoff(now) || now > giveUpAt)
      {
        throw new OperationCanceledException($"Stopped waiting for ingestion {ingestionId}.");
      }

      ModelIngestion ingestion;
      try
      {
        ingestion = await client.Ingestion.Get(ingestionId, projectId, ct).ConfigureAwait(false);
      }
      // The upload already succeeded, so a failed poll must not fail the drawing and trigger a re-publish.
      catch (Exception ex) when (!ct.IsCancellationRequested && Plant3dFailureClassifier.Classify(ex) == FailureKind.Transient)
      {
        logger.LogWarning("Polling ingestion {ingestionId} failed: {error}. Retrying.", ingestionId, ex.Message);
        await Task.Delay(s_pollInterval, ct).ConfigureAwait(false);
        continue;
      }

      var status = ingestion.statusData.status;
      if (status != lastStatus)
      {
        logger.LogInformation(
          "Ingestion {ingestionId}: {status} {message}",
          ingestionId,
          status,
          ingestion.statusData.progressMessage
        );
        lastStatus = status;
      }

      switch (status)
      {
        case ModelIngestionStatus.success:
          return ingestion.statusData.versionId.NotNull();
        case ModelIngestionStatus.failed:
          throw new SpeckleException($"Server processing failed: {ingestion.statusData.progressMessage}");
        case ModelIngestionStatus.cancelled:
          throw new OperationCanceledException($"Ingestion {ingestionId} was cancelled by the server.");
      }

      await Task.Delay(s_pollInterval, ct).ConfigureAwait(false);
    }
  }
}
