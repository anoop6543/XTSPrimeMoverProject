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

    /// <summary>
    /// Called by the machine gRPC service when a mover has arrived at the machine's load angle.
    /// Updates the mover state to AtMachine so the machine service knows it can load the part.
    /// </summary>
    public void NotifyMoverArrivalAtMachine(int moverId, int machineId)
    {
        lock (_lock)
        {
            var mover = _movers.FirstOrDefault(m => m.Id == moverId);
            if (mover != null)
                mover.State = TrackMoverState.AtMachine;
        }
    }

    /// <summary>
    /// Called by the machine gRPC service when a part has finished processing and is ready
    /// for pickup. Marks the nearest available idle mover to head to this machine's angle.
    /// </summary>
    public void NotifyPartReadyForPickup(int machineId, string partTracking)
    {
        if (machineId < 0 || machineId >= MachineAngles.Length)
        {
            // Reject invalid machine IDs explicitly so callers can detect misconfiguration
            Console.Error.WriteLine(
                $"[XtsTrackEngine] NotifyPartReadyForPickup: machineId={machineId} is out of range (0..{MachineAngles.Length - 1}). Request ignored.");
            return;
        }

        lock (_lock)
        {
            // Find a free mover that is not currently assigned to any machine
            var candidate = _movers
                .Where(m => m.State == TrackMoverState.Moving && m.CurrentPart == null && m.TargetMachineIndex < 0)
                .OrderBy(m => AngularDistance(m.Position, MachineAngles[machineId]))
                .FirstOrDefault();

            if (candidate != null)
                candidate.TargetMachineIndex = machineId;
        }
    }

    private static double AngularDistance(double fromDeg, double toDeg)
    {
        double diff = (toDeg - fromDeg + 360.0) % 360.0;
        return diff <= 180.0 ? diff : 360.0 - diff;
    }

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
