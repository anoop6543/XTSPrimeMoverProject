namespace MaintenanceService.Domain;

public class MaintenanceWorkOrder
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string WorkOrderNumber { get; set; } = string.Empty;
    public int MachineId { get; set; }
    public int? StationId { get; set; }
    public string MaintenanceType { get; set; } = "Corrective"; // Preventive, Corrective, Predictive, Emergency
    public string Description { get; set; } = string.Empty;
    public string Priority { get; set; } = "Normal"; // Low, Normal, High, Critical
    public string Status { get; set; } = "Open"; // Open, InProgress, Complete, Cancelled
    public DateTime? ScheduledAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? AssignedTo { get; set; }
    public string? TemporalWorkflowId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string TriggerType { get; set; } = "Manual"; // Manual, Alarm, Predictive, ToolLife
    public string? TriggerId { get; set; }
    public string? CompletionNotes { get; set; }
}

public class ToolLifeRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int MachineId { get; set; }
    public int StationId { get; set; }
    public string ToolName { get; set; } = string.Empty;
    public string? ToolSerial { get; set; }
    public int MaxCycleCount { get; set; }
    public int CurrentCycleCount { get; set; }
    public DateTime InstalledAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReplacedAt { get; set; }
    public bool IsActive { get; set; } = true;
    public int AlertThresholdPercent { get; set; } = 80;

    public double WearPercent => MaxCycleCount == 0 ? 0
        : Math.Round((double)CurrentCycleCount / MaxCycleCount * 100, 1);
    public bool ShouldAlert => WearPercent >= AlertThresholdPercent;
    public bool IsExpired => CurrentCycleCount >= MaxCycleCount;
}

public class SensorReading
{
    public long Id { get; set; }
    public int MachineId { get; set; }
    public int? StationId { get; set; }
    public string SensorType { get; set; } = string.Empty; // CycleTime, MotorCurrent, Temperature, Vibration, AxisError
    public double Value { get; set; }
    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;
}

public class PredictiveAlert
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int MachineId { get; set; }
    public int? StationId { get; set; }
    public string AlertType { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Severity { get; set; } = "Warning"; // Info, Warning, Critical
    public string Status { get; set; } = "Open";
    public Guid? MaintenanceWorkOrderId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? AcknowledgedAt { get; set; }
    public string? AcknowledgedBy { get; set; }
}

public class MaintenanceRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int MachineId { get; set; }
    public int? StationId { get; set; }
    public string SensorType { get; set; } = string.Empty;
    public string RuleName { get; set; } = string.Empty;
    public double ThresholdValue { get; set; }
    public string Comparator { get; set; } = "GT"; // GT, LT, GTE, LTE
    public int ConsecutiveCyclesRequired { get; set; } = 3;
    public string MaintenanceType { get; set; } = "Preventive";
    public string AlertSeverity { get; set; } = "Warning";
    public bool IsActive { get; set; } = true;
}
