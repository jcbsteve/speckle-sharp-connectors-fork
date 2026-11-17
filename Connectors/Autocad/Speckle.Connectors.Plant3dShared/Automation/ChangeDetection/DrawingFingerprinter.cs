using System.Security.Cryptography;

namespace Speckle.Connectors.Plant3dShared.Automation;

/// <summary>
/// Hashes drawing files with read-only shared access, so read-only or locked collaboration cache files can be read.
/// </summary>
public static class DrawingFingerprinter
{
  /// <summary>Streams the file through SHA-256 and returns the uppercase hex hash.</summary>
  public static async Task<string> ComputeHashAsync(string path, CancellationToken cancellationToken)
  {
    await using var stream = new FileStream(
      path,
      FileMode.Open,
      FileAccess.Read,
      FileShare.ReadWrite | FileShare.Delete,
      bufferSize: 81920,
      useAsync: true
    );
    var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
    return Convert.ToHexString(hash);
  }
}
