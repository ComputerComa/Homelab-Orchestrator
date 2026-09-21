using System.Security.Claims;
using System.Text.Encodings.Web;
using HomelabOrchestrator.Services.Ssh;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace HomelabOrchestrator.Authentication;

/// <summary>
/// A second, additional authentication scheme, entirely separate from the cookie scheme every
/// other page/endpoint uses. Authenticates a single <c>Authorization: Bearer &lt;token&gt;</c>
/// header against <see cref="ISshEnrollmentTokenStore.IsValidAsync"/> (read-only — this never
/// consumes the token; that happens only once <see cref="ISshKeyManagementService.EnrollAsync"/>
/// has validated the rest of the request). On success it produces a claims-free identity that is
/// authorized for nothing beyond the "SshEnrollmentToken" policy — it carries no username, no
/// role, and grants no access to any other endpoint or page.
/// </summary>
public class EnrollmentTokenAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ISshEnrollmentTokenStore tokenStore) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "SshEnrollmentToken";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderNames.Authorization, out var headerValue))
        {
            return AuthenticateResult.NoResult();
        }

        var header = headerValue.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.Fail("Expected an 'Authorization: Bearer <token>' header.");
        }

        var token = header["Bearer ".Length..].Trim();
        if (token.Length == 0)
        {
            return AuthenticateResult.Fail("The bearer token was empty.");
        }

        var tokenHash = SshEnrollmentTokenHasher.Hash(token);
        var isValid = await tokenStore.IsValidAsync(tokenHash, DateTime.UtcNow, Context.RequestAborted);
        if (!isValid)
        {
            return AuthenticateResult.Fail("The enrollment token is invalid, expired, or already used.");
        }

        var identity = new ClaimsIdentity(SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return AuthenticateResult.Success(ticket);
    }
}
