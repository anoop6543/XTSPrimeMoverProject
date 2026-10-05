using System;
using System.Collections.Generic;
using System.Linq;

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

    public enum MaintenanceMode
    {
        /// <summary>Available for production.</summary>
        None,
        /// <summary>Maintenance requested; no new parts accepted, current part drains out.</summary>
        Pending,
        /// <summary>Technician working on the cell; machine is down.</summary>
        InProgress
    }

    public enum MaintenanceKind
    {
        /// <summary>Planned / predictive maintenance (short, scheduled).</summary>
        Planned,
        /// <summary>Unplanned breakdown repair (long).</summary>
        Breakdown
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

        /// <summary>
        /// Outfeed nest: a finished part leaves the last station into this buffer so the machine can
        /// accept the next part. The cell robot transfers it onto a docked mover (part swap).
        /// </summary>
        public Part? OutfeedNest { get; set; }

        public MaintenanceMode Maintenance { get; set; }
        public MaintenanceKind MaintenanceKind { get; set; }
        public double MaintenanceDurationSeconds { get; set; }
        public double MaintenanceRemainingSeconds { get; set; }
        public string MaintenanceReason { get; set; } = string.Empty;
        public int MaintenanceCount { get; set; }
        public int BreakdownCount { get; set; }

        /// <summary>Sum of the nominal station times: the ideal machine cycle used for OEE performance.</summary>
        public double IdealCycleTimeSeconds => Stations.Sum(s => s.ProcessTime);

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

        /// <summary>
        /// EV battery module line (12S prismatic module):
        /// M0 stacks/compresses the cells and laser-welds the busbars,
        /// M1 mounts and fastens the cell monitoring unit (CMU) board,
        /// M2 runs 3D vision / gauging / weighing,
        /// M3 runs the end-of-line electrical tests and laser-marks the module.
        /// Defect rates are the legacy fallback only; quality is decided by measured values.
        /// </summary>
        private void InitializeStations()
        {
            switch (Type)
            {
                case MachineType.LaserWelding:
                    Stations.Add(new Station(0, "Cell Stack Compression", StationType.Assembly, 2.0, 0.005));
                    Stations.Add(new Station(1, "Busbar Laser Weld", StationType.Welding, 3.5, 0.01));
                    Stations.Add(new Station(2, "Weld Cool-Down", StationType.Assembly, 2.0, 0.002));
                    Stations.Add(new Station(3, "Weld Seam OCT Scan", StationType.Inspection, 1.5, 0.005));
                    break;

                case MachineType.PrecisionAssembly:
                    Stations.Add(new Station(0, "CMU Board Pick", StationType.Assembly, 1.5, 0.002));
                    Stations.Add(new Station(1, "CMU Board Place", StationType.Assembly, 2.5, 0.005));
                    Stations.Add(new Station(2, "Screw Fastening", StationType.Assembly, 2.0, 0.008));
                    Stations.Add(new Station(3, "Torque/Angle Verify", StationType.Testing, 1.5, 0.004));
                    Stations.Add(new Station(4, "Connector Vision Check", StationType.Inspection, 1.0, 0.003));
                    break;

                case MachineType.QualityInspection:
                    Stations.Add(new Station(0, "3D Vision Inspect", StationType.Inspection, 2.0, 0.004));
                    Stations.Add(new Station(1, "Module Height Gauge", StationType.Inspection, 2.5, 0.004));
                    Stations.Add(new Station(2, "Busbar Surface Scan", StationType.Inspection, 2.0, 0.003));
                    Stations.Add(new Station(3, "Module Weight Check", StationType.Testing, 1.0, 0.002));
                    break;

                case MachineType.FunctionalTesting:
                    Stations.Add(new Station(0, "HiPot Insulation Test", StationType.Testing, 3.0, 0.004));
                    Stations.Add(new Station(1, "OCV & DC-IR Test", StationType.Testing, 4.0, 0.006));
                    Stations.Add(new Station(2, "BMS Balancing Test", StationType.Testing, 3.5, 0.005));
                    Stations.Add(new Station(3, "EOL Final Verify", StationType.Testing, 2.0, 0.003));
                    Stations.Add(new Station(4, "Laser Mark DMC", StationType.Packaging, 1.0, 0.002));
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
            if (!IsOperational || Maintenance != MaintenanceMode.None)
            {
                return false;
            }

            // Current machine sequencing model supports one indexed part at a time.
            // Prevent loading a new part until all stations are empty.
            return Stations.TrueForAll(s => s.CurrentPart == null && s.Status == StationStatus.Idle);
        }

        /// <summary>
        /// A robot already holding a part for this machine may still place it while maintenance is only
        /// pending (the part drains through first); it must not place during the maintenance itself.
        /// </summary>
        public bool CanCompleteInboundTransfer()
        {
            return IsOperational
                   && Maintenance != MaintenanceMode.InProgress
                   && Stations.TrueForAll(s => s.CurrentPart == null && s.Status == StationStatus.Idle);
        }

        public bool IsEmpty => Stations.TrueForAll(s => s.CurrentPart == null);

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
