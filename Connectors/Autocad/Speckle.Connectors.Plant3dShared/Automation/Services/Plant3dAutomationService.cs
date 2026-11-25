using Microsoft.Extensions.Logging;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Sdk;
using Speckle.Sdk.Credentials;

namespace Speckle.Connectors.Plant3dShared.Automation;

/// <summary>
/// Publishes every drawing in the current Plant 3D collaboration project that has a configured
/// drawing-to-model mapping. Each matched drawing is published via a freshly created sender model card.
/// </summary>
public sealed class Plant3dAutomationService(
  ISendBinding sendBinding,
  IModelCardWriter modelCardWriter,
  IPlant3dDrawingModelMappingResolver mappingResolver,
  IPlant3dProjectDrawingsProvider drawingsProvider,
  IPlant3dDocumentResolver documentResolver,
  IPlant3dModelSpaceObjectIdProvider objectIdProvider,
  IAccountManager accountManager,
  Plant3dDrawingChangeDetector changeDetector,
  IPlant3dModelVersionService versionService,
  IPlant3dIngestionWaiter ingestionWaiter,
  Plant3dPublishOptions options,
  ILogger<Plant3dAutomationService> logger
)
{
  /// <summary>
  /// Enumerates all drawings in the current Plant 3D project, matches them against the configured
  /// drawing-to-model mappings by drawing GUID, and publishes each match using a newly created sender model card.
  /// </summary>
  /// <exception cref="Plant3dConfigException">The mappings, account or project are invalid.</exception>
  public async Task<IReadOnlyList<Plant3dAutomationPublishResult>> PublishAsync()
  {
    // Enumerate and record the inventory first, so the mapper gets it even when the mappings are invalid.
    var projectDrawings = drawingsProvider.GetProjectDrawings();
    if (projectDrawings.Count == 0)
    {
      throw new Plant3dConfigException("No drawings found in the current Plant 3D project.");
    }

    var inventoryPath = Plant3dDrawingInventory.Write(
      options.RunId,
      drawingsProvider.GetProjectName(),
      projectDrawings
    );
    logger.LogInformation("Drawing inventory written to '{path}'.", inventoryPath);

    var mappingsPath =
      mappingResolver.GetMappingsPath()
      ?? throw new Plant3dConfigException("No drawing model mappings path specified.");

    logger.LogInformation("Using drawing model mappings file '{mappingsPath}'.", mappingsPath);

    var mappings = mappingResolver.DeserializeMappings(mappingsPath);
    if (mappings.Count == 0)
    {
      throw new Plant3dConfigException($"The drawing model mappings file '{mappingsPath}' contains no mappings.");
    }

    var issues = ValidateMappings(mappingsPath, mappings, projectDrawings);

    var automationBridge = SetUpAutomationBridge();
    var accountId =
      GetAccountId(mappings[0]) ?? throw new Plant3dConfigException($"No account found for {mappings[0].ServerUrl}.");

    var drawingsToPublish = MapDrawingsToModels(projectDrawings, mappings, accountId).ToList();
    if (drawingsToPublish.Count == 0)
    {
      throw new Plant3dConfigException("No project drawings matched the configured drawing model mappings.");
    }

    if (options.Force)
    {
      logger.LogInformation("Force publish is active. All mapped drawings will be published once in this run.");
    }

    List<Plant3dAutomationPublishResult> results = [.. GetMappingDriftResults(issues)];

    foreach (var drawingToPublish in drawingsToPublish)
    {
      if (options.IsPastCutoff(DateTimeOffset.UtcNow))
      {
        logger.LogWarning("Deadline reached. '{name}' was not attempted.", drawingToPublish.Name);
        results.Add(Plant3dAutomationPublishResult.Cancelled(drawingToPublish.DrawingGuid, drawingToPublish.FullPath));
        continue;
      }

      results.Add(await PublishWithRetriesAsync(automationBridge, drawingToPublish));
    }

    return results;
  }

  /// <summary>
  /// Tries a drawing up to 1 + <see cref="Plant3dPublishOptions.DrawingRetries"/> times, retrying only
  /// transient failures and never starting a retry after the cut-off.
  /// </summary>
  private async Task<Plant3dAutomationPublishResult> PublishWithRetriesAsync(
    AutomationBrowserBridgeDecorator automationBridge,
    DrawingToPublish drawing
  )
  {
    var delay = TimeSpan.FromSeconds(5);
    for (var attempt = 0; ; attempt++)
    {
      var result = await TryPublishAsync(automationBridge, drawing);
      if (
        result.FailureKind != FailureKind.Transient
        || attempt >= options.DrawingRetries
        || options.IsPastCutoff(DateTimeOffset.UtcNow + delay)
      )
      {
        return result;
      }

      logger.LogWarning(
        "Transient failure for '{name}': {error}. Retrying in {delay}s.",
        drawing.Name,
        result.ErrorMessage,
        delay.TotalSeconds
      );
      await Task.Delay(delay);
      delay = TimeSpan.FromTicks(delay.Ticks * 3);
    }
  }

  private async Task<Plant3dAutomationPublishResult> TryPublishAsync(
    AutomationBrowserBridgeDecorator automationBridge,
    DrawingToPublish drawing
  )
  {
    try
    {
      var decision = await changeDetector.EvaluateAsync(drawing, CancellationToken.None);
      if (!decision.ShouldPublish)
      {
        logger.LogInformation("Skip publish of '{name}': {reason}.", drawing.Name, decision.Reason);
        return decision.Reason switch
        {
          PublishReason.Unchanged => Plant3dAutomationPublishResult.Unchanged(drawing.DrawingGuid, drawing.FullPath),
          PublishReason.AlreadyPublishedInRun => Plant3dAutomationPublishResult.AlreadyPublishedInRun(
            drawing.DrawingGuid,
            drawing.FullPath
          ),
          PublishReason.FileUnavailable => Plant3dAutomationPublishResult.FileUnavailable(
            drawing.DrawingGuid,
            drawing.FullPath,
            decision.ErrorMessage ?? "Drawing file unavailable."
          ),
          _ => Plant3dAutomationPublishResult.ServerCheckFailed(
            drawing.DrawingGuid,
            drawing.FullPath,
            decision.ErrorMessage ?? "Change detection failed."
          ),
        };
      }

      logger.LogInformation("Publish '{name}': {reason}.", drawing.Name, decision.Reason);
      var result = await PublishDrawingAsync(automationBridge, drawing);
      if (result.Succeeded)
      {
        await TagVersionAsync(drawing, decision, result.VersionId);
      }

      return result with
      {
        Reason = decision.Reason,
      };
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      logger.LogError(ex, "Publish failed for '{name}'.", drawing.Name);
      return Plant3dAutomationPublishResult.Failure(
        drawing.DrawingGuid,
        drawing.FullPath,
        ex.Message,
        Plant3dFailureClassifier.Classify(ex)
      );
    }
  }

  private IReadOnlyList<MappingIssue> ValidateMappings(
    string mappingsPath,
    IReadOnlyList<DrawingModelMapping> mappings,
    IReadOnlyList<Plant3dProjectDrawing> projectDrawings
  )
  {
    var issues = Plant3dMappingValidator.Validate(mappings, projectDrawings);
    foreach (var issue in issues)
    {
      var level = issue.Severity == MappingIssueSeverity.Error ? LogLevel.Error : LogLevel.Warning;
      logger.Log(level, "Mapping issue ({kind}): {message}", issue.Kind, issue.Message);
    }

    var errorCount = issues.Count(i => i.Severity == MappingIssueSeverity.Error);
    return errorCount == 0
      ? issues
      : throw new Plant3dConfigException(
        $"The drawing model mappings file '{mappingsPath}' has {errorCount} error(s). See the log for details."
      );
  }

  /// <summary>
  /// Results for drawings skipped because the mappings and the project are out of sync.
  /// Reported as skipped, not failed.
  /// </summary>
  private static IEnumerable<Plant3dAutomationPublishResult> GetMappingDriftResults(IReadOnlyList<MappingIssue> issues)
  {
    foreach (var issue in issues)
    {
      switch (issue.Kind)
      {
        case MappingIssueKind.MappingWithoutDrawing:
          yield return Plant3dAutomationPublishResult.MappingWithoutDrawing(issue.DrawingGuid, issue.DrawingPath);
          break;
        case MappingIssueKind.DrawingWithoutMapping:
          yield return Plant3dAutomationPublishResult.DrawingWithoutMapping(issue.DrawingGuid, issue.DrawingPath);
          break;
      }
    }
  }

  private string? GetAccountId(DrawingModelMapping mapping)
  {
    var accountId = mapping.AccountId;
    var serverUrl = mapping.ServerUrl;
    var accounts = accountManager.GetAccounts(serverUrl).ToList();
    if (accounts.Count == 0)
    {
      return null;
    }

    var account = accounts.FirstOrDefault(account => account.id == accountId);
    if (account is null)
    {
      logger.LogWarning("Mapping account not found Id '{accountId}' and server '{serverUrl}'.", accountId, serverUrl);

      var accountList = string.Join("\n", accounts.Select(a => $"{a.id} {a.serverInfo.url} {a.userInfo.name}"));
      logger.LogInformation("Available accounts\n{accounts}", accountList);

      account = accounts.First();
    }

    logger.LogInformation(
      "Using account {accountId} {userName} {accountServer}",
      account.id,
      account.userInfo.name,
      account.serverInfo.url
    );

    return account.id;
  }

  private static IEnumerable<DrawingToPublish> MapDrawingsToModels(
    IEnumerable<Plant3dProjectDrawing> drawings,
    IEnumerable<DrawingModelMapping> mappings,
    string accountId
  )
  {
    // Index mappings by drawing GUID for O(1) lookups while enumerating project drawings.
    var mappingsByDrawingGuid = mappings.ToDictionary(m => m.DrawingGuid);
    foreach (var drawing in drawings)
    {
      if (mappingsByDrawingGuid.TryGetValue(drawing.DrawingGuid, out var mapping))
      {
        yield return new DrawingToPublish(
          drawing.DrawingGuid,
          drawing.FullPath,
          drawing.Name,
          accountId,
          mapping.ProjectId,
          mapping.ModelId,
          mapping.WorkspaceId,
          mapping.WorkspaceSlug,
          mapping.ServerUrl,
          mapping.AlwaysPublish
        );
      }
    }
  }

  /// <summary>
  /// Attempts to open the drawing and publish it, translating a failure to open the document into a
  /// <see cref="Plant3dAutomationPublishResult.NotOpened"/> result instead of letting the exception propagate.
  /// </summary>
  private async Task<Plant3dAutomationPublishResult> PublishDrawingAsync(
    AutomationBrowserBridgeDecorator automationBridge,
    DrawingToPublish drawing
  )
  {
    try
    {
      return await PublishInDrawingScopeAsync(automationBridge, drawing);
    }
    catch (Plant3dPublishDocumentScopeException ex)
    {
      return Plant3dAutomationPublishResult.NotOpened(drawing.DrawingGuid, drawing.FullPath, ex.Message);
    }
  }

  private async Task<Plant3dAutomationPublishResult> PublishInDrawingScopeAsync(
    AutomationBrowserBridgeDecorator automationBridge,
    DrawingToPublish drawing
  )
  {
    // Open and switch to the drawing being published, reversed when the scope is disposed.
    using var documentScope = new Plant3dPublishDocumentScope(documentResolver, drawing.FullPath);

    // Get all object IDs from the document being published.
    var objectIds = objectIdProvider.GetAllModelSpaceObjectIds(documentScope.Document);
    if (objectIds.Count == 0)
    {
      return Plant3dAutomationPublishResult.NoObjects(drawing.DrawingGuid, drawing.FullPath);
    }

    var modelCardId = modelCardWriter.Upsert(drawing, objectIds);

    using var cts = new CancellationTokenSource();
    try
    {
      var completionTask = automationBridge.WaitForCompletion(modelCardId, cts.Token);
      logger.LogInformation("Publishing '{name}' to Speckle. Please wait.", drawing.Name);
      await sendBinding.Send(modelCardId);
      var completion = await completionTask;

      if (!completion.Succeeded)
      {
        var error = completion.ErrorMessage ?? "Unknown publish error";
        logger.LogError("Speckle publish failed: {error}", error);
        return Plant3dAutomationPublishResult.Failure(
          drawing.DrawingGuid,
          drawing.FullPath,
          error,
          Plant3dFailureClassifier.Classify(completion.ErrorMessage)
        );
      }

      var versionId = completion.VersionId;
      if (!string.IsNullOrWhiteSpace(completion.IngestionId))
      {
        logger.LogInformation("Upload complete for '{name}'. Waiting for server processing.", drawing.Name);
        try
        {
          versionId = await ingestionWaiter.WaitForVersionAsync(
            drawing.AccountId,
            drawing.ProjectId,
            completion.IngestionId,
            cts.Token
          );
        }
        catch (OperationCanceledException ex)
        {
          // Not retried: the upload succeeded, so a re-publish would create a duplicate version.
          logger.LogWarning("Waiting for '{name}' stopped: {error}", drawing.Name, ex.Message);
          return Plant3dAutomationPublishResult.Cancelled(drawing.DrawingGuid, drawing.FullPath);
        }
      }

      logger.LogInformation("Speckle publish succeeded. Version: {versionId}", versionId);
      return Plant3dAutomationPublishResult.Success(
        drawing.DrawingGuid,
        drawing.FullPath,
        drawing.ProjectId,
        drawing.ModelId,
        versionId
      );
    }
    catch
    {
      await cts.CancelAsync();
      throw;
    }
    finally
    {
      automationBridge.Clear(modelCardId);
    }
  }

  /// <summary>
  /// Writes the drawing fingerprint to the published version's message. A failure does not fail the publish; the
  /// drawing is simply published again on the next run.
  /// </summary>
  private async Task TagVersionAsync(DrawingToPublish drawing, ChangeDecision decision, string? versionId)
  {
    if (versionId is null || decision.ContentHash is null)
    {
      logger.LogWarning("No version or fingerprint for '{name}'; it will be published again next run.", drawing.Name);
      return;
    }

    var tag = new VersionFingerprint(decision.ContentHash, Plant3dPublishSchema.VERSION, options.TagRunId).ToTag();
    try
    {
      await versionService.UpdateMessageAsync(
        drawing.AccountId,
        drawing.ProjectId,
        versionId,
        $"Automated publish of {drawing.Name} ({drawing.DrawingGuid})\n{tag}",
        CancellationToken.None
      );
    }
    catch (Exception ex) when (ex is SpeckleException or HttpRequestException or TimeoutException)
    {
      logger.LogWarning(
        "Could not tag version {versionId} for '{name}': {error}. It will be published again next run.",
        versionId,
        drawing.Name,
        ex.Message
      );
    }
  }

  private AutomationBrowserBridgeDecorator SetUpAutomationBridge()
  {
    // Use the send binding with a decorated bridge to detect when the send has completed and return the result.
    sendBinding.Parent.AssociateWithBinding(sendBinding);
    return sendBinding.Parent as AutomationBrowserBridgeDecorator
      ?? throw new InvalidOperationException("Automation browser bridge decorator was not configured.");
  }
}
