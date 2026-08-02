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
        reply.Movers.AddRange(_engine.GetMoverStatuses().Select(m => new XtsContracts.Grpc.MoverStatus
        {
            MoverId = m.MoverId,
            Position = m.Position,
            Velocity = m.Velocity,
            State = m.State,
            LoadedPartTracking = m.LoadedPartTrackingNumber ?? string.Empty
        }));
        return Task.FromResult(reply);
    }

    public override async Task StreamMoverPositions(SystemStatusRequest request, IServerStreamWriter<MoverPositionUpdate> responseStream, ServerCallContext context)
    {
        while (!context.CancellationToken.IsCancellationRequested)
        {
            var update = new MoverPositionUpdate { TimestampUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
            foreach (var m in _engine.GetMoverStatuses())
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
        var command = request.Command.Trim().ToLowerInvariant();
        switch (command)
        {
            case "start": _engine.Start(); break;
            case "stop": _engine.Stop(); break;
            case "reset": _engine.Reset(); break;
            case "setspeed": _engine.SetSpeed(request.SpeedFactor); break;
            default:
                return Task.FromResult(new SystemCommandReply
                {
                    Success = false,
                    Message = $"Unsupported command '{request.Command}'. Supported commands: start, stop, reset, setspeed."
                });
        }
        return Task.FromResult(new SystemCommandReply { Success = true, Message = $"Command '{request.Command}' executed" });
    }

    public override Task<MoverArrivalReply> NotifyMoverArrival(MoverArrivalNotification request, ServerCallContext context)
    {
        // A mover has arrived at a machine station. The track engine acknowledges the arrival
        // and updates the mover's state. In the distributed architecture the machine service
        // sends this notification; in response the engine locks the mover at the station.
        _engine.NotifyMoverArrivalAtMachine(request.MoverId, request.MachineId);
        return Task.FromResult(new MoverArrivalReply { Accepted = true });
    }

    public override Task<PartReadyReply> NotifyPartReady(PartReadyNotification request, ServerCallContext context)
    {
        // A machine has finished processing a part and it is ready for pickup.
        // The track engine schedules an available mover to go to that machine.
        _engine.NotifyPartReadyForPickup(request.MachineId, request.PartTracking);
        return Task.FromResult(new PartReadyReply { Acknowledged = true });
    }
}