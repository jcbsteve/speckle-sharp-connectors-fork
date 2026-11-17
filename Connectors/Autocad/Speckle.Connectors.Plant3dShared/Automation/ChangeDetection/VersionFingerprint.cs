using System.Globalization;
using System.Text.RegularExpressions;

namespace Speckle.Connectors.Plant3dShared.Automation;

/// <summary>
/// The fingerprint stored in a published version's message, e.g. <c>[speckle-plant3d sha256=HEX schema=1]</c>.
/// </summary>
/// <param name="ContentHash">Uppercase hex SHA-256 of the drawing file.</param>
/// <param name="Schema">The <see cref="Plant3dPublishSchema.VERSION"/> the version was published with.</param>
public sealed record VersionFingerprint(string ContentHash, int Schema)
{
  private static readonly Regex s_tag = new(
    @"\[speckle-plant3d sha256=(?<hash>[0-9A-Fa-f]{64}) schema=(?<schema>\d+)\]",
    RegexOptions.Compiled | RegexOptions.CultureInvariant,
    TimeSpan.FromSeconds(1)
  );

  public string ToTag() =>
    $"[speckle-plant3d sha256={ContentHash} schema={Schema.ToString(CultureInfo.InvariantCulture)}]";

  /// <summary>Parses the fingerprint tag from a version message, or returns null when there is none.</summary>
  public static VersionFingerprint? TryParse(string? message)
  {
    if (message is null || s_tag.Match(message) is not { Success: true } match)
    {
      return null;
    }

    return int.TryParse(match.Groups["schema"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var schema)
      ? new VersionFingerprint(match.Groups["hash"].Value.ToUpperInvariant(), schema)
      : null;
  }
}
