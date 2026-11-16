using Autodesk.AutoCAD.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.Autocad.Plugin;
using Speckle.Connectors.Common;
using Speckle.Connectors.Plant3dShared.DependencyInjection;
using Speckle.Converters.Plant3dShared;

namespace Speckle.Connectors.Plant3dShared.Automation;

public class Plant3dAutomationCommand
{
  public const string COMMAND_STRING = "SpecklePublish";
  public ServiceProvider? Container { get; private set; }

  [CommandMethod(COMMAND_STRING)]
  [System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1031:Do not catch general exception types",
    Justification = "AutoCAD command entry points are void/async void, so failures must be reported instead of escaping the command boundary."
  )]
  public async void Command()
  {
    var services = new ServiceCollection();
    _ = services.Initialize(AppUtils.App, AppUtils.Version);
    services.AddPlant3d();
    services.AddPlant3dConverters();
    services.AddPlant3dAutomation();

    Container = services.BuildServiceProvider();
    var automationService = Container.GetRequiredService<Plant3dAutomationService>();
    var logger = Container.GetRequiredService<ILogger<Plant3dAutomationCommand>>();

    try
    {
      var results = await automationService.PublishAsync();
      foreach (var result in results)
      {
        if (!result.Executed)
        {
          logger.LogError("Unable to publish '{path}': {error}", result.DrawingPath, result.ErrorMessage);
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
          logger.LogError("Publish failed for '{path}': {error}", result.DrawingPath, result.ErrorMessage);
        }
      }
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
}
