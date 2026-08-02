using Microsoft.AspNetCore.Mvc;
using KnowledgeBaseService.Services;
using KnowledgeBaseService.Domain;
using System.Text.Json;

namespace KnowledgeBaseService.Controllers;

[ApiController]
[Route("api/knowledge")]
public class ErrorKnowledgeController : ControllerBase
{
    private readonly ErrorKnowledgeService _service;

    public ErrorKnowledgeController(ErrorKnowledgeService service) => _service = service;

    [HttpGet("alarm/{alarmCode}")]
    public async Task<IActionResult> GetByAlarm(string alarmCode, [FromQuery] string? machineType)
    {
        var dto = await _service.GetDtoAsync(alarmCode, machineType);
        return dto == null ? NotFound() : Ok(dto);
    }

    [HttpGet("alarm/{alarmCode}/steps")]
    public async Task<IActionResult> GetSteps(string alarmCode, [FromQuery] string machineType = "Unknown")
    {
        var steps = await _service.GetResolutionStepsAsync(alarmCode, machineType);
        return Ok(steps.Select(s => new
        {
            code = alarmCode,
            stepIndex = s.Step,
            description = s.Description,
            toolRequired = s.ToolRequired,
            requiresAck = s.RequiresAcknowledgement
        }));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateKnowledgeEntryRequest req)
    {
        var entry = await _service.CreateAsync(new ErrorKnowledgeEntry
        {
            AlarmCode = req.AlarmCode,
            MachineType = req.MachineType,
            Title = req.Title,
            Symptoms = req.Symptoms?.ToList() ?? new(),
            ResolutionSteps = req.ResolutionSteps?.Select((s, i) =>
                new ResolutionStep(i + 1, s.Description, s.ToolRequired, s.RequiresAck)).ToList() ?? new(),
            EstimatedResolutionMinutes = req.EstimatedMinutes
        });
        return Ok(new { id = entry.Id });
    }

    [HttpPost("outcomes")]
    public async Task<IActionResult> RecordOutcome([FromBody] RecordOutcomeRequest req)
    {
        await _service.RecordOutcomeAsync(
            req.AlarmCode, req.MachineId, req.StepsCompleted,
            req.WasSuccessful, req.TimeToResolveMinutes, req.OperatorId, req.Notes);
        return Ok(new { recorded = true });
    }
}

public record CreateKnowledgeEntryRequest(
    string AlarmCode, string? MachineType, string Title,
    IReadOnlyList<string>? Symptoms,
    IReadOnlyList<StepRequest>? ResolutionSteps,
    int? EstimatedMinutes);

public record StepRequest(string Description, string? ToolRequired, bool RequiresAck);

public record RecordOutcomeRequest(
    string AlarmCode, int MachineId,
    IReadOnlyList<string> StepsCompleted, bool WasSuccessful,
    int? TimeToResolveMinutes, Guid? OperatorId, string? Notes);
