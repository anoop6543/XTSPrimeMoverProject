using Temporalio.Workflows;
using XtsContracts.Workflows;
using XtsContracts;
using OrchestrationWorker.Activities;

namespace OrchestrationWorker.Workflows;

/// <summary>
/// One instance per production work order. Owns the high-level lifecycle:
/// load recipe → release parts → track completions → close order.
/// Fans out a PartLifecycleWorkflow (existing) for each part.
/// </summary>
[Workflow]
public class ProductionOrderWorkflow : IProductionOrderWorkflow
{
    private int _quantityCompleted;
    private int _quantityGood;
    private int _quantityBad;
    private bool _paused;
    private bool _cancelled;
    private string? _pauseReason;
    private string? _cancelledBy;
    private DateTime _startedAt;
    private IReadOnlyList<int> _machineRoute = Array.Empty<int>();

    [WorkflowRun]
    public async Task<ProductionOrderResult> RunAsync(ProductionOrderInput input)
    {
        _startedAt = Workflow.UtcNow;

        Workflow.Logger.LogInformation(
            "ProductionOrder started: WO={WO}, Product={P}, Qty={Q}",
            input.OrderNumber, input.ProductId, input.QuantityOrdered);

        // Load the recipe to get machine route
        _machineRoute = await Workflow.ExecuteActivityAsync(
            (OrchestrationActivities a) => a.LoadRecipeAsync(input.RecipeId),
            new ActivityOptions { StartToCloseTimeout = TimeSpan.FromSeconds(15) });

        // Update work order status to Running in MES
        await Workflow.ExecuteActivityAsync(
            (OrchestrationActivities a) => a.UpdateWorkOrderStatusAsync(
                input.WorkOrderId, "Running", Workflow.UtcNow),
            new ActivityOptions { StartToCloseTimeout = TimeSpan.FromSeconds(15) });

        // Release parts one at a time, respecting WIP limits
        int partNumber = 0;
        while (_quantityCompleted < input.QuantityOrdered && !_cancelled)
        {
            // Wait if paused
            if (_paused)
            {
                await Workflow.WaitConditionAsync(() => !_paused || _cancelled);
                if (_cancelled) break;
            }

            // Spawn a PartLifecycleWorkflow child for this part
            // (delegates to existing master-worker task queue)
            var partTracking = $"{input.OrderNumber}-P{++partNumber:D4}";
            var partId = Guid.NewGuid();

            await Workflow.StartChildWorkflowAsync(
                (IPartLifecycleWorkflow w) => w.RunAsync(
                    new PartLifecycleInput(partId, partTracking, _machineRoute)),
                new ChildWorkflowOptions
                {
                    Id = $"part-{partTracking}",
                    TaskQueue = "xts-prime-mover-queue"
                });

            // Wait for next part completion before releasing another
            // (simple rate-limiting — in practice, a WIP semaphore workflow handles concurrency)
            await Workflow.WaitConditionAsync(
                () => _quantityCompleted >= partNumber * 0.8, // allow up to 20% ahead
                TimeSpan.FromMinutes(5));
        }

        // Wait for all remaining parts to complete
        await Workflow.WaitConditionAsync(
            () => _quantityCompleted >= input.QuantityOrdered || _cancelled,
            TimeSpan.FromHours(8));

        var finalStatus = _cancelled ? "Cancelled" : "Complete";
        var runTime = Workflow.UtcNow - _startedAt;

        await Workflow.ExecuteActivityAsync(
            (OrchestrationActivities a) => a.UpdateWorkOrderStatusAsync(
                input.WorkOrderId, finalStatus, Workflow.UtcNow),
            new ActivityOptions { StartToCloseTimeout = TimeSpan.FromSeconds(15) });

        Workflow.Logger.LogInformation(
            "ProductionOrder {Status}: WO={WO}, Good={G}/{Q}",
            finalStatus, input.OrderNumber, _quantityGood, input.QuantityOrdered);

        return new ProductionOrderResult(input.WorkOrderId, input.OrderNumber, finalStatus,
            _quantityCompleted, _quantityGood, _quantityBad, runTime);
    }

    [WorkflowSignal("PartCompleted")]
    public Task SignalPartCompletedAsync(string partTrackingNumber, bool good, string? defectCategory)
    {
        _quantityCompleted++;
        if (good) _quantityGood++;
        else _quantityBad++;
        return Task.CompletedTask;
    }

    [WorkflowSignal("Pause")]
    public Task SignalPauseAsync(string reason, string requestedBy)
    {
        _paused = true;
        _pauseReason = reason;
        Workflow.Logger.LogInformation("WO paused by {Op}: {Reason}", LogSanitizer.Sanitize(requestedBy), LogSanitizer.Sanitize(reason));
        return Task.CompletedTask;
    }

    [WorkflowSignal("Resume")]
    public Task SignalResumeAsync(string authorizedBy)
    {
        _paused = false;
        _pauseReason = null;
        Workflow.Logger.LogInformation("WO resumed by {Op}", LogSanitizer.Sanitize(authorizedBy));
        return Task.CompletedTask;
    }

    [WorkflowSignal("Cancel")]
    public Task SignalCancelAsync(string reason, string authorizedBy)
    {
        _cancelled = true;
        _cancelledBy = authorizedBy;
        Workflow.Logger.LogWarning("WO cancelled by {Op}: {Reason}", LogSanitizer.Sanitize(authorizedBy), LogSanitizer.Sanitize(reason));
        return Task.CompletedTask;
    }

    [WorkflowQuery("GetProgress")]
    public ProductionOrderProgress GetProgress()
    {
        double pct = _quantityCompleted == 0 ? 0
            : Math.Round((double)_quantityCompleted / 100.0 * 100.0, 1);
        return new ProductionOrderProgress(
            _cancelled ? "Cancelled" : (_paused ? "Paused" : "Running"),
            0, _quantityCompleted, _quantityGood, _quantityBad, pct,
            _paused, _pauseReason, null);
    }
}
