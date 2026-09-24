using Azure.Core;
using Azure.Identity;

namespace Bevdox.Auth;

public sealed class AuthService
{
    private readonly TokenCredential _credential;

    public AuthService()
    {
        _credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
        {
            ExcludeBrokerCredential = false
        });
    }

    public AuthService(TokenCredential credential) => _credential = credential;

    public async Task<string> GetTokenAsync(string scope, CancellationToken ct = default)
    {
        var context = new TokenRequestContext([scope]);
        var token = await _credential.GetTokenAsync(context, ct);
        return token.Token;
    }

    public Task<string> GetArmTokenAsync(CancellationToken ct = default)
        => GetTokenAsync("https://management.azure.com/.default", ct);

    public Task<string> GetDevCenterTokenAsync(CancellationToken ct = default)
        => GetTokenAsync("https://devcenter.azure.com/.default", ct);
}
