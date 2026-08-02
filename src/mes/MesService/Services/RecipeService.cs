using System.Text.Json;
using Dapper;
using Npgsql;
using MesService.Domain;
using XtsContracts.Dtos;

namespace MesService.Services;

public class RecipeService
{
    private readonly string _connectionString;
    private readonly ILogger<RecipeService> _logger;

    public RecipeService(IConfiguration config, ILogger<RecipeService> logger)
    {
        _connectionString = config.GetConnectionString("Postgres")
            ?? "Host=postgresql;Database=mes_db;Username=xts;******";
        _logger = logger;
    }

    public async Task<IEnumerable<Recipe>> GetAllAsync(string? productId = null)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        var sql = productId == null
            ? "SELECT * FROM recipes ORDER BY product_id, revision"
            : "SELECT * FROM recipes WHERE product_id = @ProductId ORDER BY revision";
        return await conn.QueryAsync<Recipe>(sql, new { ProductId = productId });
    }

    public async Task<Recipe?> GetByIdAsync(Guid id)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        var row = await conn.QueryFirstOrDefaultAsync<dynamic>(
            "SELECT * FROM recipes WHERE id = @Id", new { Id = id });
        return row == null ? null : MapRow(row);
    }

    public async Task<Recipe?> GetActiveForProductAsync(string productId)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        var row = await conn.QueryFirstOrDefaultAsync<dynamic>(
            "SELECT * FROM recipes WHERE product_id = @ProductId AND status = 'Active' LIMIT 1",
            new { ProductId = productId });
        return row == null ? null : MapRow(row);
    }

    public async Task<Recipe> CreateAsync(Recipe recipe)
    {
        recipe.Id = Guid.NewGuid();
        recipe.CreatedAt = DateTime.UtcNow;
        recipe.Status = RecipeStatus.Draft;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.ExecuteAsync("""
            INSERT INTO recipes
                (id, product_id, revision, name, description, machine_route,
                 station_parameters, quality_spec, status, created_at)
            VALUES
                (@Id, @ProductId, @Revision, @Name, @Description, @MachineRouteJson,
                 @StationParamsJson, @QualitySpecJson, @Status, @CreatedAt)
            """, new
        {
            recipe.Id, recipe.ProductId, recipe.Revision, recipe.Name, recipe.Description,
            MachineRouteJson = JsonSerializer.Serialize(recipe.MachineRoute),
            StationParamsJson = JsonSerializer.Serialize(recipe.StationParameters),
            QualitySpecJson = JsonSerializer.Serialize(recipe.QualitySpec),
            Status = recipe.Status.ToString(),
            recipe.CreatedAt
        });

        _logger.LogInformation("Recipe created: {Product} rev {Rev}", recipe.ProductId, recipe.Revision);
        return recipe;
    }

    public async Task ApproveAsync(Guid id, string approvedBy)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        // Deactivate any existing active recipe for the same product
        await conn.ExecuteAsync("""
            UPDATE recipes r2
            SET status = 'Archived'
            FROM recipes r1
            WHERE r1.id = @Id AND r2.product_id = r1.product_id AND r2.status = 'Active'
            """, new { Id = id });

        await conn.ExecuteAsync("""
            UPDATE recipes SET status = 'Active', approved_by = @By, approved_at = @At
            WHERE id = @Id
            """, new { Id = id, By = approvedBy, At = DateTime.UtcNow });
        _logger.LogInformation("Recipe {Id} approved by {By}", id, approvedBy);
    }

    private static Recipe MapRow(dynamic row) => new()
    {
        Id = row.id,
        ProductId = row.product_id,
        Revision = row.revision,
        Name = row.name,
        Description = row.description ?? string.Empty,
        MachineRoute = JsonSerializer.Deserialize<int[]>(row.machine_route.ToString()) ?? new[] { 0, 1, 2, 3 },
        StationParameters = JsonSerializer.Deserialize<Dictionary<string, object>>(row.station_parameters.ToString()) ?? new Dictionary<string, object>(),
        QualitySpec = JsonSerializer.Deserialize<Dictionary<string, object>>(row.quality_spec.ToString()) ?? new Dictionary<string, object>(),
        Status = Enum.Parse<RecipeStatus>(row.status),
        ApprovedBy = row.approved_by,
        ApprovedAt = row.approved_at,
        CreatedAt = row.created_at
    };

    public MesRecipeDto ToDto(Recipe r) => new(
        r.Id, r.ProductId, r.Revision, r.Name, r.Description,
        r.MachineRoute.ToList().AsReadOnly(),
        r.StationParameters.AsReadOnly(),
        r.QualitySpec.AsReadOnly(),
        r.Status.ToString(), r.ApprovedBy, r.ApprovedAt, r.CreatedAt);
}
