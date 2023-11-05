using GoCloudNative.Bff.Authentication.IdentityProviders;
using GoCloudNative.Bff.Authentication.Logging;
using GoCloudNative.Bff.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace GoCloudNative.Bff.Authentication.Middleware;

internal class TokenRenewalMiddleware<TIdentityProvider> : ITokenRenewalMiddleware
    where TIdentityProvider : IIdentityProvider
{
    private readonly TIdentityProvider _identityProvider;
    private readonly ILogger<TokenRenewalMiddleware<TIdentityProvider>> _logger;

    public TokenRenewalMiddleware(TIdentityProvider identityProvider, ILogger<TokenRenewalMiddleware<TIdentityProvider>> logger)
    {
        _identityProvider = identityProvider;
        _logger = logger;
    }
    
    public async Task Apply(HttpContext context, Func<HttpContext, Task> next)
    {
        var factory = new TokenFactory(_identityProvider, context.Session);

        try
        {
            if (await factory.RenewAccessTokenIfExpiredAsync<TIdentityProvider>())
            {   
                _logger.LogLine(context, "Renewed access_token and refresh_token");
            }
        }
        catch (TokenRenewalFailedException e)
        {
            _logger.LogException(context, e);

            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync(@"{ ""reason"": ""token_renewal_failed"" }");
            return;
        }
        
        await next(context);
    }
}