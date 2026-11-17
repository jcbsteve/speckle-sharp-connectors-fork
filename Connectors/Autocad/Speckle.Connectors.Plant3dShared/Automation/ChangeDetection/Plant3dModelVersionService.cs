using Speckle.Sdk.Api;
using Speckle.Sdk.Api.GraphQL.Inputs;
using Speckle.Sdk.Credentials;

namespace Speckle.Connectors.Plant3dShared.Automation;

/// <summary>
/// Reads and updates Speckle model versions through the SDK client.
/// </summary>
public sealed class Plant3dModelVersionService(IClientFactory clientFactory, IAccountManager accountManager)
  : IPlant3dModelVersionService
{
  public async Task<LatestVersionInfo?> GetLatestVersionAsync(
    string accountId,
    string projectId,
    string modelId,
    CancellationToken cancellationToken
  )
  {
    using var client = clientFactory.Create(accountManager.GetAccount(accountId));
    var model = await client
      .Model.GetWithVersions(modelId, projectId, 1, null, null, cancellationToken)
      .ConfigureAwait(false);
    var latest = model.versions.items.FirstOrDefault();
    return latest is null ? null : new LatestVersionInfo(latest.id, latest.message);
  }

  public async Task UpdateMessageAsync(
    string accountId,
    string projectId,
    string versionId,
    string message,
    CancellationToken cancellationToken
  )
  {
    using var client = clientFactory.Create(accountManager.GetAccount(accountId));
    await client
      .Version.Update(new UpdateVersionInput(versionId, projectId, message), cancellationToken)
      .ConfigureAwait(false);
  }
}
