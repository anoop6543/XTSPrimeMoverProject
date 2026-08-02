namespace MesService.Domain;

public enum WorkOrderStatus { Pending, Released, Running, Paused, Complete, Cancelled }

public class WorkOrder
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OrderNumber { get; set; } = string.Empty;
    public string ProductId { get; set; } = string.Empty;
    public Guid RecipeId { get; set; }
    public int QuantityOrdered { get; set; }
    public int QuantityCompleted { get; set; }
    public int QuantityGood { get; set; }
    public int QuantityBad { get; set; }
    public int Priority { get; set; } = 5;
    public WorkOrderStatus Status { get; set; } = WorkOrderStatus.Pending;
    public DateTime DueDate { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? TemporalWorkflowId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }

    public double CompletionPercent =>
        QuantityOrdered == 0 ? 0 : Math.Round((double)QuantityCompleted / QuantityOrdered * 100, 1);
    public double YieldPercent =>
        QuantityCompleted == 0 ? 0 : Math.Round((double)QuantityGood / QuantityCompleted * 100, 1);
}

public enum RecipeStatus { Draft, PendingApproval, Active, Archived }

public class Recipe
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ProductId { get; set; } = string.Empty;
    public string Revision { get; set; } = "1.0";
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int[] MachineRoute { get; set; } = { 0, 1, 2, 3 };
    public Dictionary<string, object> StationParameters { get; set; } = new();
    public Dictionary<string, object> QualitySpec { get; set; } = new();
    public RecipeStatus Status { get; set; } = RecipeStatus.Draft;
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class NonConformanceReport
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string NcrNumber { get; set; } = string.Empty;
    public string PartTrackingNumber { get; set; } = string.Empty;
    public Guid? WorkOrderId { get; set; }
    public int MachineId { get; set; }
    public int? StationId { get; set; }
    public string? DefectCategory { get; set; }
    public string? DefectDescription { get; set; }
    public string Severity { get; set; } = "Minor";
    public string Status { get; set; } = "Open";
    public string? AssignedTo { get; set; }
    public string? RootCause { get; set; }
    public string? CorrectiveAction { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ClosedAt { get; set; }
}

public class ProductionScheduleEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkOrderId { get; set; }
    public int MachineId { get; set; }
    public DateTime ScheduledStart { get; set; }
    public DateTime ScheduledEnd { get; set; }
    public string ShiftName { get; set; } = string.Empty;
    public string Status { get; set; } = "Scheduled";
}
