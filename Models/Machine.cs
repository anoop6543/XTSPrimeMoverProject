using System;
using System.Collections.Generic;

namespace XTSPrimeMoverProject.Models
{
    public enum MachineType
    {
        LaserWelding,
        PrecisionAssembly,
        QualityInspection,
        FunctionalTesting
    }

    public enum PlcSequencerState
    {
        Init,
        Ready,
        Run,
        Fault,
        Reset
    }

    public class Machine
    {
        public int MachineId { get; set; }
        public string Name { get; set; }
        public MachineType Type { get; set; }
        public List<Station> Stations { get; set; }
        public int CurrentStationIndex { get; set; }
        public double LoadAngle { get; set; }
        public bool IsOperational { get; set; }
        public int PartsEnteredCount { get; set; }
        public int PartsExitedCount { get; set; }
        public PlcSequencerState SequencerState { get; set; }
        public bool IsIndexing { get; set; }
        public bool FaultActive { get; set; }
        public string FaultMessage { get; set; }
        public double RotaryAngle { get; set; }

        public Machine(int id, string name, MachineType type, double loadAngle)
        {
            MachineId = id;
            Name = name;
            Type = type;
            LoadAngle = loadAngle;
            Stations = new List<Station>();
            CurrentStationIndex = 0;
            IsOperational = true;
            PartsEnteredCount = 0;
            PartsExitedCount = 0;
            SequencerState = PlcSequencerState.Init;
            IsIndexing = false;
            FaultActive = false;
            FaultMessage = string.Empty;
            RotaryAngle = 0;

            InitializeStations();
        }

        private void InitializeStations()
        {
            switch (Type)
            {
                case MachineType.LaserWelding:
                    Stations.Add(new Station(0, "Pre-Heat", StationType.Assembly, 2.0));
                    Stations.Add(new Station(1, "Laser Weld", StationType.Welding, 3.5, 0.03));
                    Stations.Add(new Station(2, "Cool Down", StationType.Assembly, 2.0));
                    Stations.Add(new Station(3, "Weld Inspection", StationType.Inspection, 1.5, 0.02));
                    break;

                case MachineType.PrecisionAssembly:
                    Stations.Add(new Station(0, "Component Pick", StationType.Assembly, 1.5));
                    Stations.Add(new Station(1, "Precision Place", StationType.Assembly, 2.5, 0.04));
                    Stations.Add(new Station(2, "Screw Drive", StationType.Assembly, 2.0, 0.03));
                    Stations.Add(new Station(3, "Torque Verify", StationType.Testing, 1.5, 0.02));
                    Stations.Add(new Station(4, "Vision Check", StationType.Inspection, 1.0, 0.01));
                    break;

                case MachineType.QualityInspection:
                    Stations.Add(new Station(0, "Visual Inspect", StationType.Inspection, 2.0, 0.05));
                    Stations.Add(new Station(1, "Dimension Check", StationType.Inspection, 2.5, 0.04));
                    Stations.Add(new Station(2, "Surface Scan", StationType.Inspection, 2.0, 0.03));
                    Stations.Add(new Station(3, "Weight Check", StationType.Testing, 1.0, 0.01));
                    break;

                case MachineType.FunctionalTesting:
                    Stations.Add(new Station(0, "Power-On Test", StationType.Testing, 3.0, 0.06));
                    Stations.Add(new Station(1, "Function Test", StationType.Testing, 4.0, 0.07));
                    Stations.Add(new Station(2, "Stress Test", StationType.Testing, 3.5, 0.05));
                    Stations.Add(new Station(3, "Final Verify", StationType.Testing, 2.0, 0.02));
                    Stations.Add(new Station(4, "Label Print", StationType.Packaging, 1.0));
                    break;
            }
        }

        public void Update(double deltaTime)
        {
            if (CurrentStationIndex >= 0 && CurrentStationIndex < Stations.Count)
            {
                Stations[CurrentStationIndex].Update(deltaTime);
            }
        }

        public bool CanAcceptPart()
        {
            if (!IsOperational)
            {
                return false;
            }

            // Current machine sequencing model supports one indexed part at a time.
            // Prevent loading a new part until all stations are empty.
            return Stations.TrueForAll(s => s.CurrentPart == null && s.Status == StationStatus.Idle);
        }

        public bool HasCompletedPartReady()
        {
            return CurrentStationIndex == Stations.Count - 1
                   && Stations[CurrentStationIndex].Status == StationStatus.Complete
                   && Stations[CurrentStationIndex].CurrentPart != null;
        }

        public void LoadPart(Part part)
        {
            if (Stations.Count > 0)
            {
                Stations[0].StartProcessing(part);
                CurrentStationIndex = 0;
                PartsEnteredCount++;
            }
        }

        public bool TryMoveToNextStation()
        {
            if (CurrentStationIndex >= 0 && CurrentStationIndex < Stations.Count)
            {
                if (Stations[CurrentStationIndex].Status == StationStatus.Complete)
                {
                    if (CurrentStationIndex < Stations.Count - 1)
                    {
                        if (Stations[CurrentStationIndex + 1].Status == StationStatus.Idle)
                        {
                            Part? part = Stations[CurrentStationIndex].CompletePart();
                            if (part == null)
                            {
                                return false;
                            }

                            CurrentStationIndex++;
                            Stations[CurrentStationIndex].StartProcessing(part);
                            return false;
                        }
                    }
                    else
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        public Part? UnloadPart()
        {
            if (CurrentStationIndex == Stations.Count - 1)
            {
                var part = Stations[CurrentStationIndex].CompletePart();
                if (part != null)
                {
                    PartsExitedCount++;
                }

                return part;
            }

            return null;
        }
    }
}
