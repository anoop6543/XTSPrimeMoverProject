using Microsoft.AspNetCore.Mvc;
using XtsContracts.Dtos;
using Temporalio.Client;
using XtsContracts.Workflows;

namespace MachineApi.Controllers;

[ApiController]
[Route("machine/{machineId:int}")]
public class MachineApiController : ControllerBase
{
    private readonly IHttpClientFactory _factory;
    private readonly ITemporalClient _temporal;

    public MachineApiController(IHttpClientFactory factory, ITemporalClient temporal)
    {
        _factory = factory;
        _temporal = temporal;
    }

    /// <summary>GET current machine status (proxied from MachineService)</summary>
    [HttpGet("status")]
    public async Task<IActionResult> GetStatus(int machineId)
    {
        var client = _factory.CreateClient("MachineService");
        var status = await client.GetFromJsonAsync<MachineDto>($"/api/machine/{machineId}/status");
        return Ok(status);
    }

    /// <summary>GET machine status from live Temporal workflow query (snapshot-consistent)</summary>
    [HttpGet("status/live")]
    public async Task<IActionResult> GetLiveStatus(int machineId)
    {
        var handle = _temporal.GetWorkflowHandle<IMachineCycleWorkflow>($"machine-cycle-{machineId}");
        var status = await handle.QueryAsync(w => w.GetMachineStatus());
        return Ok(status);
    }

    /// <summary>POST load part into machine</summary>
    [HttpPost("load-part")]
    public async Task<IActionResult> LoadPart(int machineId, [FromBody] LoadPartApiRequest request)
    {
        var workflowId = $"machine-cycle-{machineId}";
        var handle = _temporal.GetWorkflowHandle<IMachineCycleWorkflow>(workflowId);
        await handle.SignalAsync(w => w.SignalPartArrivedAsync(new PartArrivalSignal(
            request.PartId, request.TrackingNumber, request.PartStatus, request.HasDefect, request.CallbackWorkflowId)));
        return Ok(new { queued = true });
    }

    /// <summary>POST reset machine fault</summary>
    [HttpPost("reset")]
    public async Task<IActionResult> Reset(int machineId)
    {
        var client = _factory.CreateClient("MachineService");
        await client.PostAsync($"/api/machine/{machineId}/reset", null);

        var handle = _temporal.GetWorkflowHandle<IMachineCycleWorkflow>($"machine-cycle-{machineId}");
        await handle.SignalAsync(w => w.SignalResetFaultAsync());
        return Ok(new { success = true });
    }
}

public record LoadPartApiRequest(string PartId, string TrackingNumber, string PartStatus, bool HasDefect, string CallbackWorkflowId);
