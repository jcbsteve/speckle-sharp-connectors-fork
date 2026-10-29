namespace Speckle.Connectors.Plant3dShared.Automation;

/// <summary>
/// Thrown when a <see cref="Plant3dPublishDocumentScope"/> fails to open or activate the requested drawing.
/// </summary>
public sealed class Plant3dPublishDocumentScopeException : Exception
{
  public Plant3dPublishDocumentScopeException() { }

  public Plant3dPublishDocumentScopeException(string message)
    : base(message) { }

  public Plant3dPublishDocumentScopeException(string message, Exception innerException)
    : base(message, innerException) { }
}
