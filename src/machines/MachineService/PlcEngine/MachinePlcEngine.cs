namespace MachineService.PlcEngine;

public enum MachineType { LaserWelding, PrecisionAssembly, QualityInspection, FunctionalTesting }
public enum PlcState { Init, Ready, Run, Fault, Reset }
public enum StationStatus { Idle, Processing, Complete, Error }

public record StationDefinition(int Id, string Name, string Type, double ProcessTime, double DefectRate = 0.05);

public class StationState
{
    public StationDefinition Definition { get; }
    public StationStatus Status { get; set; } = StationStatus.Idle;
    public double ElapsedTime { get; set; }
    public string? CurrentPartId { get; set; }
    public string? CurrentPartTracking { get; set; }
    public bool CurrentPartHasDefect { get; set; }

    public StationState(StationDefinition def) => Definition = def;
}

public record LoadedPart(string PartId, string TrackingNumber, bool HasDefect);

public class MachinePlcEngine
{
    private readonly object _lock = new();
    private readonly Random _random = new();
    private readonly System.Threading.Timer _timer;

    public int MachineId { get; }
    public MachineType Type { get; }
    public PlcState SequencerState { get; private set; } = PlcState.Init;
    public IReadOnlyList<StationState> Stations { get; }
    public int CurrentStationIndex { get; private set; }
    public bool IsIndexing { get; private set; }
    public double RotaryAngle { get; private set; }
    public bool FaultActive { get; private set; }
    public string FaultMessage { get; private set; } = string.Empty;
    public int PartsEntered { get; private set; }
    public int PartsExited { get; private set; }
    public LoadedPart? CurrentPart { get; private set; }
    public bool IsComplete { get; private set; }
    public bool HasDefect => Stations.Any(s => s.CurrentPartHasDefect);

    private double _indexPulseRemaining;
    private readonly double _tickSeconds = 0.1;

    public event EventHandler? StateChanged;

    public MachinePlcEngine(int machineId, MachineType type)
    {
        MachineId = machineId;
        Type = type;
        Stations = CreateStations(type).Select(d => new StationState(d)).ToList().AsReadOnly();
        _timer = new System.Threading.Timer(Tick, null, TimeSpan.FromSeconds(_tickSeconds), TimeSpan.FromSeconds(_tickSeconds));
        SequencerState = PlcState.Ready;
    }

    private static IEnumerable<StationDefinition> CreateStations(MachineType type) => type switch
    {
        MachineType.LaserWelding => new[]
        {
            new StationDefinition(0, "Pre-Heat", "Assembly", 2.0),
            new StationDefinition(1, "Laser Weld", "Welding", 3.5, 0.03),
            new StationDefinition(2, "Cool Down", "Assembly", 2.0),
            new StationDefinition(3, "Weld Inspection", "Inspection", 1.5, 0.02)
        },
        MachineType.PrecisionAssembly => new[]
        {
            new StationDefinition(0, "Component Pick", "Assembly", 1.5),
            new StationDefinition(1, "Precision Place", "Assembly", 2.5, 0.04),
            new StationDefinition(2, "Screw Drive", "Assembly", 2.0, 0.03),
            new StationDefinition(3, "Torque Verify", "Testing", 1.5, 0.02),
            new StationDefinition(4, "Vision Check", "Inspection", 1.0, 0.01)
        },
        MachineType.QualityInspection => new[]
        {
            new StationDefinition(0, "Visual Inspect", "Inspection", 2.0, 0.05),
            new StationDefinition(1, "Dimension Check", "Inspection", 2.5, 0.04),
            new StationDefinition(2, "Surface Scan", "Inspection", 2.0, 0.03),
            new StationDefinition(3, "Weight Check", "Testing", 1.0, 0.01)
        },
        MachineType.FunctionalTesting => new[]
        {
            new StationDefinition(0, "Power-On Test", "Testing", 3.0, 0.06),
            new StationDefinition(1, "Function Test", "Testing", 4.0, 0.07),
            new StationDefinition(2, "Stress Test", "Testing", 3.5, 0.05),
            new StationDefinition(3, "Final Verify", "Testing", 2.0, 0.02),
            new StationDefinition(4, "Label Print", "Packaging", 1.0)
        },
        _ => Array.Empty<StationDefinition>()
    };

    private void Tick(object? _)
    {
        lock (_lock)
        {
            if (CurrentPart == null || SequencerState != PlcState.Run) return;

            var station = Stations[CurrentStationIndex];

            if (station.Status == StationStatus.Processing)
            {
                station.ElapsedTime += _tickSeconds;

                // Timeout check (2× process time)
                if (station.ElapsedTime > station.Definition.ProcessTime * 2.0)
                {
                    FaultActive = true;
                    FaultMessage = $"Station '{station.Definition.Name}' exceeded timeout ({station.ElapsedTime:F1}s)";
                    SequencerState = PlcState.Fault;
                    StateChanged?.Invoke(this, EventArgs.Empty);
                    return;
                }

                if (station.ElapsedTime >= station.Definition.ProcessTime)
                {
                    // Station complete
                    if (_random.NextDouble() < station.Definition.DefectRate)
                        station.CurrentPartHasDefect = true;

                    station.Status = StationStatus.Complete;

                    if (CurrentStationIndex < Stations.Count - 1)
                    {
                        // Advance to next station
                        var nextStation = Stations[CurrentStationIndex + 1];
                        nextStation.CurrentPartId = station.CurrentPartId;
                        nextStation.CurrentPartTracking = station.CurrentPartTracking;
                        nextStation.CurrentPartHasDefect = station.CurrentPartHasDefect;
                        nextStation.Status = StationStatus.Processing;
                        nextStation.ElapsedTime = 0;

                        station.Status = StationStatus.Idle;
                        station.CurrentPartId = null;
                        station.CurrentPartTracking = null;
                        CurrentStationIndex++;

                        RotaryAngle = (RotaryAngle + (360.0 / Stations.Count)) % 360.0;
                        _indexPulseRemaining = 0.35;
                        IsIndexing = true;
                    }
                    else
                    {
                        // Final station complete — machine done
                        IsComplete = true;
                        SequencerState = PlcState.Ready;
                        PartsExited++;
                    }
                }
            }

            if (_indexPulseRemaining > 0)
            {
                _indexPulseRemaining -= _tickSeconds;
                if (_indexPulseRemaining <= 0) IsIndexing = false;
            }
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool LoadPart(string partId, string trackingNumber, bool hasDefect)
    {
        lock (_lock)
        {
            if (SequencerState != PlcState.Ready || CurrentPart != null) return false;

            CurrentPart = new LoadedPart(partId, trackingNumber, hasDefect);
            IsComplete = false;
            CurrentStationIndex = 0;

            var first = Stations[0];
            first.CurrentPartId = partId;
            first.CurrentPartTracking = trackingNumber;
            first.CurrentPartHasDefect = hasDefect;
            first.Status = StationStatus.Processing;
            first.ElapsedTime = 0;

            SequencerState = PlcState.Run;
            PartsEntered++;
            return true;
        }
    }

    public LoadedPart? UnloadPart()
    {
        lock (_lock)
        {
            if (!IsComplete) return null;
            var part = CurrentPart;
            CurrentPart = null;
            IsComplete = false;

            foreach (var s in Stations)
            {
                s.Status = StationStatus.Idle;
                s.CurrentPartId = null;
                s.CurrentPartTracking = null;
                s.ElapsedTime = 0;
            }
            return part;
        }
    }

    public void ResetFault()
    {
        lock (_lock)
        {
            FaultActive = false;
            FaultMessage = string.Empty;
            SequencerState = PlcState.Ready;
        }
    }

    public void Dispose() => _timer.Dispose();
}
