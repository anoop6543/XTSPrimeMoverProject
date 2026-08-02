using Microsoft.AspNetCore.Mvc;
using MaintenanceService.Services;
using MaintenanceService.Domain;

namespace MaintenanceService.Controllers;

[ApiController]
[Route("api/maintenance/work-orders")]
public class MaintenanceWorkOrdersController : ControllerBase
{
    private readonly CmmsService _service;

    public MaintenanceWorkOrdersController(CmmsService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? status, [FromQuery] int? machineId) =>
        Ok(await _service.GetAllAsync(status, machineId));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var wo = await _service.GetByIdAsync(id);
        return wo == null ? NotFound() : Ok(_service.ToDto(wo));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateMaintenanceWoRequest req)
    {
        var wo = await _service.CreateAsync(new MaintenanceWorkOrder
        {
            MachineId = req.MachineId,
            StationId = req.StationId,
            MaintenanceType = req.MaintenanceType,
            Description = req.Description,
            Priority = req.Priority ?? "Normal",
            ScheduledAt = req.ScheduledAt,
            AssignedTo = req.AssignedTo,
            TriggerType = req.TriggerType ?? "Manual",
            TriggerId = req.TriggerId
        });
        return CreatedAtAction(nameof(GetById), new { id = wo.Id },
            new { id = wo.Id, workOrderNumber = wo.WorkOrderNumber });
    }

    [HttpPatch("{id:guid}/status")]
    public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] UpdateMaintenanceStatusRequest req)
    {
        await _service.UpdateStatusAsync(id, req.Status, req.Notes);
        return NoContent();
    }
}

[ApiController]
[Route("api/maintenance/tools")]
public class ToolLifeController : ControllerBase
{
    private readonly ToolLifeService _service;

    public ToolLifeController(ToolLifeService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> GetActive([FromQuery] int? machineId) =>
        Ok(await _service.GetActiveToolsAsync(machineId));

    [HttpPost]
    public async Task<IActionResult> Register([FromBody] RegisterToolRequest req)
    {
        var tool = await _service.RegisterToolAsync(new ToolLifeRecord
        {
            MachineId = req.MachineId,
            StationId = req.StationId,
            ToolName = req.ToolName,
            ToolSerial = req.ToolSerial,
            MaxCycleCount = req.MaxCycleCount,
            AlertThresholdPercent = req.AlertThresholdPercent ?? 80
        });
        return Ok(_service.ToDto(tool));
    }

    [HttpPost("{toolId:guid}/replace")]
    public async Task<IActionResult> Replace(Guid toolId, [FromBody] ReplaceToolRequest req)
    {
        await _service.ReplaceToolAsync(toolId, req.NewToolSerial);
        return Ok(new { message = "Tool replaced." });
    }

    [HttpPost("increment-cycle")]
    public async Task<IActionResult> IncrementCycle([FromBody] IncrementCycleRequest req)
    {
        await _service.IncrementCycleCountAsync(req.MachineId, req.StationId);
        return NoContent();
    }
}

[ApiController]
[Route("api/maintenance/sensors")]
public class SensorDataController : ControllerBase
{
    private readonly PredictiveAnalysisService _service;

    public SensorDataController(PredictiveAnalysisService service) => _service = service;

    [HttpPost]
    public async Task<IActionResult> Record([FromBody] RecordSensorRequest req)
    {
        await _service.RecordReadingAsync(req.MachineId, req.StationId, req.SensorType, req.Value);
        return NoContent();
    }

    [HttpGet("{machineId:int}")]
    public async Task<IActionResult> GetRecent(int machineId, [FromQuery] string? sensorType) =>
        Ok(await _service.GetRecentAsync(machineId, sensorType));
}

[ApiController]
[Route("api/maintenance/alerts")]
public class PredictiveAlertsController : ControllerBase
{
    private readonly PredictiveAnalysisService _service;

    public PredictiveAlertsController(PredictiveAnalysisService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? status) =>
        Ok(await _service.GetAlertsAsync(status));

    [HttpPost("{id:guid}/acknowledge")]
    public async Task<IActionResult> Acknowledge(Guid id, [FromBody] AcknowledgeAlertRequest req)
    {
        await _service.AcknowledgeAlertAsync(id, req.AcknowledgedBy);
        return Ok(new { message = "Alert acknowledged." });
    }

    [HttpPost("rules")]
    public async Task<IActionResult> AddRule([FromBody] AddRuleRequest req)
    {
        var rule = await _service.AddRuleAsync(new MaintenanceRule
        {
            MachineId = req.MachineId,
            StationId = req.StationId,
            SensorType = req.SensorType,
            RuleName = req.RuleName,
            ThresholdValue = req.ThresholdValue,
            Comparator = req.Comparator,
            ConsecutiveCyclesRequired = req.ConsecutiveCycles,
            MaintenanceType = req.MaintenanceType,
            AlertSeverity = req.AlertSeverity
        });
        return Ok(rule);
    }
}

public record CreateMaintenanceWoRequest(
    int MachineId, int? StationId, string MaintenanceType, string Description,
    string? Priority, DateTime? ScheduledAt, string? AssignedTo,
    string? TriggerType, string? TriggerId);

public record UpdateMaintenanceStatusRequest(string Status, string? Notes);
public record RegisterToolRequest(int MachineId, int StationId, string ToolName, string? ToolSerial, int MaxCycleCount, int? AlertThresholdPercent);
public record ReplaceToolRequest(string? NewToolSerial);
public record IncrementCycleRequest(int MachineId, int StationId);
public record RecordSensorRequest(int MachineId, int? StationId, string SensorType, double Value);
public record AcknowledgeAlertRequest(string AcknowledgedBy);
public record AddRuleRequest(int MachineId, int? StationId, string SensorType, string RuleName, double ThresholdValue, string Comparator, int ConsecutiveCycles, string MaintenanceType, string AlertSeverity);
