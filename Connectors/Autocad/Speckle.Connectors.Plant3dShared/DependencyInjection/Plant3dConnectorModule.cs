using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.Autocad.DependencyInjection;
using Speckle.Connectors.Autocad.Operations.Send;
using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.Plant3dShared.Automation;
using Speckle.Connectors.Plant3dShared.Automation.Logging;
using Speckle.Connectors.Plant3dShared.Bindings;
using Speckle.Connectors.Plant3dShared.Operations.Send;
using Speckle.Sdk;

namespace Speckle.Connectors.Plant3dShared.DependencyInjection;

public static class Plant3dConnectorModule
{
  public static void AddPlant3d(this IServiceCollection serviceCollection)
  {
    serviceCollection.AddAutocadBase();

    // add send
    serviceCollection.LoadSend();
    serviceCollection.AddScoped<IRootObjectBuilder<AutocadRootObject>, Plant3dRootObjectBuilder>();
    serviceCollection.AddSingleton<ISendBinding, Plant3dSendBinding>();
    serviceCollection.AddSingleton<IBinding>(sp => sp.GetRequiredService<ISendBinding>());

    // automatically detects the Class:IClass interface pattern to register all generated interfaces
    serviceCollection.AddMatchingInterfacesAsTransient(Assembly.GetExecutingAssembly());
  }

  // Must be called after AddPlant3d(), as it overrides the IBrowserBridge registration
  public static void AddPlant3dAutomation(this IServiceCollection serviceCollection)
  {
    if (!serviceCollection.Any(descriptor => descriptor.ServiceType == typeof(IBrowserBridge)))
    {
      throw new InvalidOperationException(
        $"{nameof(AddPlant3dAutomation)} requires {nameof(IBrowserBridge)} to already be registered. "
          + $"Call {nameof(AddPlant3d)}() before {nameof(AddPlant3dAutomation)}()."
      );
    }

    serviceCollection.AddSingleton<BrowserBridge>();
    serviceCollection.AddSingleton<IBrowserBridge, AutomationBrowserBridgeDecorator>();
    serviceCollection.AddSingleton<IPlant3dDrawingModelMappingResolver, Plant3dDrawingModelMappingResolver>();
    serviceCollection.AddSingleton<IPlant3dProjectDrawingsProvider, Plant3dProjectDrawingsProvider>();
    serviceCollection.AddSingleton<IPlant3dDocumentResolver, Plant3dDocumentResolver>();
    serviceCollection.AddSingleton<IPlant3dModelSpaceObjectIdProvider, Plant3dModelSpaceObjectIdProvider>();
    serviceCollection.AddSingleton<IModelCardWriter, ModelCardWriter>();
    serviceCollection.AddSingleton<IPlant3dEditor, Plant3dEditor>();
    serviceCollection.AddSingleton<IMessageWriter, Plant3dEditor>();
    serviceCollection.AddLogging();
    serviceCollection.AddSingleton<ILoggerProvider, EditorLoggerProvider>();
    serviceCollection.AddSingleton<ILoggerProvider, FileLoggerProvider>();
    serviceCollection.AddSingleton<Plant3dAutomationService>();
  }
}
