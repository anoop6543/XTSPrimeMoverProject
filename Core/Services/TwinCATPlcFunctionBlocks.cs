using XTSPrimeMoverProject.Models;

namespace XTSPrimeMoverProject.Services
{
    public class TonFb
    {
        public bool IN { get; set; }
        public double PTSeconds { get; set; }
        public bool Q { get; private set; }
        public double ETSeconds { get; private set; }

        public void Update(double deltaTime)
        {
            if (!IN)
            {
                ETSeconds = 0;
                Q = false;
                return;
            }

            ETSeconds += deltaTime;
            Q = ETSeconds >= PTSeconds;
        }
    }

    public class AlarmLatchFb
    {
        public bool Set { get; set; }
        public bool Reset { get; set; }
        public bool Q { get; private set; }

        public void Update()
        {
            if (Reset)
            {
                Q = false;
                return;
            }

            if (Set)
            {
                Q = true;
            }
        }
    }

    public class FbMachineCycle
    {
        private readonly TonFb _stationTimeoutTon = new();
        private readonly AlarmLatchFb _alarmLatch = new();
        private double _indexPulseRemaining;
        private int _lastStationIndex = -1;

        public bool Enable { get; set; }
        public bool InterlockPermit { get; set; }
        public bool ResetAlarms { get; set; }

        public bool Busy { get; private set; }
        public bool Done { get; private set; }
        public bool AlarmActive => _alarmLatch.Q;
        public string AlarmText { get; private set; } = string.Empty;

        public void Cycle(Machine machine, double deltaTime)
        {
            if (ResetAlarms)
            {
                machine.SequencerState = PlcSequencerState.Reset;
            }

            switch (machine.SequencerState)
            {
                case PlcSequencerState.Init:
                    Busy = false;
                    Done = false;
                    machine.FaultActive = false;
                    machine.FaultMessage = string.Empty;
                    machine.IsIndexing = false;
                    _lastStationIndex = machine.CurrentStationIndex;
                    if (Enable)
                    {
                        machine.SequencerState = PlcSequencerState.Ready;
                    }
                    break;

                case PlcSequencerState.Ready:
                    Busy = false;
                    Done = false;
                    machine.IsIndexing = false;
                    _stationTimeoutTon.IN = false;
                    _stationTimeoutTon.Update(0);
                    _lastStationIndex = machine.CurrentStationIndex;
                    if (!Enable)
                    {
                        return;
                    }

                    if (!InterlockPermit)
                    {
                        SetFault(machine, $"Machine {machine.MachineId} interlock not permitted in READY.");
                        machine.SequencerState = PlcSequencerState.Fault;
                        return;
                    }

                    machine.SequencerState = PlcSequencerState.Run;
                    break;

                case PlcSequencerState.Run:
                    Busy = true;
                    Done = false;

                    if (!Enable)
                    {
                        machine.SequencerState = PlcSequencerState.Ready;
                        machine.IsIndexing = false;
                        _stationTimeoutTon.IN = false;
                        _stationTimeoutTon.Update(deltaTime);
                        return;
                    }

                    if (!InterlockPermit)
                    {
                        SetFault(machine, $"Machine {machine.MachineId} interlock lost during RUN.");
                        machine.SequencerState = PlcSequencerState.Fault;
                        return;
                    }

                    int previousIndex = machine.CurrentStationIndex;
                    machine.Update(deltaTime);
                    Done = machine.TryMoveToNextStation();

                    bool indexedToNext = machine.CurrentStationIndex != previousIndex;
                    if (indexedToNext)
                    {
                        machine.RotaryAngle = (machine.RotaryAngle + (360.0 / machine.Stations.Count)) % 360.0;
                        _indexPulseRemaining = 0.35;
                    }

                    if (_indexPulseRemaining > 0)
                    {
                        _indexPulseRemaining -= deltaTime;
                        machine.IsIndexing = true;
                    }
                    else
                    {
                        machine.IsIndexing = false;
                    }

                    var station = machine.CurrentStationIndex >= 0 && machine.CurrentStationIndex < machine.Stations.Count
                        ? machine.Stations[machine.CurrentStationIndex]
                        : null;

                    if (_lastStationIndex != machine.CurrentStationIndex)
                    {
                        _stationTimeoutTon.IN = false;
                        _stationTimeoutTon.Update(0);
                        _lastStationIndex = machine.CurrentStationIndex;
                    }

                    bool inProcessing = station is not null && station.Status == StationStatus.Processing;
                    _stationTimeoutTon.IN = inProcessing;
                    _stationTimeoutTon.PTSeconds = station is null ? 0 : station.ProcessTime * 2.0;
                    _stationTimeoutTon.Update(deltaTime);

                    if (inProcessing && _stationTimeoutTon.Q)
                    {
                        SetFault(machine, $"Machine {machine.MachineId} station '{station!.Name}' exceeded timeout ({_stationTimeoutTon.ETSeconds:F2}s).");
                        machine.SequencerState = PlcSequencerState.Fault;
                        return;
                    }

                    if (!_alarmLatch.Q)
                    {
                        machine.FaultActive = false;
                        machine.FaultMessage = string.Empty;
                        AlarmText = string.Empty;
                    }

                    break;

                case PlcSequencerState.Fault:
                    Busy = false;
                    Done = false;
                    machine.IsIndexing = false;
                    machine.FaultActive = true;
                    machine.FaultMessage = string.IsNullOrWhiteSpace(AlarmText) ? "Machine fault." : AlarmText;
                    if (ResetAlarms)
                    {
                        machine.SequencerState = PlcSequencerState.Reset;
                    }
                    break;

                case PlcSequencerState.Reset:
                    Busy = false;
                    Done = false;
                    machine.IsIndexing = false;
                    _indexPulseRemaining = 0;
                    _stationTimeoutTon.IN = false;
                    _stationTimeoutTon.Update(0);
                    _lastStationIndex = machine.CurrentStationIndex;

                    _alarmLatch.Set = false;
                    _alarmLatch.Reset = true;
                    _alarmLatch.Update();

                    machine.FaultActive = false;
                    machine.FaultMessage = string.Empty;
                    AlarmText = string.Empty;

                    machine.SequencerState = Enable ? PlcSequencerState.Ready : PlcSequencerState.Init;
                    break;
            }
        }

        private void SetFault(Machine machine, string text)
        {
            _alarmLatch.Reset = false;
            _alarmLatch.Set = true;
            _alarmLatch.Update();
            AlarmText = text;
            machine.FaultActive = true;
            machine.FaultMessage = text;
        }
    }
}
