using System.Globalization;
using System.Text.RegularExpressions;

namespace Speckle.Connectors.Plant3dShared.Automation;

/// <summary>
/// The fingerprint stored in a published version's message, e.g. <c>[speckle-plant3d sha256=HEX schema=1]</c>.
/// </summary>
/// <param name="ContentHash">Uppercase hex SHA-256 of the drawing file.</param>
/// <param name="Schema">The <see cref="Plant3dPublishSchema.VERSION"/> the version was published with.</param>
/// <param name="RunId">The automated run that published the version, or null when not published by a batch run.</param>
public sealed record VersionFingerprint(string ContentHash, int Schema, string? RunId = null)
{
  private static readonly Regex s_runId = new(
    "^[A-Za-z0-9_-]+$",
    RegexOptions.Compiled | RegexOptions.CultureInvariant,
    TimeSpan.FromSeconds(1)
  );

  private static readonly Regex s_tag = new(
    @"\[speckle-plant3d sha256=(?<hash>[0-9A-Fa-f]{64}) schema=(?<schema>\d+)(?: run=(?<run>[A-Za-z0-9_-]+))?\]",
    RegexOptions.Compiled | RegexOptions.CultureInvariant,
    TimeSpan.FromSeconds(1)
  );

  public string? RunId { get; } =
    RunId is null || s_runId.IsMatch(RunId)
      ? RunId
      : throw new ArgumentException($"Run id '{RunId}' may only contain letters, digits, '_' and '-'.", nameof(RunId));

  public string ToTag() =>
    $"[speckle-plant3d sha256={ContentHash} schema={Schema.ToString(CultureInfo.InvariantCulture)}"
    + (RunId is null ? "]" : $" run={RunId}]");

  /// <summary>Parses the fingerprint tag from a version message, or returns null when there is none.</summary>
  public static VersionFingerprint? TryParse(string? message)
  {
    if (message is null || s_tag.Match(message) is not { Success: true } match)
    {
      return null;
    }

    return int.TryParse(match.Groups["schema"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var schema)
      ? new VersionFingerprint(
        match.Groups["hash"].Value.ToUpperInvariant(),
        schema,
        match.Groups["run"].Success ? match.Groups["run"].Value : null
      )
      : null;
  }
}
