using Microsoft.AspNetCore.Mvc;
using MachineService.PlcEngine;
using XtsContracts.Dtos;

namespace MachineService.Controllers;

[ApiController]
[Route("api/machine/{machineId:int}")]
public class MachineController : ControllerBase
{
    private readonly MachinePlcEngine _engine;
    private readonly MachineCompletionTracker _tracker;

    public MachineController(MachinePlcEngine engine, MachineCompletionTracker tracker)
    {
        _engine = engine;
        _tracker = tracker;
    }

    [HttpGet("status")]
    public IActionResult GetStatus(int machineId)
    {
        var dto = new MachineDto(
            _engine.MachineId,
            $"Machine-{_engine.MachineId}",
            _engine.Type.ToString(),
            _engine.SequencerState.ToString(),
            !_engine.FaultActive,
            _engine.FaultActive,
            _engine.FaultMessage,
            _engine.PartsEntered,
            _engine.PartsExited,
            _engine.Stations.Select(s => new StationDto(
                s.Definition.Id, s.Definition.Name, s.Definition.Type,
                s.Status.ToString(), s.Definition.ProcessTime, s.ElapsedTime,
                s.CurrentPartTracking)).ToList().AsReadOnly(),
            _engine.CurrentStationIndex,
            _engine.IsIndexing,
            _engine.RotaryAngle);
        return Ok(dto);
    }

    [HttpPost("load")]
    public IActionResult LoadPart(int machineId, [FromBody] LoadPartRequest request)
    {
        var success = _engine.LoadPart(request.PartId, request.TrackingNumber, request.HasDefect);
        return success ? Ok(new { success = true }) : BadRequest(new { success = false, message = "Machine not ready" });
    }

    [HttpGet("completion-status")]
    public IActionResult GetCompletionStatus(int machineId)
    {
        var record = _tracker.GetCompletion(_engine.CurrentPart?.TrackingNumber ?? string.Empty);
        if (record != null)
            return Ok(record);

        if (_engine.IsComplete)
        {
            var completion = new CompletionRecord(true, false, string.Empty, _engine.HasDefect, "Processed", new List<string>());
            return Ok(completion);
        }

        if (_engine.FaultActive)
        {
            return Ok(new CompletionRecord(false, true, _engine.FaultMessage, false, string.Empty, new List<string>()));
        }

        return Ok(new CompletionRecord(false, false, string.Empty, false, string.Empty, new List<string>()));
    }

    [HttpPost("reset")]
    public IActionResult Reset(int machineId)
    {
        _engine.ResetFault();
        return Ok(new { success = true });
    }
}

public record LoadPartRequest(string PartId, string TrackingNumber, bool HasDefect);
