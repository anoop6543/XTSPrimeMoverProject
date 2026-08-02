namespace XtsContracts.Dtos;

public record IssueReportDto(
    Guid Id,
    string ReportNumber,
    int MachineId,
    string? PartTrackingNumber,
    Guid? OperatorId,
    string OperatorName,
    string Category,
    string Description,
    string Severity,
    string Status,
    string? AutoContext,
    string? ResolutionNotes,
    string? AssignedTo,
    string? TemporalWorkflowId,
    DateTime CreatedAt,
    DateTime? ResolvedAt
);

public record AlarmResolutionDto(
    Guid Id,
    string AlarmCode,
    string? MachineType,
    string Title,
    IReadOnlyList<string> Symptoms,
    IReadOnlyList<RootCauseDto> RootCauses,
    IReadOnlyList<ResolutionStepDto> ResolutionSteps,
    int? EstimatedResolutionMinutes,
    IReadOnlyList<SparePartDto> SpareParts,
    Guid? ManualSectionId,
    double ConfidenceScore,
    DateTime CreatedAt
);

public record RootCauseDto(string Cause, int Rank, double Confidence);

public record ResolutionStepDto(int Step, string Description, string? ToolRequired, bool RequiresAcknowledgement);

public record SparePartDto(string PartNumber, string Description, int? Quantity);

public record ManualSectionDto(
    Guid Id,
    string? MachineType,
    int? StationId,
    string Category,
    string Title,
    object Content,
    IReadOnlyList<string> Tags,
    string? SkillLevel,
    string Language,
    int Version,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

public record ResolutionOutcomeDto(
    Guid Id,
    Guid KnowledgeEntryId,
    string AlarmCode,
    int MachineId,
    object ResolutionPathTaken,
    bool WasSuccessful,
    int? TimeToResolveMinutes,
    Guid? OperatorId,
    DateTime ResolvedAt,
    string? Notes
);
