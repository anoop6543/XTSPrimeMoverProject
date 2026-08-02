using Npgsql;

namespace KnowledgeBaseService.Database;

public static class KnowledgeBaseDatabase
{
    public static async Task EnsureSchemaAsync(string connectionString)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE EXTENSION IF NOT EXISTS "pgcrypto";

            CREATE TABLE IF NOT EXISTS manual_sections (
                id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                machine_type VARCHAR(100),
                station_id INT,
                category VARCHAR(100) NOT NULL,
                title VARCHAR(300) NOT NULL,
                content JSONB NOT NULL DEFAULT '{}',
                tags TEXT[] NOT NULL DEFAULT '{}',
                skill_level VARCHAR(50),
                language VARCHAR(10) NOT NULL DEFAULT 'en',
                version INT NOT NULL DEFAULT 1,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );
            CREATE INDEX IF NOT EXISTS idx_manual_machine ON manual_sections(machine_type);
            CREATE INDEX IF NOT EXISTS idx_manual_category ON manual_sections(category);

            CREATE TABLE IF NOT EXISTS error_knowledge_entries (
                id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                alarm_code VARCHAR(50) NOT NULL,
                machine_type VARCHAR(100),
                title VARCHAR(300) NOT NULL,
                symptoms TEXT[] NOT NULL DEFAULT '{}',
                root_causes JSONB NOT NULL DEFAULT '[]',
                resolution_steps JSONB NOT NULL DEFAULT '[]',
                estimated_resolution_minutes INT,
                spare_parts JSONB DEFAULT '[]',
                manual_section_id UUID REFERENCES manual_sections(id),
                confidence_score DOUBLE PRECISION NOT NULL DEFAULT 1.0,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );
            CREATE INDEX IF NOT EXISTS idx_kb_alarm_code ON error_knowledge_entries(alarm_code);
            CREATE INDEX IF NOT EXISTS idx_kb_machine_type ON error_knowledge_entries(machine_type);

            CREATE TABLE IF NOT EXISTS resolution_outcomes (
                id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                knowledge_entry_id UUID REFERENCES error_knowledge_entries(id),
                alarm_code VARCHAR(50) NOT NULL,
                machine_id INT NOT NULL,
                resolution_path_taken JSONB NOT NULL DEFAULT '[]',
                was_successful BOOLEAN NOT NULL,
                time_to_resolve_minutes INT,
                operator_id UUID,
                resolved_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                notes TEXT
            );
            CREATE INDEX IF NOT EXISTS idx_outcomes_alarm ON resolution_outcomes(alarm_code);

            CREATE TABLE IF NOT EXISTS issue_reports (
                id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                report_number VARCHAR(50) UNIQUE NOT NULL,
                machine_id INT NOT NULL,
                part_tracking_number VARCHAR(100),
                operator_id UUID,
                operator_name VARCHAR(200) NOT NULL DEFAULT '',
                category VARCHAR(100) NOT NULL,
                description TEXT NOT NULL,
                severity VARCHAR(50) NOT NULL DEFAULT 'CanContinue',
                status VARCHAR(50) NOT NULL DEFAULT 'Open',
                auto_context JSONB,
                resolution_notes TEXT,
                assigned_to VARCHAR(100),
                temporal_workflow_id VARCHAR(200),
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                resolved_at TIMESTAMPTZ
            );
            CREATE INDEX IF NOT EXISTS idx_issues_machine ON issue_reports(machine_id);
            CREATE INDEX IF NOT EXISTS idx_issues_status ON issue_reports(status);

            -- Seed some baseline alarm knowledge entries for common alarms
            INSERT INTO error_knowledge_entries
                (id, alarm_code, machine_type, title, symptoms, root_causes, resolution_steps, estimated_resolution_minutes)
            VALUES
                (gen_random_uuid(), 'TIMEOUT', NULL,
                 'Station Processing Timeout',
                 ARRAY['Station elapsed time exceeded 2× process time', 'Machine in Fault state'],
                 '[{"Cause":"Mechanical jam at station","Rank":1,"Confidence":0.6},{"Cause":"Part incorrectly loaded","Rank":2,"Confidence":0.3}]'::JSONB,
                 '[{"Step":1,"Description":"Visually inspect station for jammed parts or tooling","ToolRequired":null,"RequiresAcknowledgement":true},{"Step":2,"Description":"Clear any jams carefully following LOTO procedure","ToolRequired":"Safety gloves","RequiresAcknowledgement":true},{"Step":3,"Description":"Press Reset on machine HMI","ToolRequired":null,"RequiresAcknowledgement":false}]'::JSONB,
                 5)
            ON CONFLICT DO NOTHING;
            """;
        await cmd.ExecuteNonQueryAsync();
    }
}
