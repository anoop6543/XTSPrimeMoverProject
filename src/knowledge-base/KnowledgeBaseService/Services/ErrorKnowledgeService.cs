using System.Text.Json;
using Dapper;
using Npgsql;
using KnowledgeBaseService.Domain;
using XtsContracts.Dtos;

namespace KnowledgeBaseService.Services;

/// <summary>
/// Manages alarm resolution knowledge. Self-learning: resolution outcomes feed back
/// into confidence score adjustments and root cause ranking.
/// </summary>
public class ErrorKnowledgeService
{
    private readonly string _connectionString;
    private readonly ILogger<ErrorKnowledgeService> _logger;

    public ErrorKnowledgeService(IConfiguration config, ILogger<ErrorKnowledgeService> logger)
    {
        _connectionString = config.GetConnectionString("Postgres")
            ?? "Host=postgresql;Database=kb_db;Username=xts;******";
        _logger = logger;
    }

    public async Task<ErrorKnowledgeEntry?> GetByAlarmCodeAsync(string alarmCode, string? machineType = null)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        var row = await conn.QueryFirstOrDefaultAsync<dynamic>("""
            SELECT * FROM error_knowledge_entries
            WHERE alarm_code = @AlarmCode
              AND (machine_type = @MachineType OR machine_type IS NULL)
            ORDER BY
                CASE WHEN machine_type = @MachineType THEN 0 ELSE 1 END,
                confidence_score DESC
            LIMIT 1
            """, new { AlarmCode = alarmCode, MachineType = machineType });
        return row == null ? null : MapEntry(row);
    }

    public async Task<IReadOnlyList<ResolutionStepDto>> GetResolutionStepsAsync(string alarmCode, string machineType)
    {
        var entry = await GetByAlarmCodeAsync(alarmCode, machineType);
        if (entry == null) return Array.Empty<ResolutionStepDto>();

        return entry.ResolutionSteps
            .Select(s => new ResolutionStepDto(s.Step, s.Description, s.ToolRequired, s.RequiresAcknowledgement))
            .ToList()
            .AsReadOnly();
    }

    public async Task<ErrorKnowledgeEntry> CreateAsync(ErrorKnowledgeEntry entry)
    {
        entry.Id = Guid.NewGuid();
        entry.CreatedAt = DateTime.UtcNow;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.ExecuteAsync("""
            INSERT INTO error_knowledge_entries
                (id, alarm_code, machine_type, title, symptoms, root_causes, resolution_steps,
                 estimated_resolution_minutes, spare_parts, confidence_score, created_at)
            VALUES
                (@Id, @AlarmCode, @MachineType, @Title, @Symptoms,
                 @RootCauses::JSONB, @ResolutionSteps::JSONB,
                 @EstimatedMinutes, @SpareParts::JSONB, @Confidence, @CreatedAt)
            """, new
        {
            entry.Id, entry.AlarmCode, entry.MachineType, entry.Title,
            Symptoms = entry.Symptoms.ToArray(),
            RootCauses = JsonSerializer.Serialize(entry.RootCauses),
            ResolutionSteps = JsonSerializer.Serialize(entry.ResolutionSteps),
            EstimatedMinutes = entry.EstimatedResolutionMinutes,
            SpareParts = JsonSerializer.Serialize(entry.SpareParts),
            Confidence = entry.ConfidenceScore,
            entry.CreatedAt
        });
        return entry;
    }

    /// <summary>
    /// Records resolution outcome and updates the knowledge entry's confidence score
    /// using an exponential moving average — the self-learning component.
    /// </summary>
    public async Task RecordOutcomeAsync(
        string alarmCode, int machineId, IReadOnlyList<string> steps,
        bool wasSuccessful, int? minutesTaken, Guid? operatorId, string? notes)
    {
        await using var conn = new NpgsqlConnection(_connectionString);

        // Find the knowledge entry to update
        var entry = await GetByAlarmCodeAsync(alarmCode);

        var outcomeId = Guid.NewGuid();
        await conn.ExecuteAsync("""
            INSERT INTO resolution_outcomes
                (id, knowledge_entry_id, alarm_code, machine_id, resolution_path_taken,
                 was_successful, time_to_resolve_minutes, operator_id, resolved_at, notes)
            VALUES
                (@Id, @KbId, @AlarmCode, @MachineId, @Path::JSONB,
                 @Success, @Minutes, @OperatorId, NOW(), @Notes)
            """, new
        {
            Id = outcomeId, KbId = entry?.Id, AlarmCode = alarmCode, MachineId = machineId,
            Path = JsonSerializer.Serialize(steps), Success = wasSuccessful,
            Minutes = minutesTaken, OperatorId = operatorId, Notes = notes
        });

        if (entry != null)
        {
            // Update confidence score: EMA with α=0.2
            // Success → nudge up, failure → nudge down
            const double alpha = 0.2;
            var newConfidence = entry.ConfidenceScore * (1 - alpha)
                + (wasSuccessful ? 1.0 : 0.0) * alpha;

            await conn.ExecuteAsync(
                "UPDATE error_knowledge_entries SET confidence_score = @Score WHERE id = @Id",
                new { Score = Math.Clamp(newConfidence, 0.1, 1.0), Id = entry.Id });

            _logger.LogInformation(
                "KB updated: Alarm={Code}, Success={S}, ConfScore={C:F3}",
                alarmCode, wasSuccessful, newConfidence);
        }

        // Pattern detection: same alarm 3× on same machine in 7 days
        var recentCount = await conn.ExecuteScalarAsync<int>("""
            SELECT COUNT(*) FROM resolution_outcomes
            WHERE alarm_code = @Code AND machine_id = @M
              AND resolved_at >= NOW() - INTERVAL '7 days'
            """, new { Code = alarmCode, M = machineId });

        if (recentCount >= 3)
        {
            _logger.LogWarning(
                "PREDICTIVE FLAG: Alarm {Code} on Machine {M} occurred {N}× in 7 days — consider PM",
                alarmCode, machineId, recentCount);
            // In production: trigger a MaintenanceWorkflow or alert
        }
    }

    public async Task<AlarmResolutionDto?> GetDtoAsync(string alarmCode, string? machineType)
    {
        var entry = await GetByAlarmCodeAsync(alarmCode, machineType);
        return entry == null ? null : new AlarmResolutionDto(
            entry.Id, entry.AlarmCode, entry.MachineType, entry.Title,
            entry.Symptoms.AsReadOnly(),
            entry.RootCauses.Select(r => new RootCauseDto(r.Cause, r.Rank, r.Confidence)).ToList().AsReadOnly(),
            entry.ResolutionSteps.Select(s => new ResolutionStepDto(s.Step, s.Description, s.ToolRequired, s.RequiresAcknowledgement)).ToList().AsReadOnly(),
            entry.EstimatedResolutionMinutes,
            entry.SpareParts.Select(p => new SparePartDto(p.PartNumber, p.Description, p.Quantity)).ToList().AsReadOnly(),
            entry.ManualSectionId, entry.ConfidenceScore, entry.CreatedAt);
    }

    private static ErrorKnowledgeEntry MapEntry(dynamic row) => new()
    {
        Id = row.id, AlarmCode = row.alarm_code, MachineType = row.machine_type,
        Title = row.title,
        Symptoms = row.symptoms != null ? new List<string>((string[])row.symptoms) : new(),
        RootCauses = JsonSerializer.Deserialize<List<RootCause>>(row.root_causes?.ToString() ?? "[]") ?? new List<RootCause>(),
        ResolutionSteps = JsonSerializer.Deserialize<List<ResolutionStep>>(row.resolution_steps?.ToString() ?? "[]") ?? new List<ResolutionStep>(),
        EstimatedResolutionMinutes = row.estimated_resolution_minutes,
        SpareParts = JsonSerializer.Deserialize<List<SparePart>>(row.spare_parts?.ToString() ?? "[]") ?? new List<SparePart>(),
        ManualSectionId = row.manual_section_id, ConfidenceScore = row.confidence_score,
        CreatedAt = row.created_at
    };
}
