using Npgsql;

namespace MesService.Database;

/// <summary>
/// Ensures MES PostgreSQL schema is created on first startup.
/// Uses CREATE IF NOT EXISTS — safe to re-run.
/// </summary>
public static class MesDatabase
{
    public static async Task EnsureSchemaAsync(string connectionString)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE EXTENSION IF NOT EXISTS "pgcrypto";

            CREATE TABLE IF NOT EXISTS recipes (
                id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                product_id VARCHAR(100) NOT NULL,
                revision VARCHAR(20) NOT NULL,
                name VARCHAR(200) NOT NULL,
                description TEXT,
                machine_route JSONB NOT NULL DEFAULT '[0,1,2,3]',
                station_parameters JSONB NOT NULL DEFAULT '{}',
                quality_spec JSONB NOT NULL DEFAULT '{}',
                status VARCHAR(50) NOT NULL DEFAULT 'Draft',
                approved_by VARCHAR(100),
                approved_at TIMESTAMPTZ,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                UNIQUE(product_id, revision)
            );

            CREATE TABLE IF NOT EXISTS work_orders (
                id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                order_number VARCHAR(50) UNIQUE NOT NULL,
                product_id VARCHAR(100) NOT NULL,
                recipe_id UUID REFERENCES recipes(id),
                quantity_ordered INT NOT NULL CHECK (quantity_ordered > 0),
                quantity_completed INT NOT NULL DEFAULT 0,
                quantity_good INT NOT NULL DEFAULT 0,
                quantity_bad INT NOT NULL DEFAULT 0,
                priority INT NOT NULL DEFAULT 5 CHECK (priority BETWEEN 1 AND 10),
                status VARCHAR(50) NOT NULL DEFAULT 'Pending',
                due_date TIMESTAMPTZ NOT NULL,
                started_at TIMESTAMPTZ,
                completed_at TIMESTAMPTZ,
                temporal_workflow_id VARCHAR(200),
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                created_by VARCHAR(100)
            );
            CREATE INDEX IF NOT EXISTS idx_work_orders_status ON work_orders(status);
            CREATE INDEX IF NOT EXISTS idx_work_orders_due ON work_orders(due_date);

            CREATE TABLE IF NOT EXISTS non_conformance_reports (
                id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                ncr_number VARCHAR(50) UNIQUE NOT NULL,
                part_tracking_number VARCHAR(100) NOT NULL,
                work_order_id UUID REFERENCES work_orders(id),
                machine_id INT NOT NULL,
                station_id INT,
                defect_category VARCHAR(100),
                defect_description TEXT,
                severity VARCHAR(50) NOT NULL DEFAULT 'Minor',
                status VARCHAR(50) NOT NULL DEFAULT 'Open',
                assigned_to VARCHAR(100),
                root_cause TEXT,
                corrective_action TEXT,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                closed_at TIMESTAMPTZ
            );
            CREATE INDEX IF NOT EXISTS idx_ncr_part ON non_conformance_reports(part_tracking_number);
            CREATE INDEX IF NOT EXISTS idx_ncr_status ON non_conformance_reports(status);

            CREATE TABLE IF NOT EXISTS quality_decisions (
                id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                part_tracking_number VARCHAR(100) NOT NULL,
                work_order_id UUID,
                decision VARCHAR(50) NOT NULL,
                inspection_results JSONB NOT NULL DEFAULT '[]',
                recorded_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );

            CREATE TABLE IF NOT EXISTS production_schedule (
                id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                work_order_id UUID REFERENCES work_orders(id),
                machine_id INT NOT NULL,
                scheduled_start TIMESTAMPTZ NOT NULL,
                scheduled_end TIMESTAMPTZ NOT NULL,
                shift_name VARCHAR(50) NOT NULL,
                status VARCHAR(50) NOT NULL DEFAULT 'Scheduled'
            );

            CREATE TABLE IF NOT EXISTS shift_snapshots (
                id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                shift_id UUID NOT NULL,
                shift_name VARCHAR(50) NOT NULL,
                snapshot_time TIMESTAMPTZ NOT NULL,
                parts_completed INT NOT NULL DEFAULT 0,
                alarms_count INT NOT NULL DEFAULT 0,
                uptime_seconds DOUBLE PRECISION NOT NULL DEFAULT 0,
                handoff_notes TEXT
            );
            """;

        await cmd.ExecuteNonQueryAsync();
    }
}
