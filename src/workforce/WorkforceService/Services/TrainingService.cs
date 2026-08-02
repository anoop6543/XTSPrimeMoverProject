using Dapper;
using Npgsql;
using WorkforceService.Domain;
using XtsContracts.Dtos;

namespace WorkforceService.Services;

public class TrainingService
{
    private readonly string _connectionString;

    public TrainingService(IConfiguration config)
    {
        _connectionString = config.GetConnectionString("Postgres")
            ?? "Host=postgresql;Database=workforce_db;Username=xts;******";
    }

    public async Task<IEnumerable<TrainingRecord>> GetForEmployeeAsync(Guid employeeId)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        return await conn.QueryAsync<TrainingRecord>(
            "SELECT * FROM training_records WHERE employee_id = @EmpId ORDER BY completed_at DESC",
            new { EmpId = employeeId });
    }

    public async Task<IEnumerable<TrainingRecord>> GetExpiringAsync(int daysAhead = 30)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        return await conn.QueryAsync<TrainingRecord>("""
            SELECT * FROM training_records
            WHERE expires_at IS NOT NULL
              AND expires_at BETWEEN NOW() AND NOW() + INTERVAL '1 day' * @Days
            ORDER BY expires_at
            """, new { Days = daysAhead });
    }

    public async Task RecordCompletionAsync(Guid employeeId, string moduleName, string machineType, string score)
    {
        await using var conn = new NpgsqlConnection(_connectionString);

        // Upsert: update if exists, insert if not
        await conn.ExecuteAsync("""
            INSERT INTO training_records (id, employee_id, module_name, machine_type, status, completed_at, score)
            VALUES (gen_random_uuid(), @EmpId, @Module, @MachineType, 'Complete', NOW(), @Score)
            ON CONFLICT (employee_id, module_name) DO UPDATE
            SET status = 'Complete', completed_at = NOW(), score = @Score
            """, new { EmpId = employeeId, Module = moduleName, MachineType = machineType, Score = score });
    }

    public async Task<bool> HasCompletedRequiredTrainingAsync(Guid employeeId, string machineType)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        var count = await conn.ExecuteScalarAsync<int>("""
            SELECT COUNT(*) FROM training_records
            WHERE employee_id = @EmpId
              AND machine_type = @MachineType
              AND status = 'Complete'
              AND (expires_at IS NULL OR expires_at > NOW())
            """, new { EmpId = employeeId, MachineType = machineType });
        return count > 0;
    }

    public TrainingRecordDto ToDto(TrainingRecord r) => new(
        r.Id, r.EmployeeId, r.ModuleName, r.MachineType,
        r.Status, r.CompletedAt, r.ExpiresAt, r.Score);
}
