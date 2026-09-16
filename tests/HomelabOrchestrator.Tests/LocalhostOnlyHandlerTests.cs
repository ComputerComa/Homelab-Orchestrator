using System.Net;
using HomelabOrchestrator.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace HomelabOrchestrator.Tests;

public class LocalhostOnlyHandlerTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    [InlineData("::ffff:127.0.0.1")]
    public async Task Succeeds_for_loopback_addresses(string address)
    {
        var succeeded = await EvaluateAsync(address);

        Assert.True(succeeded);
    }

    [Theory]
    [InlineData("10.0.150.5")]
    [InlineData("192.168.1.1")]
    [InlineData("::ffff:10.0.150.5")]
    public async Task Fails_for_non_loopback_addresses(string address)
    {
        var succeeded = await EvaluateAsync(address);

        Assert.False(succeeded);
    }

    [Fact]
    public async Task Fails_when_the_remote_address_is_unknown()
    {
        var handler = new LocalhostOnlyHandler(new FakeHttpContextAccessor(null));
        var context = new AuthorizationHandlerContext([new LocalhostOnlyRequirement()], new System.Security.Claims.ClaimsPrincipal(), null);

        await handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    private static async Task<bool> EvaluateAsync(string address)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse(address);

        var handler = new LocalhostOnlyHandler(new FakeHttpContextAccessor(httpContext));
        var context = new AuthorizationHandlerContext([new LocalhostOnlyRequirement()], new System.Security.Claims.ClaimsPrincipal(), null);

        await handler.HandleAsync(context);

        return context.HasSucceeded;
    }

    private sealed class FakeHttpContextAccessor(HttpContext? httpContext) : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get => httpContext; set => throw new NotSupportedException(); }
    }
}
