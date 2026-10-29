using System.Collections.Concurrent;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;

namespace Speckle.Connectors.Plant3dShared.Automation;

public sealed class AutomationBrowserBridgeDecorator(BrowserBridge inner) : IBrowserBridge
{
  private const string SET_MODEL_SEND_RESULT = "setModelSendResult";
  private const string SET_MODEL_ERROR = "setModelError";
  private readonly ConcurrentDictionary<string, TaskCompletionSource<AutomationSendResult>> _pending = new();
  private IBinding? _associatedBinding;

  public string FrontendBoundName => inner.FrontendBoundName;

  public void AssociateWithBinding(IBinding binding)
  {
    ArgumentNullException.ThrowIfNull(binding);

    if (ReferenceEquals(_associatedBinding, binding))
    {
      return;
    }

    if (_associatedBinding is not null)
    {
      throw new ArgumentException("Bridge is already associated with a different binding.", nameof(binding));
    }

    _associatedBinding = binding;
    inner.AssociateWithBinding(new InnerBridgeBinding(binding.Name, inner));
  }

  public string[] GetBridgeCapabilities() => inner.GetBridgeCapabilities();

  public string[] GetBindingsMethodNames() => inner.GetBindingsMethodNames();

  public void RunMethod(string methodName, string requestId, string args) =>
    inner.RunMethodTraced(methodName, requestId, args, null);

  public void RunMethodTraced(string methodName, string requestId, string args, string? otelTraceContext) =>
    inner.RunMethodTraced(methodName, requestId, args, otelTraceContext);

  public Task Send(string eventName, CancellationToken cancellationToken = default) =>
    inner.Send(eventName, cancellationToken);

  public Task Send<T>(string eventName, T data, CancellationToken cancellationToken = default)
    where T : class
  {
    if (eventName == SET_MODEL_SEND_RESULT)
    {
      var modelCardId = ReadStringProperty(data, "modelCardId");
      var versionId = ReadStringProperty(data, "versionId");
      if (!string.IsNullOrWhiteSpace(modelCardId) && _pending.TryGetValue(modelCardId, out var tcs))
      {
        tcs.TrySetResult(new AutomationSendResult(true, versionId, null));
      }
    }
    else if (eventName == SET_MODEL_ERROR)
    {
      var modelCardId = ReadStringProperty(data, "modelCardId");
      var error = ReadStringProperty(data, "error");
      if (!string.IsNullOrWhiteSpace(modelCardId) && _pending.TryGetValue(modelCardId, out var tcs))
      {
        tcs.TrySetResult(new AutomationSendResult(false, null, error ?? "Unknown publish error"));
      }
    }

    return inner.Send(eventName, data, cancellationToken);
  }

  public void SendProgress<T>(string eventName, T data, CancellationToken cancellationToken = default)
    where T : class => inner.SendProgress(eventName, data, cancellationToken);

  public Task<AutomationSendResult> WaitForCompletion(string modelCardId, CancellationToken cancellationToken = default)
  {
    if (string.IsNullOrWhiteSpace(modelCardId))
    {
      throw new ArgumentException("Model card ID cannot be null or whitespace.", nameof(modelCardId));
    }

    var tcs = _pending.GetOrAdd(
      modelCardId,
      _ => new TaskCompletionSource<AutomationSendResult>(TaskCreationOptions.RunContinuationsAsynchronously)
    );

    if (cancellationToken.CanBeCanceled)
    {
      cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
    }

    return tcs.Task;
  }

  public void Clear(string modelCardId)
  {
    if (!string.IsNullOrWhiteSpace(modelCardId))
    {
      _pending.TryRemove(modelCardId, out _);
    }
  }

  private static string? ReadStringProperty<T>(T data, string propertyName)
    where T : class => data.GetType().GetProperty(propertyName)?.GetValue(data) as string;

  private sealed class InnerBridgeBinding(string name, IBrowserBridge parent) : IBinding
  {
    public string Name { get; } = name;
    public IBrowserBridge Parent { get; } = parent;
  }
}

public sealed record AutomationSendResult(bool Succeeded, string? VersionId, string? ErrorMessage);
