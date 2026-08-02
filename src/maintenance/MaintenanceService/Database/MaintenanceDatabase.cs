using Npgsql;

namespace MaintenanceService.Database;

public static class MaintenanceDatabase
{
    public static async Task EnsureSchemaAsync(string connectionString)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE EXTENSION IF NOT EXISTS "pgcrypto";

            CREATE TABLE IF NOT EXISTS maintenance_work_orders (
                id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                work_order_number VARCHAR(50) UNIQUE NOT NULL,
                machine_id INT NOT NULL,
                station_id INT,
                maintenance_type VARCHAR(100) NOT NULL DEFAULT 'Corrective',
                description TEXT NOT NULL,
                priority VARCHAR(50) NOT NULL DEFAULT 'Normal',
                status VARCHAR(50) NOT NULL DEFAULT 'Open',
                scheduled_at TIMESTAMPTZ,
                started_at TIMESTAMPTZ,
                completed_at TIMESTAMPTZ,
                assigned_to VARCHAR(100),
                temporal_workflow_id VARCHAR(200),
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                trigger_type VARCHAR(100) NOT NULL DEFAULT 'Manual',
                trigger_id VARCHAR(200),
                completion_notes TEXT
            );
            CREATE INDEX IF NOT EXISTS idx_maint_wo_machine ON maintenance_work_orders(machine_id);
            CREATE INDEX IF NOT EXISTS idx_maint_wo_status ON maintenance_work_orders(status);

            CREATE TABLE IF NOT EXISTS tool_life_records (
                id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                machine_id INT NOT NULL,
                station_id INT NOT NULL,
                tool_name VARCHAR(200) NOT NULL,
                tool_serial VARCHAR(100),
                max_cycle_count INT NOT NULL CHECK (max_cycle_count > 0),
                current_cycle_count INT NOT NULL DEFAULT 0,
                installed_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                replaced_at TIMESTAMPTZ,
                is_active BOOLEAN NOT NULL DEFAULT TRUE,
                alert_threshold_percent INT NOT NULL DEFAULT 80
            );
            CREATE INDEX IF NOT EXISTS idx_tool_machine ON tool_life_records(machine_id, station_id);

            CREATE TABLE IF NOT EXISTS sensor_readings (
                id BIGSERIAL PRIMARY KEY,
                machine_id INT NOT NULL,
                station_id INT,
                sensor_type VARCHAR(100) NOT NULL,
                value DOUBLE PRECISION NOT NULL,
                recorded_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );
            CREATE INDEX IF NOT EXISTS idx_sensor_machine_time ON sensor_readings(machine_id, recorded_at DESC);

            CREATE TABLE IF NOT EXISTS predictive_alerts (
                id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                machine_id INT NOT NULL,
                station_id INT,
                alert_type VARCHAR(100) NOT NULL,
                description TEXT NOT NULL,
                severity VARCHAR(50) NOT NULL DEFAULT 'Warning',
                status VARCHAR(50) NOT NULL DEFAULT 'Open',
                maintenance_work_order_id UUID REFERENCES maintenance_work_orders(id),
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                acknowledged_at TIMESTAMPTZ,
                acknowledged_by VARCHAR(100)
            );
            CREATE INDEX IF NOT EXISTS idx_alerts_machine ON predictive_alerts(machine_id);
            CREATE INDEX IF NOT EXISTS idx_alerts_status ON predictive_alerts(status);

            CREATE TABLE IF NOT EXISTS maintenance_rules (
                id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                machine_id INT NOT NULL,
                station_id INT,
                sensor_type VARCHAR(100) NOT NULL,
                rule_name VARCHAR(200) NOT NULL,
                threshold_value DOUBLE PRECISION NOT NULL,
                comparator VARCHAR(10) NOT NULL DEFAULT 'GT',
                consecutive_cycles_required INT NOT NULL DEFAULT 3,
                maintenance_type VARCHAR(100) NOT NULL DEFAULT 'Preventive',
                alert_severity VARCHAR(50) NOT NULL DEFAULT 'Warning',
                is_active BOOLEAN NOT NULL DEFAULT TRUE
            );
            CREATE INDEX IF NOT EXISTS idx_rules_machine ON maintenance_rules(machine_id);
            """;
        await cmd.ExecuteNonQueryAsync();
    }
}
