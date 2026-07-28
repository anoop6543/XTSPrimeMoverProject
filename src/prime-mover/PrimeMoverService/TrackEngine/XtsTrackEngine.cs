using XtsContracts.Dtos;

namespace PrimeMoverService.TrackEngine;

public record TrackTickResult(
    bool NewPartEntered, Guid? NewPartId, string? NewPartTrackingNumber,
    IReadOnlyList<int>? MachineRouteIds, bool PartExited, bool ExitedPartGood,
    IReadOnlyList<MoverDto> UpdatedMovers);

public class XtsTrackEngine
{
    private readonly object _lock = new();
    private readonly Random _random = new();
    private readonly List<TrackMover> _movers;
    private readonly List<int> _machineIds;
    private double _speedFactor = 1.0;
    private int _trackingCounter;
    private bool _running;

    // Constants matching original XTSSimulationEngine
    private const double MoverMinGapDegrees = 16.0;
    private const double EntryAngle = 205.0;
    private const double ExitAngle = 0.0;
    private static readonly double[] MachineAngles = { 45, 135, 225, 315 };

    public int TotalParts { get; private set; }
    public int GoodParts { get; private set; }
    public int BadParts { get; private set; }
    public int Entered { get; private set; }
    public bool IsRunning => _running;

    public XtsTrackEngine(int moverCount, IReadOnlyList<int> machineIds)
    {
        _machineIds = machineIds.ToList();
        _movers = Enumerable.Range(0, moverCount)
            .Select(i => new TrackMover(i, i * (360.0 / moverCount)))
            .ToList();
    }

    public void Start() { lock (_lock) _running = true; }
    public void Stop() { lock (_lock) _running = false; }
    public void SetSpeed(double factor) { lock (_lock) _speedFactor = factor; }

    public TrackTickResult Tick(double deltaSeconds)
    {
        lock (_lock)
        {
            if (!_running)
                return new TrackTickResult(false, null, null, null, false, false, GetMoverDtos());

            deltaSeconds *= _speedFactor;

            bool partEntered = false;
            Guid? newPartId = null;
            string? newTracking = null;
            List<int>? machineRoute = null;
            bool partExited = false;
            bool exitedGood = false;

            // Update mover positions
            foreach (var m in _movers)
            {
                if (m.State == TrackMoverState.Moving)
                    m.Position = (m.Position + m.Velocity * deltaSeconds) % 360.0;
            }

            // Entry zone: assign parts to eligible idle movers
            var eligibleForEntry = _movers
                .Where(m => m.State == TrackMoverState.Idle && m.CurrentPart == null)
                .Where(m => IsAtAngle(m.Position, EntryAngle, 5.0))
                .FirstOrDefault();

            if (eligibleForEntry != null && _movers.Count(m => m.CurrentPart != null) < 6)
            {
                _trackingCounter++;
                var tracking = $"PART-{_trackingCounter:D6}";
                eligibleForEntry.CurrentPart = new TrackPart(Guid.NewGuid(), tracking);
                eligibleForEntry.State = TrackMoverState.Loaded;
                eligibleForEntry.TargetMachineIndex = 0;
                partEntered = true;
                newPartId = eligibleForEntry.CurrentPart.PartId;
                newTracking = tracking;
                machineRoute = _machineIds.ToList();
                Entered++;
            }

            // Exit zone: collect completed parts
            var atExit = _movers.FirstOrDefault(m =>
                m.CurrentPart != null &&
                m.TargetMachineIndex >= _machineIds.Count &&
                IsAtAngle(m.Position, ExitAngle, 5.0));

            if (atExit != null)
            {
                bool good = !atExit.CurrentPart!.HasDefect;
                atExit.CurrentPart = null;
                atExit.State = TrackMoverState.Idle;
                atExit.TargetMachineIndex = -1;
                partExited = true;
                exitedGood = good;
                TotalParts++;
                if (good) GoodParts++; else BadParts++;
            }

            return new TrackTickResult(partEntered, newPartId, newTracking, machineRoute, partExited, exitedGood, GetMoverDtos());
        }
    }

    private IReadOnlyList<MoverDto> GetMoverDtos() =>
        _movers.Select(m => new MoverDto(
            m.Id, m.Position, m.Velocity, m.State.ToString(),
            m.CurrentPart?.TrackingNumber, m.TargetMachineIndex)).ToList().AsReadOnly();

    private static bool IsAtAngle(double pos, double target, double tol) =>
        Math.Abs(pos - target) < tol || Math.Abs(pos - target) > (360 - tol);
}

public enum TrackMoverState { Idle, Moving, Loaded, AtMachine }

public class TrackMover
{
    public int Id { get; }
    public double Position { get; set; }
    public double Velocity { get; set; } = 30.0;
    public TrackMoverState State { get; set; } = TrackMoverState.Moving;
    public TrackPart? CurrentPart { get; set; }
    public int TargetMachineIndex { get; set; } = -1;
    public TrackMover(int id, double position) { Id = id; Position = position; }
}

public record TrackPart(Guid PartId, string TrackingNumber, bool HasDefect = false);
