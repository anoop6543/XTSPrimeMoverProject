using Dapper;
using Npgsql;
using MaintenanceService.Domain;
using XtsContracts.Dtos;

namespace MaintenanceService.Services;

/// <summary>
/// Tracks tool wear, increments cycle counts on part completion,
/// and triggers predictive maintenance work orders when threshold is reached.
/// </summary>
public class ToolLifeService
{
    private readonly string _connectionString;
    private readonly CmmsService _cmms;
    private readonly ILogger<ToolLifeService> _logger;

    public ToolLifeService(IConfiguration config, CmmsService cmms, ILogger<ToolLifeService> logger)
    {
        _connectionString = config.GetConnectionString("Postgres")
            ?? "Host=postgresql;Database=maintenance_db;Username=xts;******";
        _cmms = cmms;
        _logger = logger;
    }

    public async Task<IEnumerable<ToolLifeRecord>> GetActiveToolsAsync(int? machineId = null)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        var sql = machineId.HasValue
            ? "SELECT * FROM tool_life_records WHERE is_active = TRUE AND machine_id = @M ORDER BY machine_id, station_id"
            : "SELECT * FROM tool_life_records WHERE is_active = TRUE ORDER BY machine_id, station_id";
        return await conn.QueryAsync<ToolLifeRecord>(sql, new { M = machineId });
    }

    public async Task IncrementCycleCountAsync(int machineId, int stationId)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        var tools = await conn.QueryAsync<ToolLifeRecord>("""
            UPDATE tool_life_records
            SET current_cycle_count = current_cycle_count + 1
            WHERE machine_id = @M AND station_id = @S AND is_active = TRUE
            RETURNING *
            """, new { M = machineId, S = stationId });

        foreach (var tool in tools)
        {
            if (tool.ShouldAlert && !tool.IsExpired)
            {
                _logger.LogWarning(
                    "Tool life alert: Machine={M}, Station={S}, Tool={T}, Wear={W:F1}%",
                    machineId, stationId, tool.ToolName, tool.WearPercent);
            }

            if (tool.IsExpired)
            {
                _logger.LogCritical(
                    "Tool EXPIRED: Machine={M}, Station={S}, Tool={T} — creating WO",
                    machineId, stationId, tool.ToolName);

                await _cmms.CreateAsync(new MaintenanceWorkOrder
                {
                    MachineId = machineId,
                    StationId = stationId,
                    MaintenanceType = "Preventive",
                    Description = $"Tool replacement required: {tool.ToolName} (cycles: {tool.CurrentCycleCount}/{tool.MaxCycleCount})",
                    Priority = "High",
                    TriggerType = "ToolLife",
                    TriggerId = tool.Id.ToString()
                });
            }
        }
    }

    public async Task<ToolLifeRecord> RegisterToolAsync(ToolLifeRecord tool)
    {
        tool.Id = Guid.NewGuid();
        tool.InstalledAt = DateTime.UtcNow;
        tool.IsActive = true;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.ExecuteAsync("""
            INSERT INTO tool_life_records
                (id, machine_id, station_id, tool_name, tool_serial, max_cycle_count,
                 current_cycle_count, installed_at, is_active, alert_threshold_percent)
            VALUES (@Id, @MachineId, @StationId, @ToolName, @ToolSerial, @MaxCycleCount,
                    0, @InstalledAt, TRUE, @AlertThresholdPercent)
            """, tool);
        return tool;
    }

    public async Task ReplaceToolAsync(Guid toolId, string? newToolSerial)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        // Mark old tool as replaced
        var old = await conn.QueryFirstOrDefaultAsync<ToolLifeRecord>(
            "SELECT * FROM tool_life_records WHERE id = @Id", new { Id = toolId });
        if (old == null) return;

        await conn.ExecuteAsync(
            "UPDATE tool_life_records SET is_active = FALSE, replaced_at = NOW() WHERE id = @Id",
            new { Id = toolId });

        // Register new tool same spec
        await RegisterToolAsync(new ToolLifeRecord
        {
            MachineId = old.MachineId, StationId = old.StationId,
            ToolName = old.ToolName, ToolSerial = newToolSerial,
            MaxCycleCount = old.MaxCycleCount,
            AlertThresholdPercent = old.AlertThresholdPercent
        });
    }

    public ToolLifeRecordDto ToDto(ToolLifeRecord t) => new(
        t.Id, t.MachineId, t.StationId, t.ToolName, t.ToolSerial,
        t.MaxCycleCount, t.CurrentCycleCount, t.WearPercent,
        t.InstalledAt, t.ReplacedAt, t.IsActive, t.AlertThresholdPercent);
}
