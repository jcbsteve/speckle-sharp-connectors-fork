namespace Speckle.Connectors.Plant3dShared.Automation;

/// <summary>
/// Thrown when the mappings, account or project are invalid, so no drawing can be attempted.
/// </summary>
public sealed class Plant3dConfigException : Exception
{
  public Plant3dConfigException() { }

  public Plant3dConfigException(string message)
    : base(message) { }

  public Plant3dConfigException(string message, Exception innerException)
    : base(message, innerException) { }
}
