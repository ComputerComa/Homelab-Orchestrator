using System.Text.Json;
using HomelabOrchestrator.Authentication;
using HomelabOrchestrator.Services.Ssh;
using Microsoft.Net.Http.Headers;

namespace HomelabOrchestrator.Endpoints;

/// <summary>
/// The one enrollment endpoint a new device calls with its short-lived one-time token — thin HTTP
/// binding only, every actual rule (algorithm allowlisting, duplicate rejection, private-key
/// rejection, token consumption) lives behind <see cref="ISshKeyManagementService"/>. Never grants
/// access and never triggers synchronization by itself; it only ever saves a new key as Pending.
/// </summary>
public static class SshKeyEndpoints
{
    private const long MaxRequestBodyBytes = 8 * 1024;

    public static IEndpointRouteBuilder MapSshKeyEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/ssh-keys")
            .RequireAuthorization("SshEnrollmentToken")
            .RequireRateLimiting("SshEnrollment");

        group.MapPost("/enroll", EnrollAsync);

        return app;
    }

    private record EnrollRequestBody(string? DeviceName, string? PublicKey);

    private static async Task<IResult> EnrollAsync(HttpContext httpContext, ISshKeyManagementService keyManagement, CancellationToken cancellationToken)
    {
        var bodySizeFeature = httpContext.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
        if (bodySizeFeature is { IsReadOnly: false })
        {
            bodySizeFeature.MaxRequestBodySize = MaxRequestBodyBytes;
        }

        EnrollRequestBody? body;
        try
        {
            body = await httpContext.Request.ReadFromJsonAsync<EnrollRequestBody>(cancellationToken);
        }
        catch (BadHttpRequestException)
        {
            // Thrown by Kestrel when the body exceeds MaxRequestBodySize above.
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }
        catch (JsonException)
        {
            return Results.BadRequest(new { error = "The request body is not valid JSON." });
        }

        if (body is null)
        {
            return Results.BadRequest(new { error = "A JSON body with deviceName and publicKey is required." });
        }

        // The authorization policy above already confirmed this header names a currently-valid
        // token; re-read it here only so EnrollAsync can perform the actual single-use burn once
        // the rest of the request is known-good.
        var tokenPlaintext = ExtractBearerToken(httpContext.Request);
        if (tokenPlaintext is null)
        {
            return Results.Unauthorized();
        }

        var result = await keyManagement.EnrollAsync(tokenPlaintext, body.DeviceName, body.PublicKey, cancellationToken);
        if (result.Succeeded)
        {
            return Results.Created($"/api/ssh-keys/enroll", new { fingerprint = result.Fingerprint });
        }

        return result.FailureReason switch
        {
            EnrollmentFailureReason.InvalidToken or EnrollmentFailureReason.TokenExpiredOrUsed =>
                Results.Json(new { error = "The enrollment token is invalid, expired, or already used." }, statusCode: StatusCodes.Status401Unauthorized),
            EnrollmentFailureReason.DuplicateFingerprint =>
                Results.Json(new { error = "A key with this fingerprint is already enrolled." }, statusCode: StatusCodes.Status409Conflict),
            EnrollmentFailureReason.InvalidDeviceName =>
                Results.BadRequest(new { error = "deviceName must be 1-64 characters using letters, numbers, spaces, '.', '_', or '-'." }),
            EnrollmentFailureReason.PrivateKeyRejected =>
                Results.BadRequest(new { error = "publicKey looks like private key material, or contains more than one line. Submit only the single public-key line." }),
            EnrollmentFailureReason.UnsupportedAlgorithm =>
                Results.BadRequest(new { error = "publicKey's algorithm is not supported. Use ssh-ed25519 (preferred), ECDSA, or RSA with a modulus of at least 3072 bits." }),
            EnrollmentFailureReason.MalformedKey =>
                Results.BadRequest(new { error = "publicKey is not a valid OpenSSH public key line." }),
            _ => Results.BadRequest(new { error = "The enrollment request could not be processed." }),
        };
    }

    private static string? ExtractBearerToken(HttpRequest request)
    {
        if (!request.Headers.TryGetValue(HeaderNames.Authorization, out var headerValue))
        {
            return null;
        }

        var header = headerValue.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var token = header["Bearer ".Length..].Trim();
        return token.Length == 0 ? null : token;
    }
}
