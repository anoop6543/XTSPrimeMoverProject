using Dapper;
using Npgsql;
using KnowledgeBaseService.Domain;
using XtsContracts.Dtos;
using System.Text.Json;
using XtsContracts;

namespace KnowledgeBaseService.Services;

/// <summary>
/// Handles operator issue reports submitted directly from machine terminals.
/// Injects machine context automatically and routes reports via category.
/// </summary>
public class IssueReportingService
{
    private readonly string _connectionString;
    private readonly ILogger<IssueReportingService> _logger;
    private static int _reportCounter;

    public IssueReportingService(IConfiguration config, ILogger<IssueReportingService> logger)
    {
        _connectionString = config.GetConnectionString("Postgres")
            ?? "Host=postgresql;Database=kb_db;Username=xts;******";
        _logger = logger;
    }

    public async Task<IEnumerable<IssueReport>> GetAllAsync(string? status = null, int? machineId = null)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        var where = new List<string>();
        if (status != null) where.Add("status = @Status");
        if (machineId.HasValue) where.Add("machine_id = @MachineId");
        var sql = "SELECT * FROM issue_reports"
            + (where.Count > 0 ? " WHERE " + string.Join(" AND ", where) : "")
            + " ORDER BY created_at DESC";
        return await conn.QueryAsync<IssueReport>(sql, new { Status = status, MachineId = machineId });
    }

    public async Task<IssueReport?> GetByIdAsync(Guid id)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        return await conn.QueryFirstOrDefaultAsync<IssueReport>(
            "SELECT * FROM issue_reports WHERE id = @Id", new { Id = id });
    }

    /// <summary>
    /// Creates a report with auto-injected machine context (alarms, part, recipe, recent logs).
    /// </summary>
    public async Task<IssueReport> SubmitReportAsync(SubmitIssueReportRequest request)
    {
        var report = new IssueReport
        {
            Id = Guid.NewGuid(),
            ReportNumber = GenerateReportNumber(),
            MachineId = request.MachineId,
            PartTrackingNumber = request.PartTrackingNumber,
            OperatorId = request.OperatorId,
            OperatorName = request.OperatorName,
            Category = request.Category,
            Description = request.Description,
            Severity = request.Severity,
            AutoContext = JsonSerializer.Serialize(new
            {
                machineId = request.MachineId,
                currentPart = request.PartTrackingNumber,
                activeAlarms = request.ActiveAlarmCodes,
                recipeName = request.RecipeName,
                recentLogs = request.RecentLogLines,
                reportedAt = DateTime.UtcNow
            }),
            CreatedAt = DateTime.UtcNow
        };

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.ExecuteAsync("""
            INSERT INTO issue_reports
                (id, report_number, machine_id, part_tracking_number, operator_id, operator_name,
                 category, description, severity, status, auto_context, created_at)
            VALUES
                (@Id, @ReportNumber, @MachineId, @PartTrackingNumber, @OperatorId, @OperatorName,
                 @Category, @Description, @Severity, @Status, @AutoContext::JSONB, @CreatedAt)
            """, report);

        _logger.LogInformation(
            "Issue report submitted: {Nr} Category={Cat} Severity={Sev} Machine={M}",
            LogSanitizer.Sanitize(report.ReportNumber), LogSanitizer.Sanitize(report.Category),
            LogSanitizer.Sanitize(report.Severity), report.MachineId);

        // Safety severity: immediate log escalation
        if (report.Severity == "MustStop")
        {
            _logger.LogCritical(
                "SAFETY STOP REQUESTED by {Op} at Machine {M}: {Desc}",
                LogSanitizer.Sanitize(report.OperatorName), report.MachineId,
                LogSanitizer.Sanitize(report.Description));
        }

        return report;
    }

    public async Task UpdateStatusAsync(Guid id, string status, string? assignedTo, string? resolutionNotes)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        var resolvedAt = status == "Resolved" ? (DateTime?)DateTime.UtcNow : null;
        await conn.ExecuteAsync("""
            UPDATE issue_reports SET
                status = @Status,
                assigned_to = COALESCE(@AssignedTo, assigned_to),
                resolution_notes = COALESCE(@Notes, resolution_notes),
                resolved_at = COALESCE(@ResolvedAt, resolved_at)
            WHERE id = @Id
            """, new { Id = id, Status = status, AssignedTo = assignedTo, Notes = resolutionNotes, ResolvedAt = resolvedAt });
    }

    public IssueReportDto ToDto(IssueReport r) => new(
        r.Id, r.ReportNumber, r.MachineId, r.PartTrackingNumber,
        r.OperatorId, r.OperatorName, r.Category, r.Description,
        r.Severity, r.Status, r.AutoContext, r.ResolutionNotes,
        r.AssignedTo, r.TemporalWorkflowId, r.CreatedAt, r.ResolvedAt);

    private static string GenerateReportNumber() =>
        $"IR-{DateTime.UtcNow:yyyyMMdd}-{Interlocked.Increment(ref _reportCounter):D4}";
}

public record SubmitIssueReportRequest(
    int MachineId,
    string? PartTrackingNumber,
    Guid? OperatorId,
    string OperatorName,
    string Category,
    string Description,
    string Severity,
    IReadOnlyList<string>? ActiveAlarmCodes,
    string? RecipeName,
    IReadOnlyList<string>? RecentLogLines
);
