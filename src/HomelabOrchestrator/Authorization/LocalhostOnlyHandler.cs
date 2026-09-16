using System.Net;
using Microsoft.AspNetCore.Authorization;

namespace HomelabOrchestrator.Authorization;

/// <summary>
/// Reads the real remote endpoint off <see cref="IHttpContextAccessor"/> rather than
/// <see cref="AuthorizationHandlerContext.Resource"/> — endpoint-routing authorization passes the
/// matched <c>Endpoint</c> as the resource, not the request, so relying on it here would make this
/// requirement impossible to satisfy and fail closed for everyone, including localhost itself.
/// </summary>
public class LocalhostOnlyHandler(IHttpContextAccessor httpContextAccessor) : AuthorizationHandler<LocalhostOnlyRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, LocalhostOnlyRequirement requirement)
    {
        var remoteIp = httpContextAccessor.HttpContext?.Connection.RemoteIpAddress;
        if (remoteIp is not null)
        {
            // A dual-stack listener reports an IPv4 client as an IPv4-mapped IPv6 address
            // (::ffff:127.0.0.1); unmap it before checking, or a local caller would be rejected.
            var normalized = remoteIp.IsIPv4MappedToIPv6 ? remoteIp.MapToIPv4() : remoteIp;
            if (IPAddress.IsLoopback(normalized))
            {
                context.Succeed(requirement);
            }
        }

        return Task.CompletedTask;
    }
}
