using Dapper;
using Npgsql;
using WorkforceService.Domain;
using XtsContracts.Dtos;

namespace WorkforceService.Services;

public class EmployeeService
{
    private readonly string _connectionString;
    private static int _empCounter;

    public EmployeeService(IConfiguration config)
    {
        _connectionString = config.GetConnectionString("Postgres")
            ?? "Host=postgresql;Database=workforce_db;Username=xts;******";
    }

    public async Task<IEnumerable<Employee>> GetAllAsync(bool? activeOnly = null)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        var sql = activeOnly == true
            ? "SELECT * FROM employees WHERE is_active = TRUE ORDER BY last_name, first_name"
            : "SELECT * FROM employees ORDER BY last_name, first_name";
        return await conn.QueryAsync<Employee>(sql);
    }

    public async Task<Employee?> GetByIdAsync(Guid id)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        return await conn.QueryFirstOrDefaultAsync<Employee>(
            "SELECT * FROM employees WHERE id = @Id", new { Id = id });
    }

    public async Task<Employee?> GetByBadgeIdAsync(string badgeId)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        return await conn.QueryFirstOrDefaultAsync<Employee>(
            "SELECT * FROM employees WHERE badge_id = @Badge AND is_active = TRUE",
            new { Badge = badgeId });
    }

    public async Task<Employee> CreateAsync(Employee emp)
    {
        emp.Id = Guid.NewGuid();
        emp.EmployeeNumber = GenerateEmpNumber();
        emp.CreatedAt = DateTime.UtcNow;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.ExecuteAsync("""
            INSERT INTO employees (id, employee_number, first_name, last_name, email, role, department, is_active, badge_id, created_at)
            VALUES (@Id, @EmployeeNumber, @FirstName, @LastName, @Email, @Role, @Department, @IsActive, @BadgeId, @CreatedAt)
            """, new
        {
            emp.Id, emp.EmployeeNumber, emp.FirstName, emp.LastName, emp.Email,
            Role = emp.Role.ToString(), emp.Department, emp.IsActive, emp.BadgeId, emp.CreatedAt
        });
        return emp;
    }

    public async Task<bool> IsQualifiedForMachineAsync(Guid employeeId, string machineType)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        var count = await conn.ExecuteScalarAsync<int>("""
            SELECT COUNT(*) FROM certifications
            WHERE employee_id = @EmpId
              AND machine_type = @MachineType
              AND is_active = TRUE
              AND (expires_at IS NULL OR expires_at > NOW())
            """, new { EmpId = employeeId, MachineType = machineType });
        return count > 0;
    }

    public async Task<IEnumerable<Certification>> GetCertificationsAsync(Guid employeeId)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        return await conn.QueryAsync<Certification>(
            "SELECT * FROM certifications WHERE employee_id = @EmpId AND is_active = TRUE",
            new { EmpId = employeeId });
    }

    public async Task AddCertificationAsync(Certification cert)
    {
        cert.Id = Guid.NewGuid();
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.ExecuteAsync("""
            INSERT INTO certifications (id, employee_id, certification_type, machine_type, issued_at, expires_at, issued_by, is_active)
            VALUES (@Id, @EmployeeId, @CertificationType, @MachineType, @IssuedAt, @ExpiresAt, @IssuedBy, @IsActive)
            """, cert);
    }

    public EmployeeDto ToDto(Employee e, IEnumerable<Certification>? certs = null) => new(
        e.Id, e.EmployeeNumber, e.FirstName, e.LastName, e.Email ?? string.Empty,
        e.Role.ToString(), e.Department, e.IsActive, e.BadgeId,
        (certs ?? Enumerable.Empty<Certification>()).Select(c => new CertificationDto(
            c.Id, c.EmployeeId, c.CertificationType, c.MachineType,
            c.IssuedAt, c.ExpiresAt, c.IssuedBy, c.IsActive)).ToList().AsReadOnly(),
        e.CreatedAt);

    private static string GenerateEmpNumber() =>
        $"EMP-{Interlocked.Increment(ref _empCounter):D5}";
}
