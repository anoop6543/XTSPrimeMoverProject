using Microsoft.AspNetCore.Mvc;
using MesService.Services;

namespace MesApi.Controllers;

[ApiController]
[Route("api/scheduling")]
public class SchedulingController : ControllerBase
{
    private readonly SchedulingService _service;

    public SchedulingController(SchedulingService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> GetSchedule([FromQuery] string? date)
    {
        var target = DateOnly.TryParse(date, out var d) ? d : DateOnly.FromDateTime(DateTime.Today);
        return Ok(await _service.GetScheduleAsync(target));
    }

    [HttpPost]
    public async Task<IActionResult> Schedule([FromBody] ScheduleRequest req)
    {
        var entry = await _service.ScheduleWorkOrderAsync(
            req.WorkOrderId, req.MachineId, req.ScheduledStart, req.ScheduledEnd, req.ShiftName);
        return Ok(entry);
    }

    [HttpPost("shift-snapshot")]
    public async Task<IActionResult> ShiftSnapshot([FromBody] ShiftSnapshotRequest req)
    {
        var result = await _service.RecordShiftSnapshotAsync(
            req.ShiftId, req.ShiftName, req.SnapshotTime);
        return Ok(new
        {
            partsCompleted = result.PartsCompleted,
            alarmsThisShift = result.AlarmsThisShift,
            totalUptimeSeconds = result.TotalUptime.TotalSeconds,
            handoff = result.Handoff
        });
    }
}

public record ScheduleRequest(
    Guid WorkOrderId, int MachineId,
    DateTime ScheduledStart, DateTime ScheduledEnd, string ShiftName);

public record ShiftSnapshotRequest(Guid ShiftId, string ShiftName, DateTime SnapshotTime);
