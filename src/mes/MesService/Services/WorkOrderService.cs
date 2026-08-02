using System.Text.Json;
using Dapper;
using Npgsql;
using MesService.Domain;
using XtsContracts.Dtos;

namespace MesService.Services;

public class WorkOrderService
{
    private readonly string _connectionString;
    private readonly ILogger<WorkOrderService> _logger;
    private static int _orderCounter;

    public WorkOrderService(IConfiguration config, ILogger<WorkOrderService> logger)
    {
        _connectionString = config.GetConnectionString("Postgres")
            ?? "Host=postgresql;Database=mes_db;Username=xts;******";
        _logger = logger;
    }

    public async Task<IEnumerable<WorkOrder>> GetAllAsync(string? status = null)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        var sql = status == null
            ? "SELECT * FROM work_orders ORDER BY priority DESC, due_date ASC"
            : "SELECT * FROM work_orders WHERE status = @Status ORDER BY priority DESC, due_date ASC";
        return await conn.QueryAsync<WorkOrder>(sql, new { Status = status });
    }

    public async Task<WorkOrder?> GetByIdAsync(Guid id)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        return await conn.QueryFirstOrDefaultAsync<WorkOrder>(
            "SELECT * FROM work_orders WHERE id = @Id", new { Id = id });
    }

    public async Task<WorkOrder> CreateAsync(WorkOrder order)
    {
        order.Id = Guid.NewGuid();
        order.OrderNumber = GenerateOrderNumber();
        order.CreatedAt = DateTime.UtcNow;
        order.Status = WorkOrderStatus.Pending;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.ExecuteAsync("""
            INSERT INTO work_orders
                (id, order_number, product_id, recipe_id, quantity_ordered, quantity_completed,
                 quantity_good, quantity_bad, priority, status, due_date, created_at, created_by)
            VALUES
                (@Id, @OrderNumber, @ProductId, @RecipeId, @QuantityOrdered, 0,
                 0, 0, @Priority, @Status, @DueDate, @CreatedAt, @CreatedBy)
            """, order);

        _logger.LogInformation("Work order created: {WO}", order.OrderNumber);
        return order;
    }

    public async Task UpdateStatusAsync(Guid id, string status, DateTime timestamp)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        var completedAt = status is "Complete" or "Cancelled" ? (DateTime?)timestamp : null;
        var startedAt = status == "Running" ? (DateTime?)timestamp : null;

        await conn.ExecuteAsync("""
            UPDATE work_orders SET
                status = @Status,
                started_at = COALESCE(started_at, @StartedAt),
                completed_at = @CompletedAt
            WHERE id = @Id
            """, new { Id = id, Status = status, StartedAt = startedAt, CompletedAt = completedAt });
    }

    public async Task RecordPartCompletedAsync(Guid workOrderId, bool good)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.ExecuteAsync("""
            UPDATE work_orders SET
                quantity_completed = quantity_completed + 1,
                quantity_good = quantity_good + @Good,
                quantity_bad = quantity_bad + @Bad
            WHERE id = @Id
            """, new { Id = workOrderId, Good = good ? 1 : 0, Bad = good ? 0 : 1 });
    }

    public async Task SetTemporalWorkflowIdAsync(Guid id, string workflowId)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.ExecuteAsync(
            "UPDATE work_orders SET temporal_workflow_id = @WfId WHERE id = @Id",
            new { Id = id, WfId = workflowId });
    }

    public WorkOrderDto ToDto(WorkOrder wo) => new(
        wo.Id, wo.OrderNumber, wo.ProductId, wo.RecipeId,
        wo.QuantityOrdered, wo.QuantityCompleted, wo.QuantityGood, wo.QuantityBad,
        wo.Priority, wo.Status.ToString(), wo.DueDate, wo.StartedAt, wo.CompletedAt,
        wo.TemporalWorkflowId, wo.CreatedAt, wo.CreatedBy);

    private static string GenerateOrderNumber() =>
        $"WO-{DateTime.UtcNow:yyyyMMdd}-{Interlocked.Increment(ref _orderCounter):D4}";
}
