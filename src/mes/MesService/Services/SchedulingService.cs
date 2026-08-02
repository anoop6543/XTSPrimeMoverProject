using Dapper;
using Npgsql;
using XtsContracts.Dtos;

namespace MesService.Services;

public class SchedulingService
{
    private readonly string _connectionString;

    public SchedulingService(IConfiguration config)
    {
        _connectionString = config.GetConnectionString("Postgres")
            ?? "Host=postgresql;Database=mes_db;Username=xts;******";
    }

    public async Task<IEnumerable<ProductionScheduleEntryDto>> GetScheduleAsync(DateOnly date)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        var rows = await conn.QueryAsync<dynamic>("""
            SELECT s.*, wo.order_number, wo.product_id
            FROM production_schedule s
            JOIN work_orders wo ON wo.id = s.work_order_id
            WHERE DATE(s.scheduled_start) = @Date
            ORDER BY s.scheduled_start
            """, new { Date = date.ToDateTime(TimeOnly.MinValue) });

        return rows.Select(r => new ProductionScheduleEntryDto(
            r.id, r.work_order_id, r.order_number, r.product_id,
            r.machine_id, r.scheduled_start, r.scheduled_end, r.shift_name, r.status));
    }

    public async Task<ProductionScheduleEntryDto> ScheduleWorkOrderAsync(
        Guid workOrderId, int machineId, DateTime start, DateTime end, string shiftName)
    {
        var id = Guid.NewGuid();
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.ExecuteAsync("""
            INSERT INTO production_schedule (id, work_order_id, machine_id, scheduled_start, scheduled_end, shift_name)
            VALUES (@Id, @WorkOrderId, @MachineId, @Start, @End, @ShiftName)
            """, new { Id = id, WorkOrderId = workOrderId, MachineId = machineId, Start = start, End = end, ShiftName = shiftName });

        return new ProductionScheduleEntryDto(id, workOrderId, string.Empty, string.Empty,
            machineId, start, end, shiftName, "Scheduled");
    }

    public async Task<ShiftSnapshotSummary> RecordShiftSnapshotAsync(
        Guid shiftId, string shiftName, DateTime snapshotTime)
    {
        await using var conn = new NpgsqlConnection(_connectionString);

        // Count parts completed this shift
        var partsCompleted = await conn.ExecuteScalarAsync<int>("""
            SELECT COUNT(*) FROM work_orders
            WHERE completed_at >= @Start AND completed_at <= @End
            """, new { Start = snapshotTime.AddHours(-12), End = snapshotTime });

        var id = Guid.NewGuid();
        await conn.ExecuteAsync("""
            INSERT INTO shift_snapshots (id, shift_id, shift_name, snapshot_time, parts_completed, handoff_notes)
            VALUES (@Id, @ShiftId, @ShiftName, @Time, @Parts, @Notes)
            """, new { Id = id, ShiftId = shiftId, ShiftName = shiftName, Time = snapshotTime,
                Parts = partsCompleted, Notes = $"Shift {shiftName} completed. Parts: {partsCompleted}" });

        return new ShiftSnapshotSummary(partsCompleted, 0, TimeSpan.Zero,
            $"Shift {shiftName} complete. Total parts: {partsCompleted}");
    }
}

public record ShiftSnapshotSummary(int PartsCompleted, int AlarmsThisShift, TimeSpan TotalUptime, string Handoff);
