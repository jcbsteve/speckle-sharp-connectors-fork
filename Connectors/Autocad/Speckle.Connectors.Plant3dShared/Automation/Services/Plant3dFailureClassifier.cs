using System.Net;

namespace Speckle.Connectors.Plant3dShared.Automation;

/// <summary>
/// The single place that decides whether a publish failure is worth retrying.
/// </summary>
public static class Plant3dFailureClassifier
{
  private static readonly string[] s_fatalMarkers = ["401", "403", "unauthorized", "forbidden"];

  private static readonly string[] s_transientMarkers =
  [
    "timeout",
    "timed out",
    "408",
    "429",
    "500",
    "502",
    "503",
    "504",
    "too many requests",
    "service unavailable",
    "bad gateway",
    "connection",
    "network",
  ];

  /// <summary>Classifies an exception thrown while checking or publishing a drawing.</summary>
  public static FailureKind Classify(Exception exception) =>
    exception switch
    {
      HttpRequestException { StatusCode: { } status } => ClassifyStatus(status),
      // HttpClient reports its own timeout as a TaskCanceledException.
      TimeoutException or HttpRequestException or TaskCanceledException => FailureKind.Transient,
      _ => Classify(exception.Message),
    };

  /// <summary>
  /// Classifies a send error that is only available as a message. Unknown errors, such as conversion
  /// failures, are fatal so they are not retried.
  /// </summary>
  public static FailureKind Classify(string? errorMessage)
  {
    if (string.IsNullOrWhiteSpace(errorMessage))
    {
      return FailureKind.Fatal;
    }

    if (s_fatalMarkers.Any(m => errorMessage!.Contains(m, StringComparison.OrdinalIgnoreCase)))
    {
      return FailureKind.Fatal;
    }

    return s_transientMarkers.Any(m => errorMessage!.Contains(m, StringComparison.OrdinalIgnoreCase))
      ? FailureKind.Transient
      : FailureKind.Fatal;
  }

  private static FailureKind ClassifyStatus(HttpStatusCode status) =>
    status is HttpStatusCode.RequestTimeout || (int)status == 429 || (int)status >= 500
      ? FailureKind.Transient
      : FailureKind.Fatal;
}
