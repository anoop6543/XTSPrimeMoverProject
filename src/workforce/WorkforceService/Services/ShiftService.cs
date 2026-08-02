using Dapper;
using Npgsql;
using WorkforceService.Domain;
using XtsContracts.Dtos;

namespace WorkforceService.Services;

public class ShiftService
{
    private readonly string _connectionString;

    public ShiftService(IConfiguration config)
    {
        _connectionString = config.GetConnectionString("Postgres")
            ?? "Host=postgresql;Database=workforce_db;Username=xts;******";
    }

    public async Task<IEnumerable<Shift>> GetShiftsAsync(DateOnly date)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        return await conn.QueryAsync<Shift>(
            "SELECT * FROM shifts WHERE date = @Date ORDER BY start_time",
            new { Date = date.ToDateTime(TimeOnly.MinValue).Date });
    }

    public async Task<Shift> CreateShiftAsync(Shift shift)
    {
        shift.Id = Guid.NewGuid();
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.ExecuteAsync("""
            INSERT INTO shifts (id, shift_name, start_time, end_time, date, status)
            VALUES (@Id, @ShiftName, @StartTime, @EndTime, @Date, @Status)
            """, new
        {
            shift.Id, shift.ShiftName,
            StartTime = shift.StartTime.ToString(),
            EndTime = shift.EndTime.ToString(),
            Date = shift.Date.ToDateTime(TimeOnly.MinValue).Date,
            shift.Status
        });
        return shift;
    }

    public async Task AssignEmployeeAsync(ShiftAssignment assignment)
    {
        assignment.Id = Guid.NewGuid();
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.ExecuteAsync("""
            INSERT INTO shift_assignments (id, shift_id, employee_id, machine_id, role_on_shift)
            VALUES (@Id, @ShiftId, @EmployeeId, @MachineId, @RoleOnShift)
            """, assignment);
    }

    public async Task ClockInAsync(Guid shiftId, Guid employeeId)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.ExecuteAsync("""
            UPDATE shift_assignments SET clocked_in_at = NOW()
            WHERE shift_id = @ShiftId AND employee_id = @EmpId AND clocked_in_at IS NULL
            """, new { ShiftId = shiftId, EmpId = employeeId });
    }

    public async Task ClockOutAsync(Guid shiftId, Guid employeeId)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.ExecuteAsync("""
            UPDATE shift_assignments SET clocked_out_at = NOW()
            WHERE shift_id = @ShiftId AND employee_id = @EmpId AND clocked_out_at IS NULL
            """, new { ShiftId = shiftId, EmpId = employeeId });
    }

    public async Task<bool> IsOperatorLoggedInAtMachineAsync(int machineId)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        var count = await conn.ExecuteScalarAsync<int>("""
            SELECT COUNT(*) FROM shift_assignments sa
            JOIN shifts s ON s.id = sa.shift_id
            WHERE sa.machine_id = @MachineId
              AND sa.clocked_in_at IS NOT NULL
              AND sa.clocked_out_at IS NULL
              AND s.date = CURRENT_DATE
            """, new { MachineId = machineId });
        return count > 0;
    }
}
