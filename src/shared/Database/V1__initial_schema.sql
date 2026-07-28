-- XTS Prime Mover System — Initial PostgreSQL Schema
-- Replaces SQLite-based SimulationDataLogger

-- Part lifecycle (supplements Temporal workflow history with queryable records)
CREATE TABLE IF NOT EXISTS parts (
    part_id         UUID PRIMARY KEY,
    tracking_number VARCHAR(32) UNIQUE NOT NULL,
    status          VARCHAR(32) NOT NULL DEFAULT 'BaseLayer',
    has_defect      BOOLEAN NOT NULL DEFAULT FALSE,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    entered_at      TIMESTAMPTZ,
    exited_at       TIMESTAMPTZ,
    machine_route   INTEGER[] NOT NULL DEFAULT '{}',
    temporal_workflow_id VARCHAR(128)
);

CREATE INDEX ON parts(tracking_number);
CREATE INDEX ON parts(created_at DESC);
CREATE INDEX ON parts(status);

-- Station events (per part, per station)
CREATE TABLE IF NOT EXISTS station_events (
    id              BIGSERIAL PRIMARY KEY,
    part_id         UUID REFERENCES parts(part_id),
    machine_id      INTEGER NOT NULL,
    station_id      INTEGER NOT NULL,
    station_name    VARCHAR(64) NOT NULL,
    started_at      TIMESTAMPTZ NOT NULL,
    completed_at    TIMESTAMPTZ,
    had_defect      BOOLEAN NOT NULL DEFAULT FALSE,
    elapsed_seconds DOUBLE PRECISION
);

CREATE INDEX ON station_events(part_id);
CREATE INDEX ON station_events(machine_id, station_id);

-- Machine runs (aggregate per machine invocation)
CREATE TABLE IF NOT EXISTS machine_runs (
    id              BIGSERIAL PRIMARY KEY,
    machine_id      INTEGER NOT NULL,
    part_id         UUID REFERENCES parts(part_id),
    tracking_number VARCHAR(32) NOT NULL,
    started_at      TIMESTAMPTZ NOT NULL,
    completed_at    TIMESTAMPTZ,
    success         BOOLEAN,
    fault_message   VARCHAR(512)
);

CREATE INDEX ON machine_runs(machine_id);
CREATE INDEX ON machine_runs(part_id);

-- Production snapshots
CREATE TABLE IF NOT EXISTS production_snapshots (
    id                  BIGSERIAL PRIMARY KEY,
    snapshot_at         TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    total_parts         INTEGER NOT NULL,
    good_parts          INTEGER NOT NULL,
    bad_parts           INTEGER NOT NULL,
    movers_active       INTEGER NOT NULL,
    machines_running    INTEGER NOT NULL
);

-- Error logs
CREATE TABLE IF NOT EXISTS error_logs (
    id          BIGSERIAL PRIMARY KEY,
    occurred_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    category    VARCHAR(64),
    source      VARCHAR(128),
    message     TEXT,
    stack_trace TEXT,
    recovered   BOOLEAN DEFAULT FALSE
);

-- Alarms
CREATE TABLE IF NOT EXISTS alarms (
    id          BIGSERIAL PRIMARY KEY,
    raised_at   TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    cleared_at  TIMESTAMPTZ,
    machine_id  INTEGER,
    alarm_code  VARCHAR(64),
    message     TEXT,
    severity    VARCHAR(16) DEFAULT 'Warning'
);

-- Recipes
CREATE TABLE IF NOT EXISTS recipes (
    recipe_id       UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name            VARCHAR(128) NOT NULL,
    machine_order   INTEGER[] NOT NULL,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    is_active       BOOLEAN DEFAULT TRUE
);

-- Insert default recipe
INSERT INTO recipes (name, machine_order) VALUES ('Standard-LAQF', '{0,1,2,3}') ON CONFLICT DO NOTHING;
