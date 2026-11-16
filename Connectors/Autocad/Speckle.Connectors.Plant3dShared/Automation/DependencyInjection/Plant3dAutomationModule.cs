using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.Plant3dShared.Automation.Logging;
using Speckle.Connectors.Plant3dShared.Bindings;
using Speckle.Connectors.Plant3dShared.DependencyInjection;

namespace Speckle.Connectors.Plant3dShared.Automation;

public static class Plant3dAutomationModule
{
  // Must be called after AddPlant3d(), as it overrides the IBinding (Plant3dSendBinding) and IBrowserBridge registrations
  public static void AddPlant3dAutomation(this IServiceCollection serviceCollection)
  {
    if (!serviceCollection.Any(descriptor => descriptor.ServiceType == typeof(IBrowserBridge)))
    {
      throw new InvalidOperationException(
        $"{nameof(AddPlant3dAutomation)} requires {nameof(IBrowserBridge)} to already be registered. "
          + $"Call {nameof(Plant3dConnectorModule.AddPlant3d)}() before {nameof(AddPlant3dAutomation)}()."
      );
    }

    serviceCollection.ExposeSendBindingAsISendBinding();
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

  // Upstream registers Plant3dSendBinding only as IBinding. Automation also needs it as
  // ISendBinding, so replace that registration with one shared singleton under both types.
  private static void ExposeSendBindingAsISendBinding(this IServiceCollection serviceCollection)
  {
    var sendBinding =
      serviceCollection.FirstOrDefault(d =>
        d.ServiceType == typeof(IBinding) && d.ImplementationType == typeof(Plant3dSendBinding)
      )
      ?? throw new InvalidOperationException(
        $"Expected {nameof(Plant3dSendBinding)} to be registered as {nameof(IBinding)} by "
          + $"{nameof(Plant3dConnectorModule.AddPlant3d)}(). The upstream registration may have changed."
      );

    serviceCollection.Remove(sendBinding);
    serviceCollection.AddSingleton<ISendBinding, Plant3dSendBinding>();
    serviceCollection.AddSingleton<IBinding>(sp => sp.GetRequiredService<ISendBinding>());
  }
}
