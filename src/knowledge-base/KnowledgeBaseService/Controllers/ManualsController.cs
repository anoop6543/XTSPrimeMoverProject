using Microsoft.AspNetCore.Mvc;
using KnowledgeBaseService.Services;
using KnowledgeBaseService.Domain;
using System.Text.Json;

namespace KnowledgeBaseService.Controllers;

[ApiController]
[Route("api/manuals")]
public class ManualsController : ControllerBase
{
    private readonly ManualService _service;

    public ManualsController(ManualService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? machineType, [FromQuery] string? category) =>
        Ok(await _service.GetAllAsync(machineType, category));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var section = await _service.GetByIdAsync(id);
        return section == null ? NotFound() : Ok(_service.ToDto(section));
    }

    [HttpGet("contextual")]
    public async Task<IActionResult> GetContextual(
        [FromQuery] string? machineType,
        [FromQuery] int? stationId,
        [FromQuery] string? alarmCode,
        [FromQuery] string? operationCategory)
    {
        var section = await _service.GetContextualSectionAsync(
            machineType, stationId, alarmCode, operationCategory);
        return section == null
            ? NotFound(new { message = "No contextual manual section found for current state." })
            : Ok(_service.ToDto(section));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateManualSectionRequest req)
    {
        var section = await _service.CreateAsync(new ManualSection
        {
            MachineType = req.MachineType,
            StationId = req.StationId,
            Category = req.Category,
            Title = req.Title,
            Content = JsonDocument.Parse(req.ContentJson ?? "{}"),
            Tags = req.Tags?.ToList() ?? new(),
            SkillLevel = req.SkillLevel,
            Language = req.Language ?? "en"
        });
        return CreatedAtAction(nameof(GetById), new { id = section.Id }, _service.ToDto(section));
    }
}

public record CreateManualSectionRequest(
    string? MachineType, int? StationId, string Category, string Title,
    string? ContentJson, IReadOnlyList<string>? Tags,
    string? SkillLevel, string? Language);
