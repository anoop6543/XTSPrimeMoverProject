using Dapper;
using Npgsql;
using MesService.Domain;
using XtsContracts.Dtos;
using XtsContracts;

namespace MesService.Services;

public class QualityService
{
    private readonly string _connectionString;
    private readonly ILogger<QualityService> _logger;
    private static int _ncrCounter;

    public QualityService(IConfiguration config, ILogger<QualityService> logger)
    {
        _connectionString = config.GetConnectionString("Postgres")
            ?? "Host=postgresql;Database=mes_db;Username=xts;******";
        _logger = logger;
    }

    public async Task<IEnumerable<NonConformanceReport>> GetAllAsync(string? status = null)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        var sql = status == null
            ? "SELECT * FROM non_conformance_reports ORDER BY created_at DESC"
            : "SELECT * FROM non_conformance_reports WHERE status = @Status ORDER BY created_at DESC";
        return await conn.QueryAsync<NonConformanceReport>(sql, new { Status = status });
    }

    public async Task<NonConformanceReport?> GetByIdAsync(Guid id)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        return await conn.QueryFirstOrDefaultAsync<NonConformanceReport>(
            "SELECT * FROM non_conformance_reports WHERE id = @Id", new { Id = id });
    }

    public async Task<NonConformanceReport> CreateAsync(
        string partTracking, int machineId, string defectCategory,
        string decision, Guid? workOrderId)
    {
        var ncr = new NonConformanceReport
        {
            Id = Guid.NewGuid(),
            NcrNumber = GenerateNcrNumber(),
            PartTrackingNumber = partTracking,
            MachineId = machineId,
            DefectCategory = defectCategory,
            DefectDescription = $"Decision: {decision}",
            Severity = DetermineSeverity(defectCategory),
            WorkOrderId = workOrderId,
            CreatedAt = DateTime.UtcNow
        };

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.ExecuteAsync("""
            INSERT INTO non_conformance_reports
                (id, ncr_number, part_tracking_number, work_order_id, machine_id,
                 defect_category, defect_description, severity, status, created_at)
            VALUES
                (@Id, @NcrNumber, @PartTrackingNumber, @WorkOrderId, @MachineId,
                 @DefectCategory, @DefectDescription, @Severity, @Status, @CreatedAt)
            """, ncr);

        _logger.LogWarning("NCR created: {NCR} for Part {Part}", LogSanitizer.Sanitize(ncr.NcrNumber), LogSanitizer.Sanitize(partTracking));
        return ncr;
    }

    public async Task UpdateAsync(Guid id, string? assignedTo, string? rootCause, string? correctiveAction)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.ExecuteAsync("""
            UPDATE non_conformance_reports SET
                assigned_to = COALESCE(@AssignedTo, assigned_to),
                root_cause = COALESCE(@RootCause, root_cause),
                corrective_action = COALESCE(@CorrectiveAction, corrective_action)
            WHERE id = @Id
            """, new { Id = id, AssignedTo = assignedTo, RootCause = rootCause, CorrectiveAction = correctiveAction });
    }

    public async Task CloseAsync(Guid id)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.ExecuteAsync(
            "UPDATE non_conformance_reports SET status = 'Closed', closed_at = @At WHERE id = @Id",
            new { Id = id, At = DateTime.UtcNow });
    }

    public async Task<Dictionary<string, int>> GetDefectSummaryAsync(DateTime since)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        var rows = await conn.QueryAsync<(string category, int count)>("""
            SELECT COALESCE(defect_category, 'Unknown') as category, COUNT(*) as count
            FROM non_conformance_reports
            WHERE created_at >= @Since
            GROUP BY defect_category
            ORDER BY count DESC
            """, new { Since = since });
        return rows.ToDictionary(r => r.category, r => r.count);
    }

    private static string GenerateNcrNumber() =>
        $"NCR-{DateTime.UtcNow:yyyyMMdd}-{Interlocked.Increment(ref _ncrCounter):D4}";

    private static string DetermineSeverity(string defectCategory) =>
        defectCategory.Contains("Safety", StringComparison.OrdinalIgnoreCase) ? "Critical"
        : defectCategory.Contains("Functional", StringComparison.OrdinalIgnoreCase) ? "Major"
        : "Minor";

    public NonConformanceReportDto ToDto(NonConformanceReport r) => new(
        r.Id, r.NcrNumber, r.PartTrackingNumber, r.WorkOrderId, r.MachineId,
        r.StationId, r.DefectCategory, r.DefectDescription, r.Severity, r.Status,
        r.AssignedTo, r.RootCause, r.CorrectiveAction, r.CreatedAt, r.ClosedAt);
}
