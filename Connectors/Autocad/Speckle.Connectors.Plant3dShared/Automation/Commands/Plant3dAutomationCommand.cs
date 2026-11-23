using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.Autocad.Plugin;
using Speckle.Connectors.Common;
using Speckle.Connectors.Plant3dShared.Automation.Logging;
using Speckle.Connectors.Plant3dShared.DependencyInjection;
using Speckle.Converters.Plant3dShared;

namespace Speckle.Connectors.Plant3dShared.Automation;

/// <summary>
/// <c>SPECKLEPUBLISH</c> publishes interactively and leaves Plant 3D open. <c>-SPECKLEPUBLISH</c> is the batch form used
/// by <c>pub.lsp</c>: it writes the run's result file and then closes Plant 3D.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
  "Design",
  "CA1031:Do not catch general exception types",
  Justification = "AutoCAD command entry points are void/async void, so failures must be reported instead of escaping the command boundary."
)]
public class Plant3dAutomationCommand
{
  public const string COMMAND_STRING = "SpecklePublish";
  public const string BATCH_COMMAND_STRING = "-SpecklePublish";
  public ServiceProvider? Container { get; private set; }

  [CommandMethod(COMMAND_STRING)]
  public async void Command()
  {
    var logger = BuildContainer();
    try
    {
      PromptForForce();
      await PublishAndLogAsync(logger);
    }
    catch (Plant3dConfigException exception)
    {
      logger.LogWarning("{error}", exception.Message);
    }
    catch (OperationCanceledException)
    {
      logger.LogInformation("Speckle publish was canceled.");
    }
    catch (System.Exception exception)
    {
      logger.LogError(exception, "Speckle publish failed.");
    }
  }

  // Session context is needed to close documents when the publish has finished.
  [CommandMethod(BATCH_COMMAND_STRING, CommandFlags.Session)]
  public async void BatchCommand()
  {
    var logger = BuildContainer();
    var options = Container!.GetRequiredService<Plant3dPublishOptions>();
    options.AllowPrompts = false;
    if (options.ApplyDefaultBatchDeadline(DateTimeOffset.UtcNow))
    {
      logger.LogWarning(
        "{envVar} not set. Using a default deadline of {minutes} minutes.",
        Plant3dPublishOptions.DEADLINE_ENV_VAR,
        Plant3dPublishOptions.DefaultBatchTimeout.TotalMinutes
      );
    }
    logger.LogInformation(
      "Publish started. Run: {runId}, force: {force}, deadline: {deadline}, drawing retries: {retries}.",
      options.RunId,
      options.Force,
      options.DeadlineUtc?.ToString("O") ?? "none",
      options.DrawingRetries
    );

    var startedUtc = DateTimeOffset.UtcNow;
    IReadOnlyList<Plant3dAutomationPublishResult> results = [];
    string? configError = null;
    // An unexpected error leaves no result file, so the script reports a crash and retries the run.
    var writeResult = true;
    try
    {
      results = await PublishAndLogAsync(logger);
    }
    catch (Plant3dConfigException exception)
    {
      configError = exception.Message;
      logger.LogError("Configuration error: {error}", exception.Message);
    }
    catch (System.Exception exception)
    {
      writeResult = false;
      logger.LogError(exception, "Speckle publish failed.");
    }
    finally
    {
      if (writeResult)
      {
        WriteResultFile(logger, Plant3dRunResult.Create(options, startedUtc, results, configError));
      }

      QuitPlant3d(logger);
    }
  }

  private ILogger BuildContainer()
  {
    var services = new ServiceCollection();
    _ = services.Initialize(AppUtils.App, AppUtils.Version);
    services.AddPlant3d();
    services.AddPlant3dConverters();
    services.AddPlant3dAutomation();

    Container = services.BuildServiceProvider();
    var logger = Container.GetRequiredService<ILogger<Plant3dAutomationCommand>>();
    foreach (var warning in Container.GetRequiredService<Plant3dPublishOptions>().Warnings)
    {
      logger.LogWarning("{warning}", warning);
    }

    return logger;
  }

  private void PromptForForce()
  {
    if (Environment.GetEnvironmentVariable(Plant3dPublishOptions.FORCE_ENV_VAR) is not null)
    {
      return;
    }

    var editor = Container!.GetRequiredService<IPlant3dEditor>();
    var answer = editor.GetKeywords("Force publish all mapped drawings [Yes/No]", ["Yes", "No"]);
    Container!.GetRequiredService<Plant3dPublishOptions>().Force = answer == "Yes";
  }

  private async Task<IReadOnlyList<Plant3dAutomationPublishResult>> PublishAndLogAsync(ILogger logger)
  {
    var automationService = Container!.GetRequiredService<Plant3dAutomationService>();
    var results = await automationService.PublishAsync();
    foreach (var result in results)
    {
      if (result.IsUnchanged || result.IsAlreadyPublishedInRun)
      {
        logger.LogInformation("{reason} '{path}', not published.", result.Reason, result.DrawingPath);
      }
      else if (result.Succeeded)
      {
        logger.LogInformation(
          "Publish succeeded for '{path}'. Version: {version}",
          result.DrawingPath,
          result.VersionId
        );
      }
      else
      {
        logger.LogError(
          "Publish failed ({kind}) for '{path}': {error}",
          result.FailureKind,
          result.DrawingPath,
          result.ErrorMessage
        );
      }
    }

    logger.LogInformation(
      "Publish summary: {published} published, {unchanged} unchanged, {alreadyPublished} already published in run, "
        + "{failed} failed, {cancelled} cancelled. Force: {force}.",
      results.Count(r => r.Executed && r.Succeeded),
      results.Count(r => r.IsUnchanged),
      results.Count(r => r.IsAlreadyPublishedInRun),
      results.Count(r => r.FailureKind is FailureKind.Transient or FailureKind.Fatal),
      results.Count(r => r.FailureKind == FailureKind.Cancelled),
      Container!.GetRequiredService<Plant3dPublishOptions>().Force
    );

    return results;
  }

  private static void WriteResultFile(ILogger logger, Plant3dRunResult runResult)
  {
    var path = FileLoggerProvider.GetResultFilePath(runResult.RunId);
    try
    {
      runResult.Write(path);
      logger.LogInformation("Publish {status}. Result written to '{path}'.", runResult.Status, path);
    }
    catch (System.Exception exception)
    {
      logger.LogError(exception, "Unable to write the result file '{path}'.", path);
    }
  }

  private static void QuitPlant3d(ILogger logger)
  {
    try
    {
      // Discard changes so no save prompt can block the exit.
      foreach (var document in Application.DocumentManager.Cast<Document>().ToList())
      {
        document.CloseAndDiscard();
      }

      Application.Quit();
    }
    catch (System.Exception exception)
    {
      logger.LogError(exception, "Unable to close Plant 3D.");
    }
  }
}
