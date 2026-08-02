using Microsoft.AspNetCore.Mvc;
using MesService.Services;
using MesService.Domain;

namespace MesApi.Controllers;

[ApiController]
[Route("api/recipes")]
public class RecipesController : ControllerBase
{
    private readonly RecipeService _service;

    public RecipesController(RecipeService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? productId) =>
        Ok(await _service.GetAllAsync(productId));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var r = await _service.GetByIdAsync(id);
        return r == null ? NotFound() : Ok(_service.ToDto(r));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateRecipeRequest req)
    {
        var recipe = await _service.CreateAsync(new Recipe
        {
            ProductId = req.ProductId,
            Revision = req.Revision,
            Name = req.Name,
            Description = req.Description ?? string.Empty,
            MachineRoute = req.MachineRoute ?? new[] { 0, 1, 2, 3 },
            StationParameters = req.StationParameters ?? new(),
            QualitySpec = req.QualitySpec ?? new()
        });
        return CreatedAtAction(nameof(GetById), new { id = recipe.Id }, _service.ToDto(recipe));
    }

    [HttpPost("{id:guid}/approve")]
    public async Task<IActionResult> Approve(Guid id, [FromBody] ApproveRecipeRequest req)
    {
        await _service.ApproveAsync(id, req.ApprovedBy);
        return Ok(new { message = "Recipe approved and activated." });
    }
}

public record CreateRecipeRequest(
    string ProductId, string Revision, string Name, string? Description,
    int[]? MachineRoute,
    Dictionary<string, object>? StationParameters,
    Dictionary<string, object>? QualitySpec);

public record ApproveRecipeRequest(string ApprovedBy);
