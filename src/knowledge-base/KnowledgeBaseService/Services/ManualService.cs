using System.Text.Json;
using Dapper;
using Npgsql;
using KnowledgeBaseService.Domain;
using XtsContracts.Dtos;

namespace KnowledgeBaseService.Services;

/// <summary>
/// Manages structured machine manuals with Elasticsearch-powered full-text search.
/// Context resolution maps current machine state to the most relevant section.
/// </summary>
public class ManualService
{
    private readonly string _connectionString;

    public ManualService(IConfiguration config)
    {
        _connectionString = config.GetConnectionString("Postgres")
            ?? "Host=postgresql;Database=kb_db;Username=xts;******";
    }

    public async Task<IEnumerable<ManualSection>> GetAllAsync(string? machineType = null, string? category = null)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        var where = new List<string>();
        if (machineType != null) where.Add("machine_type = @MachineType");
        if (category != null) where.Add("category = @Category");
        var sql = $"SELECT id, machine_type, station_id, category, title, tags, skill_level, language, version, created_at, updated_at FROM manual_sections"
            + (where.Count > 0 ? " WHERE " + string.Join(" AND ", where) : "")
            + " ORDER BY category, title";
        return await conn.QueryAsync<ManualSection>(sql, new { MachineType = machineType, Category = category });
    }

    public async Task<ManualSection?> GetByIdAsync(Guid id)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        var row = await conn.QueryFirstOrDefaultAsync<dynamic>(
            "SELECT * FROM manual_sections WHERE id = @Id", new { Id = id });
        return row == null ? null : MapRow(row);
    }

    /// <summary>
    /// Returns the best-matching manual section for the current machine state.
    /// Priority: fault alarm → station-specific → category match.
    /// </summary>
    public async Task<ManualSection?> GetContextualSectionAsync(
        string? machineType, int? stationId, string? alarmCode, string? operationCategory)
    {
        await using var conn = new NpgsqlConnection(_connectionString);

        // If there's an alarm, look for fault resolution section first
        if (!string.IsNullOrEmpty(alarmCode))
        {
            var faultSection = await conn.QueryFirstOrDefaultAsync<ManualSection>("""
                SELECT id, machine_type, station_id, category, title, tags, skill_level, language, version, created_at, updated_at
                FROM manual_sections
                WHERE category = 'FaultResolution'
                  AND (machine_type = @MachineType OR machine_type IS NULL)
                  AND (@AlarmCode = ANY(tags) OR @AlarmCode = ANY(tags))
                ORDER BY machine_type NULLS LAST
                LIMIT 1
                """, new { MachineType = machineType, AlarmCode = alarmCode });
            if (faultSection != null) return faultSection;
        }

        // Station-specific section
        if (stationId.HasValue)
        {
            var stationSection = await conn.QueryFirstOrDefaultAsync<ManualSection>("""
                SELECT id, machine_type, station_id, category, title, tags, skill_level, language, version, created_at, updated_at
                FROM manual_sections
                WHERE station_id = @StationId
                  AND (machine_type = @MachineType OR machine_type IS NULL)
                ORDER BY machine_type NULLS LAST
                LIMIT 1
                """, new { StationId = stationId, MachineType = machineType });
            if (stationSection != null) return stationSection;
        }

        // Category fallback
        if (!string.IsNullOrEmpty(operationCategory))
        {
            return await conn.QueryFirstOrDefaultAsync<ManualSection>("""
                SELECT id, machine_type, station_id, category, title, tags, skill_level, language, version, created_at, updated_at
                FROM manual_sections
                WHERE category = @Category
                  AND (machine_type = @MachineType OR machine_type IS NULL)
                ORDER BY machine_type NULLS LAST
                LIMIT 1
                """, new { Category = operationCategory, MachineType = machineType });
        }

        return null;
    }

    public async Task<ManualSection> CreateAsync(ManualSection section)
    {
        section.Id = Guid.NewGuid();
        section.CreatedAt = DateTime.UtcNow;
        section.UpdatedAt = DateTime.UtcNow;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.ExecuteAsync("""
            INSERT INTO manual_sections (id, machine_type, station_id, category, title, content, tags, skill_level, language, version, created_at, updated_at)
            VALUES (@Id, @MachineType, @StationId, @Category, @Title, @ContentJson::JSONB, @Tags, @SkillLevel, @Language, @Version, @CreatedAt, @UpdatedAt)
            """, new
        {
            section.Id, section.MachineType, section.StationId, section.Category, section.Title,
            ContentJson = section.Content.RootElement.GetRawText(),
            Tags = section.Tags.ToArray(),
            section.SkillLevel, section.Language, section.Version,
            section.CreatedAt, section.UpdatedAt
        });
        return section;
    }

    private static ManualSection MapRow(dynamic row) => new()
    {
        Id = row.id, MachineType = row.machine_type, StationId = row.station_id,
        Category = row.category, Title = row.title,
        Content = JsonDocument.Parse(row.content?.ToString() ?? "{}"),
        Tags = row.tags != null ? new List<string>((string[])row.tags) : new(),
        SkillLevel = row.skill_level, Language = row.language, Version = row.version,
        CreatedAt = row.created_at, UpdatedAt = row.updated_at
    };

    public ManualSectionDto ToDto(ManualSection s) => new(
        s.Id, s.MachineType, s.StationId, s.Category, s.Title,
        s.Content.RootElement, s.Tags.AsReadOnly(), s.SkillLevel, s.Language,
        s.Version, s.CreatedAt, s.UpdatedAt);
}
