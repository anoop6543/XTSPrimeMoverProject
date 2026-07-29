using Grpc.Core;
using XtsContracts.Grpc;
using MachineService.PlcEngine;

namespace MachineService.GrpcServices;

public class MachineGrpcService : XtsContracts.Grpc.MachineService.MachineServiceBase
{
    private readonly MachinePlcEngine _engine;
    private readonly ILogger<MachineGrpcService> _logger;

    public MachineGrpcService(MachinePlcEngine engine, ILogger<MachineGrpcService> logger)
    {
        _engine = engine;
        _logger = logger;
    }

    public override Task<MachineStatusReply> GetMachineStatus(MachineStatusRequest request, ServerCallContext context)
    {
        return Task.FromResult(BuildReply());
    }

    public override async Task StreamMachineStatus(MachineStatusRequest request, IServerStreamWriter<MachineStatusReply> responseStream, ServerCallContext context)
    {
        while (!context.CancellationToken.IsCancellationRequested)
        {
            await responseStream.WriteAsync(BuildReply());
            await Task.Delay(100, context.CancellationToken);
        }
    }

    public override Task<LoadPartReply> LoadPart(LoadPartRequest request, ServerCallContext context)
    {
        var success = _engine.LoadPart(request.PartId, request.TrackingNumber, request.HasDefect);
        return Task.FromResult(new LoadPartReply { Success = success, Message = success ? "OK" : "Machine not ready" });
    }

    public override Task<ResetMachineReply> ResetMachine(ResetMachineRequest request, ServerCallContext context)
    {
        _engine.ResetFault();
        return Task.FromResult(new ResetMachineReply { Success = true });
    }

    public override Task<UnloadPartReply> UnloadPart(UnloadPartRequest request, ServerCallContext context)
    {
        var part = _engine.UnloadPart();
        if (part == null)
        {
            return Task.FromResult(new UnloadPartReply
            {
                Success = false,
                PartId = string.Empty,
                TrackingNumber = string.Empty,
                HasDefect = false,
                PartStatus = string.Empty
            });
        }

        string finalStatus = part.HasDefect ? "Defect" : "Processed";
        return Task.FromResult(new UnloadPartReply
        {
            Success = true,
            PartId = part.PartId,
            TrackingNumber = part.TrackingNumber,
            HasDefect = part.HasDefect,
            PartStatus = finalStatus
        });
    }

    public override Task<AcknowledgeFaultReply> AcknowledgeFault(AcknowledgeFaultRequest request, ServerCallContext context)
    {
        _engine.ResetFault();
        _logger.LogInformation("Fault acknowledged on Machine {MachineId}", request.MachineId);
        return Task.FromResult(new AcknowledgeFaultReply { Success = true });
    }

    private MachineStatusReply BuildReply()
    {
        var reply = new MachineStatusReply
        {
            MachineId = _engine.MachineId,
            MachineName = $"Machine-{_engine.MachineId}",
            SequencerState = _engine.SequencerState.ToString(),
            IsOperational = !_engine.FaultActive,
            FaultActive = _engine.FaultActive,
            FaultMessage = _engine.FaultMessage,
            PartsEntered = _engine.PartsEntered,
            PartsExited = _engine.PartsExited,
            CurrentStationIndex = _engine.CurrentStationIndex,
            IsIndexing = _engine.IsIndexing,
            RotaryAngle = _engine.RotaryAngle,
            TimestampUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        foreach (var s in _engine.Stations)
        {
            reply.Stations.Add(new XtsContracts.Grpc.StationStatus
            {
                StationId = s.Definition.Id,
                Name = s.Definition.Name,
                Type = s.Definition.Type,
                Status = s.Status.ToString(),
                ProcessTime = s.Definition.ProcessTime,
                ElapsedTime = s.ElapsedTime,
                CurrentPartTracking = s.CurrentPartTracking ?? string.Empty
            });
        }

        return reply;
    }
}
