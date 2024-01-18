using GoCloudNative.Bff.Authentication.IdentityProviders;
using GoCloudNative.Bff.Authentication.Locking;
using Microsoft.AspNetCore.Http;

namespace GoCloudNative.Bff.Authentication.OpenIdConnect;

internal class TokenFactory
{
    private readonly IIdentityProvider _identityProvider;
    private readonly ISession _session;
    private readonly IConcurrentContext _concurrentContext;

    public TokenFactory(IIdentityProvider identityProvider, 
        ISession session,
        IConcurrentContext concurrentContext)
    {
        _identityProvider = identityProvider;
        _session = session;
        _concurrentContext = concurrentContext;
    }

    public async Task RenewAccessTokenIfExpiredAsync<T>(string traceIdentifier)
    {
        await _concurrentContext.ExecuteOncePerSession(_session, 
            nameof(RenewAccessTokenIfExpiredAsync),
            GetIsTokenExpired<T>, 
            async () =>
            {
                // avoid thread collisions without complicated distributed read/write locking mechanisms
                // without this, the the token will be refreshed multiple times
                // i know.. a bit hacky.. but it gets the job done...
                await _session.ProlongExpiryDate<T>(15);

                try
                {
                    var refreshToken = _session.GetRefreshToken<T>(); // todo: What is refresh_token is null?
                    var tokenResponse = await _identityProvider.RefreshTokenAsync(refreshToken, traceIdentifier);
                
                    await _session.UpdateAccessAndRefreshTokenAsync<T>(tokenResponse);

                    // in case of static refresh_tokens requesting a new access token will not always yield a refresh_token
                    if (!string.IsNullOrEmpty(tokenResponse.refresh_token) && refreshToken != tokenResponse.refresh_token) 
                    {
                        await _identityProvider.RevokeAsync(refreshToken, traceIdentifier);
                    }
                }
                catch (Exception e)
                {
                    await _session.ProlongExpiryDate<T>(-15);
                    throw;
                }
            });
    }
    
    private bool GetIsTokenExpired<T>()
    {
        // Todo: Critical section here. The block on line 46 should be within some sort of locking mechanism.
        // hypothetically it's possible another instance/threat updates this value on the exact same second.
        // in that case, the token will be renewed several times.
        var expiryDateInSession = _session.GetExpiryDate<T>();
        if (!expiryDateInSession.HasValue)
        {
            return false;
        }
        
        var expiry = expiryDateInSession.Value.AddSeconds(-30);
        var now = DateTime.UtcNow;

        return expiry <= now;
    }
}