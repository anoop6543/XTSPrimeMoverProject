using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using XTSPrimeMoverProject.Models;

namespace XTSPrimeMoverProject.Services
{
    public class SimulationDataLogger
    {
        private readonly string _connectionString;
        private readonly ErrorHandlingService _errorHandler = ErrorHandlingService.Instance;
        private static readonly HashSet<string> AllowedTables = new(StringComparer.OrdinalIgnoreCase)
        {
            "Recipes",
            "Parts",
            "PartEvents",
            "MachineRuns",
            "Results",
            "ProductionSnapshots",
            "ErrorLogs",
            "Alarms"
        };

        public string DatabasePath { get; }

        public SimulationDataLogger(string? dbPath = null)
        {
            DatabasePath = dbPath ?? Path.Combine(AppContext.BaseDirectory, "XTSFactorySim.db");
            _connectionString = $"Data Source={DatabasePath}";
            InitializeSafe();
        }

        private void InitializeSafe()
        {
            _errorHandler.ExecuteWithRetry(
                Initialize,
                "DB.Initialize",
                ErrorCategory.Database,
                maxRetries: 3,
                baseDelayMs: 200);
        }

        private void Initialize()
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = @"
CREATE TABLE IF NOT EXISTS Recipes (
    RecipeId INTEGER PRIMARY KEY AUTOINCREMENT,
    MachineId INTEGER NOT NULL,
    StationId INTEGER NOT NULL,
    MachineName TEXT NOT NULL,
    StationName TEXT NOT NULL,
    StationType TEXT NOT NULL,
    ProcessTimeSec REAL NOT NULL,
    DefectRate REAL NOT NULL,
    CreatedAt TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS Parts (
    PartId TEXT PRIMARY KEY,
    TrackingNumber TEXT NOT NULL,
    CreatedAt TEXT NOT NULL,
    EnteredPrimeMoverAt TEXT NOT NULL,
    ExitedPrimeMoverAt TEXT,
    FinalStatus TEXT,
    IsGood INTEGER,
    HasDefect INTEGER
);

CREATE TABLE IF NOT EXISTS PartEvents (
    EventId INTEGER PRIMARY KEY AUTOINCREMENT,
    PartId TEXT NOT NULL,
    TrackingNumber TEXT NOT NULL,
    EventTime TEXT NOT NULL,
    EventType TEXT NOT NULL,
    Location TEXT NOT NULL,
    Details TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS MachineRuns (
    RunId INTEGER PRIMARY KEY AUTOINCREMENT,
    MachineId INTEGER NOT NULL,
    MachineName TEXT NOT NULL,
    PartId TEXT NOT NULL,
    TrackingNumber TEXT NOT NULL,
    EnteredAt TEXT NOT NULL,
    ExitedAt TEXT,
    IsGood INTEGER
);

CREATE TABLE IF NOT EXISTS Results (
    ResultId INTEGER PRIMARY KEY AUTOINCREMENT,
    PartId TEXT NOT NULL,
    TrackingNumber TEXT NOT NULL,
    CompletedAt TEXT NOT NULL,
    FinalStatus TEXT NOT NULL,
    CompletedStations INTEGER NOT NULL,
    TotalStations INTEGER NOT NULL
);

CREATE TABLE IF NOT EXISTS ProductionSnapshots (
    SnapshotId INTEGER PRIMARY KEY AUTOINCREMENT,
    SnapshotTime TEXT NOT NULL,
    PrimeMoverEntered INTEGER NOT NULL,
    PrimeMoverGoodExit INTEGER NOT NULL,
    PrimeMoverBadExit INTEGER NOT NULL
);

CREATE TABLE IF NOT EXISTS ErrorLogs (
    ErrorId INTEGER PRIMARY KEY AUTOINCREMENT,
    LoggedAt TEXT NOT NULL,
    Source TEXT NOT NULL,
    Message TEXT NOT NULL,
    Detail TEXT
);

CREATE TABLE IF NOT EXISTS Alarms (
    AlarmId INTEGER PRIMARY KEY AUTOINCREMENT,
    AlarmTime TEXT NOT NULL,
    Severity TEXT NOT NULL,
    Source TEXT NOT NULL,
    Message TEXT NOT NULL,
    IsActive INTEGER NOT NULL
);
";
            command.ExecuteNonQuery();
        }

        public void SeedRecipes(IEnumerable<Machine> machines)
        {
            var machineList = machines.ToList();
            _errorHandler.ExecuteWithRetry(() =>
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                using var delete = connection.CreateCommand();
                delete.CommandText = "DELETE FROM Recipes";
                delete.ExecuteNonQuery();

                foreach (var machine in machineList)
                {
                    foreach (var station in machine.Stations)
                    {
                        using var cmd = connection.CreateCommand();
                        cmd.CommandText = @"
INSERT INTO Recipes (MachineId, StationId, MachineName, StationName, StationType, ProcessTimeSec, DefectRate, CreatedAt)
VALUES ($machineId, $stationId, $machineName, $stationName, $stationType, $processTime, $defectRate, $createdAt);";
                        cmd.Parameters.AddWithValue("$machineId", machine.MachineId);
                        cmd.Parameters.AddWithValue("$stationId", station.StationId);
                        cmd.Parameters.AddWithValue("$machineName", machine.Name);
                        cmd.Parameters.AddWithValue("$stationName", station.Name);
                        cmd.Parameters.AddWithValue("$stationType", station.Type.ToString());
                        cmd.Parameters.AddWithValue("$processTime", station.ProcessTime);
                        cmd.Parameters.AddWithValue("$defectRate", station.DefectRate);
                        cmd.Parameters.AddWithValue("$createdAt", DateTime.UtcNow.ToString("o"));
                        cmd.ExecuteNonQuery();
                    }
                }
            }, "DB.SeedRecipes", ErrorCategory.Database);
        }

        public void LogPartCreated(Part part)
        {
            _errorHandler.ExecuteWithRetry(() =>
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
INSERT OR REPLACE INTO Parts (PartId, TrackingNumber, CreatedAt, EnteredPrimeMoverAt, ExitedPrimeMoverAt, FinalStatus, IsGood, HasDefect)
VALUES ($partId, $tracking, $createdAt, $enteredAt, NULL, NULL, NULL, $hasDefect);";
                cmd.Parameters.AddWithValue("$partId", part.PartId.ToString());
                cmd.Parameters.AddWithValue("$tracking", part.TrackingNumber);
                cmd.Parameters.AddWithValue("$createdAt", part.CreatedAt.ToString("o"));
                cmd.Parameters.AddWithValue("$enteredAt", part.EnteredPrimeMoverAt?.ToString("o") ?? DateTime.UtcNow.ToString("o"));
                cmd.Parameters.AddWithValue("$hasDefect", part.HasDefect ? 1 : 0);
                cmd.ExecuteNonQuery();
            }, "DB.LogPartCreated", ErrorCategory.Database);
        }

        public void LogPartEvent(Part part, string eventType, string location, string details)
        {
            _errorHandler.ExecuteWithRetry(() =>
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
INSERT INTO PartEvents (PartId, TrackingNumber, EventTime, EventType, Location, Details)
VALUES ($partId, $tracking, $eventTime, $eventType, $location, $details);";
                cmd.Parameters.AddWithValue("$partId", part.PartId.ToString());
                cmd.Parameters.AddWithValue("$tracking", part.TrackingNumber);
                cmd.Parameters.AddWithValue("$eventTime", DateTime.UtcNow.ToString("o"));
                cmd.Parameters.AddWithValue("$eventType", eventType);
                cmd.Parameters.AddWithValue("$location", location);
                cmd.Parameters.AddWithValue("$details", details);
                cmd.ExecuteNonQuery();
            }, "DB.LogPartEvent", ErrorCategory.Database);
        }

        public void LogMachineEntry(Machine machine, Part part)
        {
            _errorHandler.ExecuteWithRetry(() =>
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
INSERT INTO MachineRuns (MachineId, MachineName, PartId, TrackingNumber, EnteredAt, ExitedAt, IsGood)
VALUES ($machineId, $machineName, $partId, $tracking, $enteredAt, NULL, NULL);";
                cmd.Parameters.AddWithValue("$machineId", machine.MachineId);
                cmd.Parameters.AddWithValue("$machineName", machine.Name);
                cmd.Parameters.AddWithValue("$partId", part.PartId.ToString());
                cmd.Parameters.AddWithValue("$tracking", part.TrackingNumber);
                cmd.Parameters.AddWithValue("$enteredAt", DateTime.UtcNow.ToString("o"));
                cmd.ExecuteNonQuery();
            }, "DB.LogMachineEntry", ErrorCategory.Database);
        }

        public void LogMachineExit(Machine machine, Part part)
        {
            _errorHandler.ExecuteWithRetry(() =>
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
UPDATE MachineRuns
SET ExitedAt = $exitedAt,
    IsGood = $isGood
WHERE RunId = (
    SELECT RunId
    FROM MachineRuns
    WHERE MachineId = $machineId AND PartId = $partId AND ExitedAt IS NULL
    ORDER BY RunId DESC
    LIMIT 1
);";
                cmd.Parameters.AddWithValue("$machineId", machine.MachineId);
                cmd.Parameters.AddWithValue("$partId", part.PartId.ToString());
                cmd.Parameters.AddWithValue("$exitedAt", DateTime.UtcNow.ToString("o"));
                cmd.Parameters.AddWithValue("$isGood", part.HasDefect ? 0 : 1);
                cmd.ExecuteNonQuery();
            }, "DB.LogMachineExit", ErrorCategory.Database);
        }

        public void LogPartResult(Part part, int totalStations)
        {
            _errorHandler.ExecuteWithRetry(() =>
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                using (var partUpdate = connection.CreateCommand())
                {
                    partUpdate.CommandText = @"
UPDATE Parts
SET ExitedPrimeMoverAt = $exitedAt,
    FinalStatus = $finalStatus,
    IsGood = $isGood,
    HasDefect = $hasDefect
WHERE PartId = $partId;";
                    partUpdate.Parameters.AddWithValue("$exitedAt", part.ExitedPrimeMoverAt?.ToString("o") ?? DateTime.UtcNow.ToString("o"));
                    partUpdate.Parameters.AddWithValue("$finalStatus", part.Status.ToString());
                    partUpdate.Parameters.AddWithValue("$isGood", part.Status == PartStatus.Good ? 1 : 0);
                    partUpdate.Parameters.AddWithValue("$hasDefect", part.HasDefect ? 1 : 0);
                    partUpdate.Parameters.AddWithValue("$partId", part.PartId.ToString());
                    partUpdate.ExecuteNonQuery();
                }

                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
INSERT INTO Results (PartId, TrackingNumber, CompletedAt, FinalStatus, CompletedStations, TotalStations)
VALUES ($partId, $tracking, $completedAt, $finalStatus, $completedStations, $totalStations);";
                cmd.Parameters.AddWithValue("$partId", part.PartId.ToString());
                cmd.Parameters.AddWithValue("$tracking", part.TrackingNumber);
                cmd.Parameters.AddWithValue("$completedAt", DateTime.UtcNow.ToString("o"));
                cmd.Parameters.AddWithValue("$finalStatus", part.Status.ToString());
                cmd.Parameters.AddWithValue("$completedStations", part.CompletedStations);
                cmd.Parameters.AddWithValue("$totalStations", totalStations);
                cmd.ExecuteNonQuery();
            }, "DB.LogPartResult", ErrorCategory.Database);
        }

        public void LogSnapshot(int enteredPrimeMover, int goodExit, int badExit)
        {
            _errorHandler.ExecuteWithRetry(() =>
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
INSERT INTO ProductionSnapshots (SnapshotTime, PrimeMoverEntered, PrimeMoverGoodExit, PrimeMoverBadExit)
VALUES ($time, $entered, $good, $bad);";
                cmd.Parameters.AddWithValue("$time", DateTime.UtcNow.ToString("o"));
                cmd.Parameters.AddWithValue("$entered", enteredPrimeMover);
                cmd.Parameters.AddWithValue("$good", goodExit);
                cmd.Parameters.AddWithValue("$bad", badExit);
                cmd.ExecuteNonQuery();
            }, "DB.LogSnapshot", ErrorCategory.Database);
        }

        public void LogError(string source, string message, string? detail = null)
        {
            _errorHandler.ExecuteWithRetry(() =>
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
INSERT INTO ErrorLogs (LoggedAt, Source, Message, Detail)
VALUES ($time, $source, $message, $detail);";
                cmd.Parameters.AddWithValue("$time", DateTime.UtcNow.ToString("o"));
                cmd.Parameters.AddWithValue("$source", source);
                cmd.Parameters.AddWithValue("$message", message);
                cmd.Parameters.AddWithValue("$detail", detail ?? string.Empty);
                cmd.ExecuteNonQuery();
            }, "DB.LogError", ErrorCategory.Database);
        }

        public void LogAlarm(string severity, string source, string message, bool isActive)
        {
            _errorHandler.ExecuteWithRetry(() =>
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
INSERT INTO Alarms (AlarmTime, Severity, Source, Message, IsActive)
VALUES ($time, $severity, $source, $message, $active);";
                cmd.Parameters.AddWithValue("$time", DateTime.UtcNow.ToString("o"));
                cmd.Parameters.AddWithValue("$severity", severity);
                cmd.Parameters.AddWithValue("$source", source);
                cmd.Parameters.AddWithValue("$message", message);
                cmd.Parameters.AddWithValue("$active", isActive ? 1 : 0);
                cmd.ExecuteNonQuery();
            }, "DB.LogAlarm", ErrorCategory.Database);
        }

        public List<PartHistoryEventRecord> GetPartHistory(string trackingNumber)
        {
            return _errorHandler.ExecuteWithRetry(() =>
            {
                var result = new List<PartHistoryEventRecord>();

                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
SELECT EventTime, EventType, Location, Details
FROM PartEvents
WHERE TrackingNumber = $tracking
ORDER BY EventTime;";
                cmd.Parameters.AddWithValue("$tracking", trackingNumber);

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    result.Add(new PartHistoryEventRecord
                    {
                        EventTime = reader["EventTime"]?.ToString() ?? string.Empty,
                        EventType = reader["EventType"]?.ToString() ?? string.Empty,
                        Location = reader["Location"]?.ToString() ?? string.Empty,
                        Details = reader["Details"]?.ToString() ?? string.Empty
                    });
                }

                return result;
            }, "DB.GetPartHistory", ErrorCategory.Database, fallback: new List<PartHistoryEventRecord>())!;
        }

        public PartSummaryRecord? GetPartSummary(string trackingNumber)
        {
            return _errorHandler.ExecuteWithRetry(() =>
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
SELECT p.PartId,
       p.TrackingNumber,
       p.CreatedAt,
       p.EnteredPrimeMoverAt,
       p.ExitedPrimeMoverAt,
       p.FinalStatus,
       COALESCE(r.CompletedStations, 0) AS CompletedStations,
       COALESCE(r.TotalStations, 0) AS TotalStations
FROM Parts p
LEFT JOIN Results r ON r.PartId = p.PartId
WHERE p.TrackingNumber = $tracking
ORDER BY r.ResultId DESC
LIMIT 1;";
                cmd.Parameters.AddWithValue("$tracking", trackingNumber);

                using var reader = cmd.ExecuteReader();
                if (!reader.Read())
                {
                    return null;
                }

                return new PartSummaryRecord
                {
                PartId = reader["PartId"]?.ToString() ?? string.Empty,
                TrackingNumber = reader["TrackingNumber"]?.ToString() ?? string.Empty,
                CreatedAt = reader["CreatedAt"]?.ToString() ?? string.Empty,
                EnteredPrimeMoverAt = reader["EnteredPrimeMoverAt"]?.ToString() ?? string.Empty,
                ExitedPrimeMoverAt = reader["ExitedPrimeMoverAt"]?.ToString() ?? string.Empty,
                FinalStatus = reader["FinalStatus"]?.ToString() ?? "InProgress",
                CompletedStations = Convert.ToInt32(reader["CompletedStations"]),
                    TotalStations = Convert.ToInt32(reader["TotalStations"])
                };
            }, "DB.GetPartSummary", ErrorCategory.Database, fallback: null);
        }

        public List<string> GetExportableTables()
        {
            return AllowedTables.OrderBy(t => t).ToList();
        }

        public List<string> GetAllTables()
        {
            return _errorHandler.ExecuteWithRetry(() =>
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
SELECT name
FROM sqlite_master
WHERE type='table'
  AND name NOT LIKE 'sqlite_%'
ORDER BY name;";

                var result = new List<string>();
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    result.Add(reader["name"]?.ToString() ?? string.Empty);
                }

                return result.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            }, "DB.GetAllTables", ErrorCategory.Database, fallback: new List<string>())!;
        }

        public List<string> GetTableColumns(string tableName)
        {
            ValidateTableName(tableName);

            return _errorHandler.ExecuteWithRetry(() =>
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                using var cmd = connection.CreateCommand();
                cmd.CommandText = $"PRAGMA table_info({tableName});";

                var columns = new List<string>();
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    columns.Add(reader["name"]?.ToString() ?? string.Empty);
                }

                return columns.Where(c => !string.IsNullOrWhiteSpace(c)).ToList();
            }, "DB.GetTableColumns", ErrorCategory.Database, fallback: new List<string>())!;
        }

        public int GetTableRowCount(string tableName)
        {
            ValidateTableName(tableName);

            return _errorHandler.ExecuteWithRetry(() =>
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                using var cmd = connection.CreateCommand();
                cmd.CommandText = $"SELECT COUNT(*) FROM {tableName};";
                return Convert.ToInt32(cmd.ExecuteScalar());
            }, "DB.GetTableRowCount", ErrorCategory.Database, fallback: 0);
        }

        public List<Dictionary<string, string>> GetTableRows(string tableName, int maxRows = 500)
        {
            ValidateTableName(tableName);

            int safeMaxRows = Math.Clamp(maxRows, 1, 5000);

            return _errorHandler.ExecuteWithRetry(() =>
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                using var cmd = connection.CreateCommand();
                cmd.CommandText = $"SELECT * FROM {tableName} LIMIT {safeMaxRows};";

                var rows = new List<Dictionary<string, string>>();
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        string key = reader.GetName(i);
                        string value = reader.IsDBNull(i) ? string.Empty : reader.GetValue(i)?.ToString() ?? string.Empty;
                        row[key] = value;
                    }

                    rows.Add(row);
                }

                return rows;
            }, "DB.GetTableRows", ErrorCategory.Database, fallback: new List<Dictionary<string, string>>())!;
        }

        public string ExportTableToCsv(string tableName, string? exportDirectory = null)
        {
            if (!AllowedTables.Contains(tableName))
            {
                throw new InvalidOperationException($"Table '{tableName}' is not allowed for export.");
            }

            string targetDirectory = exportDirectory ?? Path.Combine(AppContext.BaseDirectory, "Exports");
            Directory.CreateDirectory(targetDirectory);

            string filePath = Path.Combine(targetDirectory, $"{tableName}_{DateTime.Now:yyyyMMdd_HHmmss}.csv");

            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = $"SELECT * FROM {tableName};";

            using var reader = cmd.ExecuteReader();
            using var writer = new StreamWriter(filePath, false, Encoding.UTF8);

            for (int i = 0; i < reader.FieldCount; i++)
            {
                if (i > 0)
                {
                    writer.Write(',');
                }

                writer.Write(EscapeCsv(reader.GetName(i)));
            }
            writer.WriteLine();

            while (reader.Read())
            {
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    if (i > 0)
                    {
                        writer.Write(',');
                    }

                    writer.Write(EscapeCsv(reader.IsDBNull(i) ? string.Empty : reader.GetValue(i).ToString() ?? string.Empty));
                }
                writer.WriteLine();
            }

            return filePath;
        }

        private static string EscapeCsv(string input)
        {
            if (input.Contains(',') || input.Contains('"') || input.Contains('\n') || input.Contains('\r'))
            {
                return $"\"{input.Replace("\"", "\"\"")}\"";
            }

            return input;
        }

        private static void ValidateTableName(string tableName)
        {
            if (string.IsNullOrWhiteSpace(tableName) || !Regex.IsMatch(tableName, "^[A-Za-z0-9_]+$"))
            {
                throw new InvalidOperationException("Invalid table name.");
            }
        }
    }

    public class PartHistoryEventRecord
    {
        public string EventTime { get; set; } = string.Empty;
        public string EventType { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public string Details { get; set; } = string.Empty;
    }

    public class PartSummaryRecord
    {
        public string PartId { get; set; } = string.Empty;
        public string TrackingNumber { get; set; } = string.Empty;
        public string CreatedAt { get; set; } = string.Empty;
        public string EnteredPrimeMoverAt { get; set; } = string.Empty;
        public string ExitedPrimeMoverAt { get; set; } = string.Empty;
        public string FinalStatus { get; set; } = string.Empty;
        public int CompletedStations { get; set; }
        public int TotalStations { get; set; }
    }
}
