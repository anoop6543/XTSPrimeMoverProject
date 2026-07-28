using Microsoft.AspNetCore.Mvc;
using XtsContracts.Dtos;

namespace PrimeMoverApi.Controllers;

[ApiController]
[Route("api/recipes")]
public class RecipesController : ControllerBase
{
    // In-memory recipe store (replace with PostgreSQL in production)
    private static readonly List<RecipeDto> _recipes = new()
    {
        new RecipeDto(Guid.NewGuid(), "Standard-LAQF", new[] { 0, 1, 2, 3 }.ToList(), DateTime.UtcNow),
        new RecipeDto(Guid.NewGuid(), "Quick-LQ", new[] { 0, 2 }.ToList(), DateTime.UtcNow),
    };

    [HttpGet]
    public IActionResult GetAll() => Ok(_recipes);

    [HttpGet("{id:guid}")]
    public IActionResult GetById(Guid id)
    {
        var recipe = _recipes.FirstOrDefault(r => r.RecipeId == id);
        return recipe != null ? Ok(recipe) : NotFound();
    }

    [HttpPost]
    public IActionResult Create([FromBody] CreateRecipeRequest request)
    {
        var recipe = new RecipeDto(Guid.NewGuid(), request.Name, request.MachineOrder, DateTime.UtcNow);
        _recipes.Add(recipe);
        return CreatedAtAction(nameof(GetById), new { id = recipe.RecipeId }, recipe);
    }
}

public record CreateRecipeRequest(string Name, IReadOnlyList<int> MachineOrder);
