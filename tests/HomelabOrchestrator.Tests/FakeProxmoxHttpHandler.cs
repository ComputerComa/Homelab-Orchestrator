using System.Net;
using System.Text;

namespace HomelabOrchestrator.Tests;

/// <summary>
/// Stands in for the Proxmox HTTP API so <see cref="HomelabOrchestrator.Services.Proxmox.ProxmoxService"/>
/// can be exercised without a live server. Routes on the request path and returns canned,
/// Proxmox-shaped JSON envelopes (the same "{ data: ... }" / "{ errors: ... }" shapes the real API uses).
/// </summary>
public class FakeProxmoxHttpHandler : HttpMessageHandler
{
    public Func<HttpRequestMessage, (HttpStatusCode StatusCode, string Json)> Respond { get; set; } =
        _ => (HttpStatusCode.NotFound, """{"data":null}""");

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var (statusCode, json) = Respond(request);
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        return Task.FromResult(response);
    }

    public static HttpClient BuildClient(Func<HttpRequestMessage, (HttpStatusCode, string)> respond) =>
        new(new FakeProxmoxHttpHandler { Respond = respond });
}
