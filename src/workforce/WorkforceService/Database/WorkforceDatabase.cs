using Npgsql;

namespace WorkforceService.Database;

public static class WorkforceDatabase
{
    public static async Task EnsureSchemaAsync(string connectionString)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE EXTENSION IF NOT EXISTS "pgcrypto";

            CREATE TABLE IF NOT EXISTS employees (
                id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                employee_number VARCHAR(50) UNIQUE NOT NULL,
                first_name VARCHAR(100) NOT NULL,
                last_name VARCHAR(100) NOT NULL,
                email VARCHAR(200),
                role VARCHAR(100) NOT NULL,
                department VARCHAR(100) NOT NULL DEFAULT '',
                is_active BOOLEAN NOT NULL DEFAULT TRUE,
                badge_id VARCHAR(100),
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );
            CREATE INDEX IF NOT EXISTS idx_employees_badge ON employees(badge_id);
            CREATE INDEX IF NOT EXISTS idx_employees_role ON employees(role);

            CREATE TABLE IF NOT EXISTS certifications (
                id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                employee_id UUID REFERENCES employees(id) ON DELETE CASCADE,
                certification_type VARCHAR(100) NOT NULL,
                machine_type VARCHAR(100),
                issued_at TIMESTAMPTZ NOT NULL,
                expires_at TIMESTAMPTZ,
                issued_by VARCHAR(100),
                is_active BOOLEAN NOT NULL DEFAULT TRUE
            );
            CREATE INDEX IF NOT EXISTS idx_certs_employee ON certifications(employee_id);

            CREATE TABLE IF NOT EXISTS shifts (
                id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                shift_name VARCHAR(50) NOT NULL,
                start_time TIME NOT NULL,
                end_time TIME NOT NULL,
                date DATE NOT NULL,
                status VARCHAR(50) NOT NULL DEFAULT 'Scheduled'
            );
            CREATE INDEX IF NOT EXISTS idx_shifts_date ON shifts(date);

            CREATE TABLE IF NOT EXISTS shift_assignments (
                id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                shift_id UUID REFERENCES shifts(id) ON DELETE CASCADE,
                employee_id UUID REFERENCES employees(id),
                machine_id INT,
                role_on_shift VARCHAR(100),
                clocked_in_at TIMESTAMPTZ,
                clocked_out_at TIMESTAMPTZ
            );

            CREATE TABLE IF NOT EXISTS training_records (
                id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                employee_id UUID REFERENCES employees(id) ON DELETE CASCADE,
                module_name VARCHAR(200) NOT NULL,
                machine_type VARCHAR(100) NOT NULL DEFAULT '',
                status VARCHAR(50) NOT NULL DEFAULT 'NotStarted',
                completed_at TIMESTAMPTZ,
                expires_at TIMESTAMPTZ,
                score VARCHAR(50),
                UNIQUE (employee_id, module_name)
            );
            CREATE INDEX IF NOT EXISTS idx_training_employee ON training_records(employee_id);

            CREATE TABLE IF NOT EXISTS performance_kpis (
                id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                employee_id UUID REFERENCES employees(id),
                machine_id INT NOT NULL,
                parts_per_hour DOUBLE PRECISION NOT NULL DEFAULT 0,
                quality_yield_percent DOUBLE PRECISION NOT NULL DEFAULT 0,
                alarm_response_seconds DOUBLE PRECISION NOT NULL DEFAULT 0,
                kpi_date DATE NOT NULL DEFAULT CURRENT_DATE
            );
            CREATE INDEX IF NOT EXISTS idx_kpi_employee_date ON performance_kpis(employee_id, kpi_date);
            """;
        await cmd.ExecuteNonQueryAsync();
    }
}
