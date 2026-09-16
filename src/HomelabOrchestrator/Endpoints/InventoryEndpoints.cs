using HomelabOrchestrator.Services.Ansible;
using HomelabOrchestrator.Services.Proxmox;

namespace HomelabOrchestrator.Endpoints;

/// <summary>
/// Read-only Ansible dynamic-inventory endpoints. Thin HTTP binding only — all the actual work
/// happens behind <see cref="IAnsibleInventoryService"/>, never Proxmox directly.
/// </summary>
public static class InventoryEndpoints
{
    public static IEndpointRouteBuilder MapInventoryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/inventory");

        group.MapGet("/containers/running", GetRunningContainersAsync);
        group.MapGet("/containers/{vmid:int}", GetContainerAsync);

        return app;
    }

    private static async Task<IResult> GetContainerAsync(int vmid, IAnsibleInventoryService inventory, CancellationToken cancellationToken)
    {
        try
        {
            var result = await inventory.GetContainerInventoryAsync(vmid, cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }
        catch (ProxmoxOperationException ex)
        {
            return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static async Task<IResult> GetRunningContainersAsync(IAnsibleInventoryService inventory, CancellationToken cancellationToken)
    {
        try
        {
            return Results.Ok(await inventory.GetRunningContainersInventoryAsync(cancellationToken));
        }
        catch (ProxmoxOperationException ex)
        {
            return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }
}
