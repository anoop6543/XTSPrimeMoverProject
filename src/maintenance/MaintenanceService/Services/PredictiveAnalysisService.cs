using Dapper;
using Npgsql;
using MaintenanceService.Domain;
using XtsContracts.Dtos;

namespace MaintenanceService.Services;

/// <summary>
/// Collects sensor data, evaluates it against maintenance rules,
/// and fires predictive alerts when thresholds are crossed.
/// Phase 1: rule-based. Phase 2: ONNX anomaly model.
/// </summary>
public class PredictiveAnalysisService
{
    private readonly string _connectionString;
    private readonly CmmsService _cmms;
    private readonly ILogger<PredictiveAnalysisService> _logger;

    // In-memory rule violation counters (machine+station+sensorType → consecutive count)
    private readonly Dictionary<string, int> _violationCounters = new();

    public PredictiveAnalysisService(
        IConfiguration config, CmmsService cmms,
        ILogger<PredictiveAnalysisService> logger)
    {
        _connectionString = config.GetConnectionString("Postgres")
            ?? "Host=postgresql;Database=maintenance_db;Username=xts;******";
        _cmms = cmms;
        _logger = logger;
    }

    public async Task RecordReadingAsync(int machineId, int? stationId, string sensorType, double value)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.ExecuteAsync("""
            INSERT INTO sensor_readings (machine_id, station_id, sensor_type, value, recorded_at)
            VALUES (@M, @S, @T, @V, NOW())
            """, new { M = machineId, S = stationId, T = sensorType, V = value });

        // Evaluate rules
        await EvaluateRulesAsync(conn, machineId, stationId, sensorType, value);
    }

    public async Task<IEnumerable<SensorReadingDto>> GetRecentAsync(
        int machineId, string? sensorType, int limitPerType = 100)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        var sql = sensorType != null
            ? """
              SELECT * FROM sensor_readings
              WHERE machine_id = @M AND sensor_type = @T
              ORDER BY recorded_at DESC LIMIT @Limit
              """
            : """
              SELECT DISTINCT ON (sensor_type) *
              FROM sensor_readings
              WHERE machine_id = @M
              ORDER BY sensor_type, recorded_at DESC
              LIMIT 500
              """;
        var rows = await conn.QueryAsync<SensorReading>(sql, new { M = machineId, T = sensorType, Limit = limitPerType });
        return rows.Select(r => new SensorReadingDto(r.Id, r.MachineId, r.StationId, r.SensorType, r.Value, r.RecordedAt));
    }

    public async Task<IEnumerable<PredictiveAlertDto>> GetAlertsAsync(string? status = null)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        var sql = status == null
            ? "SELECT * FROM predictive_alerts ORDER BY created_at DESC"
            : "SELECT * FROM predictive_alerts WHERE status = @Status ORDER BY created_at DESC";
        var rows = await conn.QueryAsync<PredictiveAlert>(sql, new { Status = status });
        return rows.Select(ToDto);
    }

    public async Task AcknowledgeAlertAsync(Guid alertId, string acknowledgedBy)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.ExecuteAsync("""
            UPDATE predictive_alerts
            SET status = 'Acknowledged', acknowledged_at = NOW(), acknowledged_by = @By
            WHERE id = @Id
            """, new { Id = alertId, By = acknowledgedBy });
    }

    public async Task<MaintenanceRuleDto> AddRuleAsync(MaintenanceRule rule)
    {
        rule.Id = Guid.NewGuid();
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.ExecuteAsync("""
            INSERT INTO maintenance_rules
                (id, machine_id, station_id, sensor_type, rule_name, threshold_value,
                 comparator, consecutive_cycles_required, maintenance_type, alert_severity, is_active)
            VALUES (@Id, @MachineId, @StationId, @SensorType, @RuleName, @ThresholdValue,
                    @Comparator, @ConsecutiveCyclesRequired, @MaintenanceType, @AlertSeverity, @IsActive)
            """, rule);
        return ToRuleDto(rule);
    }

    private async Task EvaluateRulesAsync(NpgsqlConnection conn, int machineId, int? stationId, string sensorType, double value)
    {
        var rules = await conn.QueryAsync<MaintenanceRule>("""
            SELECT * FROM maintenance_rules
            WHERE machine_id = @M AND sensor_type = @T
              AND (station_id = @S OR station_id IS NULL)
              AND is_active = TRUE
            """, new { M = machineId, T = sensorType, S = stationId });

        foreach (var rule in rules)
        {
            bool violated = rule.Comparator switch
            {
                "GT" => value > rule.ThresholdValue,
                "LT" => value < rule.ThresholdValue,
                "GTE" => value >= rule.ThresholdValue,
                "LTE" => value <= rule.ThresholdValue,
                _ => false
            };

            var key = $"{machineId}-{stationId}-{sensorType}-{rule.Id}";
            _violationCounters.TryGetValue(key, out var count);

            if (violated)
            {
                _violationCounters[key] = count + 1;

                if (_violationCounters[key] >= rule.ConsecutiveCyclesRequired)
                {
                    // Fire alert
                    _violationCounters[key] = 0;
                    await FirePredictiveAlertAsync(conn, rule, machineId, stationId, value);
                }
            }
            else
            {
                _violationCounters[key] = 0;
            }
        }
    }

    private async Task FirePredictiveAlertAsync(
        NpgsqlConnection conn, MaintenanceRule rule, int machineId, int? stationId, double value)
    {
        var alertId = Guid.NewGuid();
        var description = $"{rule.RuleName}: {rule.SensorType}={value:F3} {rule.Comparator} {rule.ThresholdValue} " +
                          $"for {rule.ConsecutiveCyclesRequired} cycles";

        await conn.ExecuteAsync("""
            INSERT INTO predictive_alerts (id, machine_id, station_id, alert_type, description, severity, status, created_at)
            VALUES (@Id, @M, @S, @Type, @Desc, @Sev, 'Open', NOW())
            """, new { Id = alertId, M = machineId, S = stationId, Type = rule.MaintenanceType, Desc = description, Sev = rule.AlertSeverity });

        _logger.LogWarning(
            "Predictive alert: Machine={M}, Station={S}, Rule={R}, Severity={Sev}",
            machineId, stationId, rule.RuleName, rule.AlertSeverity);

        // Auto-create maintenance work order for critical alerts
        if (rule.AlertSeverity == "Critical")
        {
            var wo = await _cmms.CreateAsync(new MaintenanceWorkOrder
            {
                MachineId = machineId,
                StationId = stationId,
                MaintenanceType = rule.MaintenanceType,
                Description = description,
                Priority = "High",
                TriggerType = "Predictive",
                TriggerId = alertId.ToString()
            });

            await conn.ExecuteAsync(
                "UPDATE predictive_alerts SET maintenance_work_order_id = @WoId WHERE id = @AlertId",
                new { WoId = wo.Id, AlertId = alertId });
        }
    }

    private static PredictiveAlertDto ToDto(PredictiveAlert a) => new(
        a.Id, a.MachineId, a.StationId, a.AlertType, a.Description,
        a.Severity, a.Status, a.MaintenanceWorkOrderId,
        a.CreatedAt, a.AcknowledgedAt, a.AcknowledgedBy);

    private static MaintenanceRuleDto ToRuleDto(MaintenanceRule r) => new(
        r.Id, r.MachineId, r.StationId, r.SensorType, r.RuleName,
        r.ThresholdValue, r.Comparator, r.ConsecutiveCyclesRequired,
        r.MaintenanceType, r.AlertSeverity, r.IsActive);
}
