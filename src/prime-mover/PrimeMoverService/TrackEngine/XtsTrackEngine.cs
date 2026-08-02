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
    private readonly int _moverCount;
    private double _speedFactor = 1.0;
    private int _trackingCounter;
    private bool _running;

    // Constants matching original XTSSimulationEngine
    private const double MoverMinGapDegrees = 16.0;
    private const double EntryAngle = 205.0;
    private const double ExitAngle = 0.0;
    private const int ExitTargetIndex = int.MaxValue;
    private static readonly double[] MachineAngles = { 45, 135, 225, 315 };

    public int TotalParts { get; private set; }
    public int GoodParts { get; private set; }
    public int BadParts { get; private set; }
    public int Entered { get; private set; }
    public bool IsRunning => _running;

    public XtsTrackEngine(int moverCount, IReadOnlyList<int> machineIds)
    {
        _moverCount = moverCount;
        _machineIds = machineIds.ToList();
        _movers = new List<TrackMover>(moverCount);
        Reset();
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
        if (machineId < 0 || machineId >= MachineAngles.Length)
        {
            Console.Error.WriteLine(
                $"[XtsTrackEngine] NotifyMoverArrivalAtMachine: machineId={machineId} is out of range (0..{MachineAngles.Length - 1}). Request ignored.");
            return;
        }

        lock (_lock)
        {
            var mover = _movers.FirstOrDefault(m => m.Id == moverId);
            if (mover == null)
            {
                Console.Error.WriteLine($"[XtsTrackEngine] NotifyMoverArrivalAtMachine: moverId={moverId} was not found. Request ignored.");
                return;
            }

            mover.TargetMachineIndex = machineId;
            mover.State = mover.CurrentPart == null ? TrackMoverState.Moving : TrackMoverState.Loaded;
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
                .Where(m => m.CurrentPart == null && m.TargetMachineIndex < 0 && m.State != TrackMoverState.AtMachine)
                .OrderBy(m => AngularDistance(m.Position, MachineAngles[machineId]))
                .FirstOrDefault();

            if (candidate != null)
            {
                candidate.TargetMachineIndex = machineId;
                candidate.State = TrackMoverState.Moving;
            }
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            _speedFactor = 1.0;
            _trackingCounter = 0;
            _running = false;
            TotalParts = 0;
            GoodParts = 0;
            BadParts = 0;
            Entered = 0;

            _movers.Clear();
            _movers.AddRange(Enumerable.Range(0, _moverCount)
                .Select(i => new TrackMover(i, i * (360.0 / _moverCount))));
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
                if (m.State is TrackMoverState.Moving or TrackMoverState.Loaded)
                    m.Position = (m.Position + m.Velocity * deltaSeconds) % 360.0;
            }

            foreach (var mover in _movers.Where(m => m.TargetMachineIndex >= 0 && m.TargetMachineIndex != ExitTargetIndex))
            {
                if (!IsAtAngle(mover.Position, MachineAngles[mover.TargetMachineIndex], 5.0))
                    continue;

                if (mover.CurrentPart == null)
                {
                    mover.TargetMachineIndex = -1;
                    mover.State = TrackMoverState.Moving;
                    continue;
                }

                var currentRouteIndex = _machineIds.IndexOf(mover.TargetMachineIndex);
                mover.TargetMachineIndex = currentRouteIndex >= 0 && currentRouteIndex + 1 < _machineIds.Count
                    ? _machineIds[currentRouteIndex + 1]
                    : ExitTargetIndex;
                mover.State = TrackMoverState.Loaded;
            }

            // Entry zone: assign parts to eligible free movers
            var eligibleForEntry = _movers
                .Where(m => m.CurrentPart == null && m.TargetMachineIndex < 0)
                .Where(m => IsAtAngle(m.Position, EntryAngle, 5.0))
                .FirstOrDefault();

            if (eligibleForEntry != null && _movers.Count(m => m.CurrentPart != null) < 6)
            {
                _trackingCounter++;
                var tracking = $"PART-{_trackingCounter:D6}";
                eligibleForEntry.CurrentPart = new TrackPart(Guid.NewGuid(), tracking);
                eligibleForEntry.State = TrackMoverState.Loaded;
                eligibleForEntry.TargetMachineIndex = _machineIds[0];
                partEntered = true;
                newPartId = eligibleForEntry.CurrentPart.PartId;
                newTracking = tracking;
                machineRoute = _machineIds.ToList();
                Entered++;
            }

            // Exit zone: collect completed parts
            var atExit = _movers.FirstOrDefault(m =>
                m.CurrentPart != null &&
                m.TargetMachineIndex == ExitTargetIndex &&
                IsAtAngle(m.Position, ExitAngle, 5.0));

            if (atExit != null)
            {
                bool good = !atExit.CurrentPart!.HasDefect;
                atExit.CurrentPart = null;
                atExit.State = TrackMoverState.Moving;
                atExit.TargetMachineIndex = -1;
                partExited = true;
                exitedGood = good;
                TotalParts++;
                if (good) GoodParts++; else BadParts++;
            }

            return new TrackTickResult(partEntered, newPartId, newTracking, machineRoute, partExited, exitedGood, GetMoverDtos());
        }
    }

    public IReadOnlyList<MoverDto> GetMoverStatuses()
    {
        lock (_lock)
        {
            return GetMoverDtos();
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
