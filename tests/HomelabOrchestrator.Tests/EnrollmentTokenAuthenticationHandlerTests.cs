using System.Text.Encodings.Web;
using HomelabOrchestrator.Authentication;
using HomelabOrchestrator.Models;
using HomelabOrchestrator.Services.Ssh;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HomelabOrchestrator.Tests;

public class EnrollmentTokenAuthenticationHandlerTests
{
    [Fact]
    public async Task Missing_authorization_header_produces_no_result()
    {
        var result = await AuthenticateAsync(authorizationHeader: null, isValid: (_, _) => true);

        Assert.True(result.None);
    }

    [Fact]
    public async Task Header_without_the_bearer_scheme_fails()
    {
        var result = await AuthenticateAsync("Basic dXNlcjpwYXNz", (_, _) => true);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Empty_bearer_token_fails()
    {
        var result = await AuthenticateAsync("Bearer ", (_, _) => true);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task A_token_the_store_reports_valid_succeeds_with_a_claims_free_identity()
    {
        var result = await AuthenticateAsync("Bearer some-valid-token", (_, _) => true);

        Assert.True(result.Succeeded);
        Assert.Empty(result.Principal!.Claims);
    }

    [Fact]
    public async Task A_token_the_store_reports_invalid_or_expired_fails()
    {
        var result = await AuthenticateAsync("Bearer some-bad-token", (_, _) => false);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task The_presented_token_is_hashed_before_being_checked_against_the_store()
    {
        const string plaintext = "the-real-token";
        var hashSeen = "";

        await AuthenticateAsync($"Bearer {plaintext}", (hash, _) =>
        {
            hashSeen = hash;
            return true;
        });

        Assert.Equal(SshEnrollmentTokenHasher.Hash(plaintext), hashSeen);
        Assert.NotEqual(plaintext, hashSeen);
    }

    private static async Task<AuthenticateResult> AuthenticateAsync(string? authorizationHeader, Func<string, DateTime, bool> isValid)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        await using var provider = services.BuildServiceProvider();

        var handler = new EnrollmentTokenAuthenticationHandler(
            new FakeOptionsMonitor<AuthenticationSchemeOptions>(new AuthenticationSchemeOptions()),
            NullLoggerFactory.Instance,
            UrlEncoder.Default,
            new FakeTokenStore(isValid));

        var context = new DefaultHttpContext { RequestServices = provider };
        if (authorizationHeader is not null)
        {
            context.Request.Headers.Authorization = authorizationHeader;
        }

        var scheme = new AuthenticationScheme(EnrollmentTokenAuthenticationHandler.SchemeName, null, typeof(EnrollmentTokenAuthenticationHandler));
        await handler.InitializeAsync(scheme, context);
        return await handler.AuthenticateAsync();
    }

    private sealed class FakeOptionsMonitor<T>(T currentValue) : IOptionsMonitor<T>
    {
        public T CurrentValue { get; } = currentValue;

        public T Get(string? name) => CurrentValue;

        public IDisposable OnChange(Action<T, string?> listener) => NullDisposable.Instance;

        private sealed class NullDisposable : IDisposable
        {
            public static readonly NullDisposable Instance = new();

            public void Dispose()
            {
            }
        }
    }

    private sealed class FakeTokenStore(Func<string, DateTime, bool> isValid) : ISshEnrollmentTokenStore
    {
        public Task AddAsync(SshEnrollmentToken token, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool> IsValidAsync(string tokenHash, DateTime nowUtc, CancellationToken cancellationToken = default) =>
            Task.FromResult(isValid(tokenHash, nowUtc));

        public Task<bool> TryConsumeAsync(string tokenHash, DateTime nowUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
