namespace XtsContracts.Dtos;

public record MaintenanceWorkOrderDto(
    Guid Id,
    string WorkOrderNumber,
    int MachineId,
    int? StationId,
    string MaintenanceType,
    string Description,
    string Priority,
    string Status,
    DateTime? ScheduledAt,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    string? AssignedTo,
    string? TemporalWorkflowId,
    DateTime CreatedAt,
    string TriggerType,
    string? TriggerId
);

public record ToolLifeRecordDto(
    Guid Id,
    int MachineId,
    int StationId,
    string ToolName,
    string? ToolSerial,
    int MaxCycleCount,
    int CurrentCycleCount,
    double WearPercent,
    DateTime InstalledAt,
    DateTime? ReplacedAt,
    bool IsActive,
    int AlertThresholdPercent
);

public record SensorReadingDto(
    long Id,
    int MachineId,
    int? StationId,
    string SensorType,
    double Value,
    DateTime RecordedAt
);

public record PredictiveAlertDto(
    Guid Id,
    int MachineId,
    int? StationId,
    string AlertType,
    string Description,
    string Severity,
    string Status,
    Guid? MaintenanceWorkOrderId,
    DateTime CreatedAt,
    DateTime? AcknowledgedAt,
    string? AcknowledgedBy
);

public record MaintenanceRuleDto(
    Guid Id,
    int MachineId,
    int? StationId,
    string SensorType,
    string RuleName,
    double ThresholdValue,
    string Comparator,
    int ConsecutiveCyclesRequired,
    string MaintenanceType,
    string AlertSeverity,
    bool IsActive
);
