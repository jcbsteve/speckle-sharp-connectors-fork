using System.Drawing;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Windows;
using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.Common;
using Speckle.Connectors.DUI;
using Speckle.Connectors.DUI.WebView;
#if AUTOCAD
using Speckle.Connectors.Autocad.DependencyInjection;
using Speckle.Converters.Autocad;
#elif CIVIL3D
using Speckle.Converters.Civil3dShared;
using Speckle.Connectors.Civil3dShared.DependencyInjection;
#elif PLANT3D
using Microsoft.Extensions.Logging;
using Speckle.Connectors.Plant3dShared.Automation;
using Speckle.Connectors.Plant3dShared.DependencyInjection;
using Speckle.Converters.Plant3dShared;
#endif
namespace Speckle.Connectors.Autocad.Plugin;

public class AutocadCommand
{
  private static PaletteSet? PaletteSet { get; set; }
  private static readonly Guid s_id = new("7C27DD2B-86E8-4D31-B3DE-B34B267B1DC8");
  public ServiceProvider? Container { get; private set; }
  public const string COMMAND_STRING = "Speckle";
  public const string COMMAND_STRING_AUTOMATION = "SpecklePublish";

#if PLANT3D
  [CommandMethod(COMMAND_STRING_AUTOMATION)]
  [System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1031:Do not catch general exception types",
    Justification = "AutoCAD command entry points are void/async void, so failures must be reported instead of escaping the command boundary."
  )]
  public async void CommandAutomation()
  {
    var services = new ServiceCollection();

    _ = services.Initialize(AppUtils.App, AppUtils.Version);
    services.AddPlant3d();
    services.AddPlant3dConverters();
    services.AddPlant3dAutomation();

    Container = services.BuildServiceProvider();
    var automationService = Container.GetRequiredService<Plant3dAutomationService>();
    var logger = Container.GetRequiredService<ILogger<AutocadCommand>>();

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
#endif

  [CommandMethod(COMMAND_STRING)]
  public void Command()
  {
    if (PaletteSet != null)
    {
      FocusPalette();
      return;
    }

    PaletteSet = new PaletteSet($"Speckle", s_id)
    {
      Size = new Size(400, 500),
      DockEnabled = (DockSides)((int)DockSides.Left + (int)DockSides.Right),
    };

    // init DI
    var services = new ServiceCollection();

    _ = services.Initialize(AppUtils.App, AppUtils.Version);

#if AUTOCAD
    services.AddAutocad();
    services.AddAutocadConverters();
#elif CIVIL3D
    services.AddCivil3d();
    services.AddCivil3dConverters();
#elif PLANT3D
    services.AddPlant3d();
    services.AddPlant3dConverters();
#endif
    Container = services.BuildServiceProvider();
    Container.UseDUI();

    var panelWebView = Container.GetRequiredService<DUI3ControlWebView>();

    PaletteSet.AddVisual("Speckle", panelWebView);

    FocusPalette();
  }

  private void FocusPalette()
  {
    if (PaletteSet != null)
    {
      PaletteSet.KeepFocus = true;
      PaletteSet.Visible = true;
    }
  }
}
