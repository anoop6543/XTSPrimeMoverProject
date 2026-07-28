using XtsContracts.Dtos;

namespace PrimeMoverApi.Services;

public class MachineAggregatorService
{
    private readonly IHttpClientFactory _factory;
    private readonly ILogger<MachineAggregatorService> _logger;

    public MachineAggregatorService(IHttpClientFactory factory, ILogger<MachineAggregatorService> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task<IReadOnlyList<MachineDto>> GetAllMachineStatusesAsync()
    {
        var tasks = Enumerable.Range(0, 4).Select(async i =>
        {
            try
            {
                var client = _factory.CreateClient($"MachineApi-{i}");
                return await client.GetFromJsonAsync<MachineDto>($"/machine/{i}/status");
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to get status for Machine {Id}: {Error}", i, ex.Message);
                return null;
            }
        });

        var results = await Task.WhenAll(tasks);
        return results.Where(r => r != null).Select(r => r!).ToList().AsReadOnly();
    }
}
