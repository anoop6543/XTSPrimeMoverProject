using System.Text.Json;

namespace KnowledgeBaseService.Domain;

/// <summary>
/// A node in the manual content tree. Can be a section, step, warning, or tip.
/// </summary>
public class ManualSection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string? MachineType { get; set; }
    public int? StationId { get; set; }
    public string Category { get; set; } = string.Empty; // Operation, FaultResolution, Maintenance, Safety, Changeover
    public string Title { get; set; } = string.Empty;
    public JsonDocument Content { get; set; } = JsonDocument.Parse("{}");
    public List<string> Tags { get; set; } = new();
    public string? SkillLevel { get; set; } // Operator, Technician, Engineer
    public string Language { get; set; } = "en";
    public int Version { get; set; } = 1;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Knowledge base entry for a specific alarm code with resolution guidance.
/// </summary>
public class ErrorKnowledgeEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string AlarmCode { get; set; } = string.Empty;
    public string? MachineType { get; set; }
    public string Title { get; set; } = string.Empty;
    public List<string> Symptoms { get; set; } = new();
    public List<RootCause> RootCauses { get; set; } = new();
    public List<ResolutionStep> ResolutionSteps { get; set; } = new();
    public int? EstimatedResolutionMinutes { get; set; }
    public List<SparePart> SpareParts { get; set; } = new();
    public Guid? ManualSectionId { get; set; }
    public double ConfidenceScore { get; set; } = 1.0;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public record RootCause(string Cause, int Rank, double Confidence);
public record ResolutionStep(int Step, string Description, string? ToolRequired, bool RequiresAcknowledgement);
public record SparePart(string PartNumber, string Description, int? Quantity);

/// <summary>
/// Outcome record for each fault resolution attempt — drives self-learning scoring.
/// </summary>
public class ResolutionOutcome
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid KnowledgeEntryId { get; set; }
    public string AlarmCode { get; set; } = string.Empty;
    public int MachineId { get; set; }
    public List<string> ResolutionPathTaken { get; set; } = new();
    public bool WasSuccessful { get; set; }
    public int? TimeToResolveMinutes { get; set; }
    public Guid? OperatorId { get; set; }
    public DateTime ResolvedAt { get; set; } = DateTime.UtcNow;
    public string? Notes { get; set; }
}

/// <summary>
/// Operator-submitted issue report from the machine terminal.
/// </summary>
public class IssueReport
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ReportNumber { get; set; } = string.Empty;
    public int MachineId { get; set; }
    public string? PartTrackingNumber { get; set; }
    public Guid? OperatorId { get; set; }
    public string OperatorName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty; // Quality, MachineBehavior, Safety, Tooling, Other
    public string Description { get; set; } = string.Empty;
    public string Severity { get; set; } = "CanContinue"; // CanContinue, LineSlowed, MustStop
    public string Status { get; set; } = "Open";
    public string? AutoContext { get; set; } // JSON: alarm codes, last log lines, recipe name, etc.
    public string? ResolutionNotes { get; set; }
    public string? AssignedTo { get; set; }
    public string? TemporalWorkflowId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAt { get; set; }
}
