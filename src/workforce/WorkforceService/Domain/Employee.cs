namespace WorkforceService.Domain;

public enum EmployeeRole { Operator, Technician, Engineer, Supervisor, Manager, Quality, Safety }

public class Employee
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string EmployeeNumber { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public EmployeeRole Role { get; set; }
    public string Department { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public string? BadgeId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public string FullName => $"{FirstName} {LastName}";
}

public class Certification
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EmployeeId { get; set; }
    public string CertificationType { get; set; } = string.Empty;
    public string? MachineType { get; set; }
    public DateTime IssuedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public string? IssuedBy { get; set; }
    public bool IsActive { get; set; } = true;

    public bool IsExpired => ExpiresAt.HasValue && ExpiresAt < DateTime.UtcNow;
    public int? DaysUntilExpiry => ExpiresAt.HasValue
        ? (int)(ExpiresAt.Value - DateTime.UtcNow).TotalDays
        : null;
}

public class Shift
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ShiftName { get; set; } = string.Empty;
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public DateOnly Date { get; set; }
    public string Status { get; set; } = "Scheduled";
    public List<ShiftAssignment> Assignments { get; set; } = new();
}

public class ShiftAssignment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ShiftId { get; set; }
    public Guid EmployeeId { get; set; }
    public string EmployeeName { get; set; } = string.Empty;
    public int? MachineId { get; set; }
    public string? RoleOnShift { get; set; }
    public DateTime? ClockedInAt { get; set; }
    public DateTime? ClockedOutAt { get; set; }
}

public class TrainingRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EmployeeId { get; set; }
    public string ModuleName { get; set; } = string.Empty;
    public string MachineType { get; set; } = string.Empty;
    public string Status { get; set; } = "NotStarted";
    public DateTime? CompletedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public string? Score { get; set; }
}
