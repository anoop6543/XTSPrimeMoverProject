namespace XtsContracts.Dtos;

public record EmployeeDto(
    Guid Id,
    string EmployeeNumber,
    string FirstName,
    string LastName,
    string Email,
    string Role,
    string Department,
    bool IsActive,
    string? BadgeId,
    IReadOnlyList<CertificationDto> Certifications,
    DateTime CreatedAt
);

public record CertificationDto(
    Guid Id,
    Guid EmployeeId,
    string CertificationType,
    string? MachineType,
    DateTime IssuedAt,
    DateTime? ExpiresAt,
    string? IssuedBy,
    bool IsActive
);

public record ShiftDto(
    Guid Id,
    string ShiftName,
    TimeSpan StartTime,
    TimeSpan EndTime,
    DateOnly Date,
    string Status,
    IReadOnlyList<ShiftAssignmentDto> Assignments
);

public record ShiftAssignmentDto(
    Guid Id,
    Guid ShiftId,
    Guid EmployeeId,
    string EmployeeName,
    int? MachineId,
    string? RoleOnShift,
    DateTime? ClockedInAt,
    DateTime? ClockedOutAt
);

public record TrainingRecordDto(
    Guid Id,
    Guid EmployeeId,
    string ModuleName,
    string MachineType,
    string Status,
    DateTime? CompletedAt,
    DateTime? ExpiresAt,
    string? Score
);

public record PerformanceKpiDto(
    Guid EmployeeId,
    string EmployeeName,
    int MachineId,
    double PartsPerHour,
    double QualityYieldPercent,
    double AlarmResponseTimeSeconds,
    DateOnly Date
);
