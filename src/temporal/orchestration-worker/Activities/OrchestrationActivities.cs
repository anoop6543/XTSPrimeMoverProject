using Temporalio.Activities;
using System.Net.Http.Json;
using System.Text.Json;

namespace OrchestrationWorker.Activities;

/// <summary>
/// Activities for all orchestration-layer decisions:
/// MES updates, quality recording, fault resolution, safety signals,
/// shift management, and cross-service notifications.
/// </summary>
public class OrchestrationActivities
{
    private readonly IHttpClientFactory _http;
    private readonly ILogger<OrchestrationActivities> _logger;

    public OrchestrationActivities(IHttpClientFactory http, ILogger<OrchestrationActivities> logger)
    {
        _http = http;
        _logger = logger;
    }

    // ── Quality Gate Activities ─────────────────────────────────────────────

    [Activity]
    public async Task NotifySupervisorQualityHoldAsync(string partTracking, string defectCategory, int machineId)
    {
        _logger.LogWarning(
            "QUALITY HOLD: Part={Part}, Defect={Defect}, Machine={M}",
            partTracking, defectCategory, machineId);
        // In production: push SignalR notification to supervisor terminal
        await Task.Delay(100);
    }

    [Activity]
    public async Task<Guid> CreateNonConformanceReportAsync(
        string partTracking, int machineId, string defectCategory, string decision, Guid? workOrderId)
    {
        var client = _http.CreateClient("MesService");
        var response = await client.PostAsJsonAsync("/api/quality/ncr", new
        {
            PartTrackingNumber = partTracking,
            MachineId = machineId,
            DefectCategory = defectCategory,
            Decision = decision,
            WorkOrderId = workOrderId
        });
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        return result.GetProperty("id").GetGuid();
    }

    [Activity]
    public async Task RecordQualityDecisionAsync(
        string partTracking, string decision,
        IReadOnlyList<XtsContracts.Workflows.InspectionResult> results, Guid? workOrderId)
    {
        var client = _http.CreateClient("MesService");
        await client.PostAsJsonAsync("/api/quality/decisions", new
        {
            PartTrackingNumber = partTracking,
            Decision = decision,
            Results = results,
            WorkOrderId = workOrderId,
            RecordedAt = DateTime.UtcNow
        });
    }

    // ── Fault Resolution Activities ─────────────────────────────────────────

    [Activity]
    public async Task<IReadOnlyList<Workflows.ResolutionStepInfo>> LookupFaultResolutionStepsAsync(
        string alarmCode, string machineType)
    {
        var client = _http.CreateClient("KnowledgeBaseService");
        try
        {
            var response = await client.GetFromJsonAsync<List<Workflows.ResolutionStepInfo>>(
                $"/api/knowledge/alarm/{Uri.EscapeDataString(alarmCode)}/steps?machineType={Uri.EscapeDataString(machineType)}");
            return response ?? new List<Workflows.ResolutionStepInfo>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load resolution steps for alarm {Code}", alarmCode);
            // Return a default "contact maintenance" step so the workflow can still run
            return new[]
            {
                new Workflows.ResolutionStepInfo(alarmCode, 0,
                    "Check machine for obvious fault conditions and reset if safe", null, true),
                new Workflows.ResolutionStepInfo(alarmCode, 1,
                    "If not resolved, contact maintenance supervisor", null, true)
            };
        }
    }

    [Activity]
    public async Task PushGuidanceToTerminalAsync(
        int machineId, string alarmCode,
        IReadOnlyList<Workflows.ResolutionStepInfo> steps)
    {
        _logger.LogInformation(
            "Pushing {Count} resolution steps to Machine {M} terminal (Alarm={A})",
            steps.Count, machineId, alarmCode);
        // In production: SignalR push to machine HMI
        await Task.Delay(50);
    }

    [Activity]
    public async Task EscalateFaultAsync(int machineId, string alarmCode, int level, string targetRole)
    {
        _logger.LogWarning(
            "Escalating fault: Machine={M}, Alarm={A}, Level={L}, To={Role}",
            machineId, alarmCode, level, targetRole);
        await Task.Delay(100);
    }

    [Activity]
    public async Task<Guid> CreateMaintenanceWorkOrderAsync(
        int machineId, string alarmCode, string description, string maintenanceType)
    {
        var client = _http.CreateClient("MaintenanceService");
        var response = await client.PostAsJsonAsync("/api/maintenance/work-orders", new
        {
            MachineId = machineId,
            Description = description,
            MaintenanceType = maintenanceType,
            TriggerType = "Alarm",
            TriggerId = alarmCode,
            Priority = "Normal"
        });
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        return result.GetProperty("id").GetGuid();
    }

    [Activity]
    public async Task RecordFaultResolutionOutcomeAsync(
        string alarmCode, int machineId,
        IReadOnlyList<string> stepsCompleted, bool resolved, int minutesTaken)
    {
        var client = _http.CreateClient("KnowledgeBaseService");
        await client.PostAsJsonAsync("/api/knowledge/outcomes", new
        {
            AlarmCode = alarmCode,
            MachineId = machineId,
            StepsCompleted = stepsCompleted,
            WasSuccessful = resolved,
            TimeToResolveMinutes = minutesTaken,
            ResolvedAt = DateTime.UtcNow
        });
    }

    // ── Safety Activities ───────────────────────────────────────────────────

    [Activity]
    public async Task EmergencyStopAllMachinesAsync(string sourceId, string reason)
    {
        _logger.LogCritical("E-STOP: Stopping all machines. Source={S}, Reason={R}", sourceId, reason);
        var tasks = Enumerable.Range(0, 4).Select(async machineIdx =>
        {
            try
            {
                var client = _http.CreateClient($"MachineApi-{machineIdx}");
                await client.PostAsync("/api/machine/emergency-stop", null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to E-stop machine {M}", machineIdx);
            }
        });
        await Task.WhenAll(tasks);
    }

    [Activity]
    public async Task NotifySafetyOfficerAsync(string sourceId, string reason, string triggeredBy)
    {
        _logger.LogCritical(
            "SAFETY NOTIFICATION: Source={S}, Reason={R}, By={By}",
            sourceId, reason, triggeredBy);
        await Task.Delay(100);
    }

    [Activity]
    public async Task BroadcastSafetyAllClearAsync(string authorizedBy)
    {
        _logger.LogInformation("SAFETY ALL-CLEAR authorized by {Auth}", authorizedBy);
        await Task.Delay(100);
    }

    // ── Shift Transition Activities ──────────────────────────────────────────

    [Activity]
    public async Task StopNewPartEntryAsync(Guid shiftId)
    {
        var client = _http.CreateClient("PrimeMoverApi");
        await client.PostAsync("/api/system/stop-entry", null);
        _logger.LogInformation("Part entry stopped for shift {Shift}", shiftId);
    }

    [Activity]
    public async Task<ShiftSnapshotResult> TakeShiftSnapshotAsync(Guid shiftId, string shiftName)
    {
        var client = _http.CreateClient("MesService");
        try
        {
            var resp = await client.PostAsJsonAsync("/api/scheduling/shift-snapshot", new
            {
                ShiftId = shiftId,
                ShiftName = shiftName,
                SnapshotTime = DateTime.UtcNow
            });
            if (resp.IsSuccessStatusCode)
            {
                var result = await resp.Content.ReadFromJsonAsync<ShiftSnapshotResult>();
                return result ?? new ShiftSnapshotResult(0, 0, TimeSpan.Zero, "Shift complete");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to take shift snapshot for {Shift}", shiftId);
        }
        return new ShiftSnapshotResult(0, 0, TimeSpan.Zero, "Snapshot unavailable");
    }

    [Activity]
    public async Task NotifyIncomingShiftAsync(Guid incomingShiftId, string shiftName, string handoffNotes)
    {
        _logger.LogInformation(
            "Shift handoff to {Shift}: {Notes}", shiftName, handoffNotes);
        await Task.Delay(100);
    }

    // ── MES / Work Order Activities ──────────────────────────────────────────

    [Activity]
    public async Task<IReadOnlyList<int>> LoadRecipeAsync(Guid recipeId)
    {
        var client = _http.CreateClient("MesService");
        try
        {
            var recipe = await client.GetFromJsonAsync<JsonElement>($"/api/recipes/{recipeId}");
            var route = recipe.GetProperty("machineRoute").Deserialize<int[]>();
            return route ?? new[] { 0, 1, 2, 3 };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load recipe {Id}, using default route", recipeId);
            return new[] { 0, 1, 2, 3 };
        }
    }

    [Activity]
    public async Task UpdateWorkOrderStatusAsync(Guid workOrderId, string status, DateTime timestamp)
    {
        var client = _http.CreateClient("MesService");
        await client.PatchAsync($"/api/work-orders/{workOrderId}/status",
            JsonContent.Create(new { Status = status, Timestamp = timestamp }));
        _logger.LogInformation("WO {Id} status → {Status}", workOrderId, status);
    }

    // ── Mover Scheduler Activities ──────────────────────────────────────────

    [Activity]
    public async Task NotifyMoverAssignedAsync(
        int moverId, int machineId, string partTracking, string callbackWorkflowId)
    {
        var client = _http.CreateClient("PrimeMoverApi");
        await client.PostAsJsonAsync("/api/movers/assigned", new
        {
            MoverId = moverId,
            MachineId = machineId,
            PartTracking = partTracking,
            CallbackWorkflowId = callbackWorkflowId
        });
    }
}

public record ShiftSnapshotResult(int PartsCompleted, int AlarmsThisShift, TimeSpan TotalUptime, string Handoff);
