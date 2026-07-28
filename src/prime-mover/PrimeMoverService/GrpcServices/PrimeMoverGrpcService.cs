using Grpc.Core;
using PrimeMoverService.TrackEngine;
using XtsContracts.Grpc;

namespace PrimeMoverService.GrpcServices;

public class PrimeMoverGrpcService : XtsContracts.Grpc.PrimeMoverService.PrimeMoverServiceBase
{
    private readonly XtsTrackEngine _engine;

    public PrimeMoverGrpcService(XtsTrackEngine engine) => _engine = engine;

    public override Task<SystemStatusReply> GetSystemStatus(SystemStatusRequest request, ServerCallContext context)
    {
        var reply = new SystemStatusReply
        {
            IsRunning = _engine.IsRunning,
            TotalPartsProduced = _engine.TotalParts,
            GoodParts = _engine.GoodParts,
            BadParts = _engine.BadParts,
            TimestampUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        return Task.FromResult(reply);
    }

    public override async Task StreamMoverPositions(SystemStatusRequest request, IServerStreamWriter<MoverPositionUpdate> responseStream, ServerCallContext context)
    {
        while (!context.CancellationToken.IsCancellationRequested)
        {
            var tick = _engine.Tick(0.1);
            var update = new MoverPositionUpdate { TimestampUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
            foreach (var m in tick.UpdatedMovers)
            {
                update.Movers.Add(new XtsContracts.Grpc.MoverStatus
                {
                    MoverId = m.MoverId,
                    Position = m.Position,
                    Velocity = m.Velocity,
                    State = m.State,
                    LoadedPartTracking = m.LoadedPartTrackingNumber ?? string.Empty
                });
            }
            await responseStream.WriteAsync(update);
            await Task.Delay(100, context.CancellationToken);
        }
    }

    public override Task<SystemCommandReply> SendCommand(SystemCommandRequest request, ServerCallContext context)
    {
        switch (request.Command.ToLower())
        {
            case "start": _engine.Start(); break;
            case "stop": _engine.Stop(); break;
            case "setspeed": _engine.SetSpeed(request.SpeedFactor); break;
        }
        return Task.FromResult(new SystemCommandReply { Success = true, Message = $"Command '{request.Command}' executed" });
    }
}
