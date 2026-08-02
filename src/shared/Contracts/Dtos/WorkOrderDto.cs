namespace XtsContracts.Dtos;

public record WorkOrderDto(
    Guid Id,
    string OrderNumber,
    string ProductId,
    Guid RecipeId,
    int QuantityOrdered,
    int QuantityCompleted,
    int QuantityGood,
    int QuantityBad,
    int Priority,
    string Status,
    DateTime DueDate,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    string? TemporalWorkflowId,
    DateTime CreatedAt,
    string? CreatedBy
);

public record MesRecipeDto(
    Guid Id,
    string ProductId,
    string Revision,
    string Name,
    string Description,
    IReadOnlyList<int> MachineRoute,
    IReadOnlyDictionary<string, object> StationParameters,
    IReadOnlyDictionary<string, object> QualitySpec,
    string Status,
    string? ApprovedBy,
    DateTime? ApprovedAt,
    DateTime CreatedAt
);

public record NonConformanceReportDto(
    Guid Id,
    string NcrNumber,
    string PartTrackingNumber,
    Guid? WorkOrderId,
    int MachineId,
    int? StationId,
    string? DefectCategory,
    string? DefectDescription,
    string Severity,
    string Status,
    string? AssignedTo,
    string? RootCause,
    string? CorrectiveAction,
    DateTime CreatedAt,
    DateTime? ClosedAt
);

public record ProductionScheduleEntryDto(
    Guid Id,
    Guid WorkOrderId,
    string OrderNumber,
    string ProductId,
    int MachineId,
    DateTime ScheduledStart,
    DateTime ScheduledEnd,
    string ShiftName,
    string Status
);
