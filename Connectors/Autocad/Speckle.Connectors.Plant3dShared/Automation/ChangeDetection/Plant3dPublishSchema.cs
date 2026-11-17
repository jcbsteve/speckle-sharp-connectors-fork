namespace Speckle.Connectors.Plant3dShared.Automation;

/// <summary>
/// Version of the published output produced by the Plant 3D automation.
/// </summary>
/// <remarks>
/// Increment <see cref="VERSION"/> whenever converter output, root object structure or published properties change
/// in a way that requires every mapped drawing to be republished. The value is written to each published version's
/// message fingerprint.
/// </remarks>
public static class Plant3dPublishSchema
{
  public const int VERSION = 1;
}
