using System;
using System.Collections.Generic;

namespace XTSPrimeMoverProject.Models
{
    public enum PartStatus
    {
        Empty,
        BaseLayer,
        InProcess,
        Assembled,
        Tested,
        Good,
        Bad
    }

    public class Part
    {
        public Guid PartId { get; set; }
        public string TrackingNumber { get; set; }
        public PartStatus Status { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? EnteredPrimeMoverAt { get; set; }
        public DateTime? ExitedPrimeMoverAt { get; set; }
        public int ProcessStep { get; set; }
        public string[] ProcessHistory { get; set; }
        public bool HasDefect { get; set; }
        public int NextMachineIndex { get; set; }
        public int CompletedStations { get; set; }
        public string CurrentLocation { get; set; }

        /// <summary>Number of machine operations completed (drives the 3D build-up of the battery module).</summary>
        public int MachinesCompleted { get; set; }

        /// <summary>Simulation time (seconds) at which the part entered the XTS line; used for flow-time analytics.</summary>
        public double EnteredSimTime { get; set; }

        /// <summary>In-process measurements, one per station (full genealogy for traceability).</summary>
        public List<StationMeasurement> Measurements { get; } = new();

        public Part()
        {
            PartId = Guid.NewGuid();
            TrackingNumber = string.Empty;
            Status = PartStatus.BaseLayer;
            CreatedAt = DateTime.Now;
            ProcessStep = 0;
            ProcessHistory = new string[64];
            HasDefect = false;
            NextMachineIndex = 0;
            CompletedStations = 0;
            CurrentLocation = "PrimeMover";
        }

        public double GetCompletionPercent(int totalStations)
        {
            if (totalStations <= 0)
            {
                return 0;
            }

            return Math.Min(100.0, CompletedStations * 100.0 / totalStations);
        }

        public void AddProcessHistory(string operation)
        {
            if (ProcessStep < ProcessHistory.Length)
            {
                ProcessHistory[ProcessStep] = $"{DateTime.Now:HH:mm:ss.fff} - {operation}";
                ProcessStep++;
            }
        }
    }
}
