using Dapper;
using Npgsql;
using MaintenanceService.Domain;
using XtsContracts.Dtos;

namespace MaintenanceService.Services;

public class CmmsService
{
    private readonly string _connectionString;
    private readonly ILogger<CmmsService> _logger;
    private static int _woCounter;

    public CmmsService(IConfiguration config, ILogger<CmmsService> logger)
    {
        _connectionString = config.GetConnectionString("Postgres")
            ?? "Host=postgresql;Database=maintenance_db;Username=xts;******";
        _logger = logger;
    }

    public async Task<IEnumerable<MaintenanceWorkOrder>> GetAllAsync(string? status = null, int? machineId = null)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        var where = new List<string>();
        if (status != null) where.Add("status = @Status");
        if (machineId.HasValue) where.Add("machine_id = @MachineId");
        var sql = "SELECT * FROM maintenance_work_orders"
            + (where.Count > 0 ? " WHERE " + string.Join(" AND ", where) : "")
            + " ORDER BY priority DESC, created_at DESC";
        return await conn.QueryAsync<MaintenanceWorkOrder>(sql, new { Status = status, MachineId = machineId });
    }

    public async Task<MaintenanceWorkOrder?> GetByIdAsync(Guid id)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        return await conn.QueryFirstOrDefaultAsync<MaintenanceWorkOrder>(
            "SELECT * FROM maintenance_work_orders WHERE id = @Id", new { Id = id });
    }

    public async Task<MaintenanceWorkOrder> CreateAsync(MaintenanceWorkOrder wo)
    {
        wo.Id = Guid.NewGuid();
        wo.WorkOrderNumber = GenerateWoNumber();
        wo.CreatedAt = DateTime.UtcNow;
        wo.Status = "Open";

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.ExecuteAsync("""
            INSERT INTO maintenance_work_orders
                (id, work_order_number, machine_id, station_id, maintenance_type, description,
                 priority, status, scheduled_at, assigned_to, created_at, trigger_type, trigger_id)
            VALUES
                (@Id, @WorkOrderNumber, @MachineId, @StationId, @MaintenanceType, @Description,
                 @Priority, @Status, @ScheduledAt, @AssignedTo, @CreatedAt, @TriggerType, @TriggerId)
            """, wo);

        _logger.LogInformation(
            "Maintenance WO created: {WO}, Machine={M}, Type={T}",
            wo.WorkOrderNumber, wo.MachineId, wo.MaintenanceType);
        return wo;
    }

    public async Task UpdateStatusAsync(Guid id, string status, string? notes = null)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        var startedAt = status == "InProgress" ? (DateTime?)DateTime.UtcNow : null;
        var completedAt = status == "Complete" ? (DateTime?)DateTime.UtcNow : null;
        await conn.ExecuteAsync("""
            UPDATE maintenance_work_orders SET
                status = @Status,
                started_at = COALESCE(started_at, @StartedAt),
                completed_at = @CompletedAt,
                completion_notes = COALESCE(@Notes, completion_notes)
            WHERE id = @Id
            """, new { Id = id, Status = status, StartedAt = startedAt, CompletedAt = completedAt, Notes = notes });
    }

    public MaintenanceWorkOrderDto ToDto(MaintenanceWorkOrder wo) => new(
        wo.Id, wo.WorkOrderNumber, wo.MachineId, wo.StationId, wo.MaintenanceType,
        wo.Description, wo.Priority, wo.Status, wo.ScheduledAt, wo.StartedAt,
        wo.CompletedAt, wo.AssignedTo, wo.TemporalWorkflowId, wo.CreatedAt,
        wo.TriggerType, wo.TriggerId);

    private static string GenerateWoNumber() =>
        $"MWO-{DateTime.UtcNow:yyyyMMdd}-{Interlocked.Increment(ref _woCounter):D4}";
}
