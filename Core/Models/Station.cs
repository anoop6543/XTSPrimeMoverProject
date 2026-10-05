using System;

namespace XTSPrimeMoverProject.Models
{
    public enum StationType
    {
        Assembly,
        Welding,
        Inspection,
        Testing,
        Packaging
    }

    public enum StationStatus
    {
        Idle,
        Processing,
        Complete,
        Error
    }

    public class Station
    {
        private static readonly Random FallbackRandom = new();

        public int StationId { get; set; }
        public string Name { get; set; }
        public StationType Type { get; set; }
        public StationStatus Status { get; set; }
        public Part? CurrentPart { get; set; }

        /// <summary>Nominal (recipe) process time. The PLC timeout (TON) is parameterised from this value.</summary>
        public double ProcessTime { get; set; }

        /// <summary>Actual process time for the current cycle, including health-related slowdown.</summary>
        public double EffectiveProcessTime { get; private set; }

        public double ElapsedTime { get; set; }

        /// <summary>Legacy defect probability, used only when no process model is attached.</summary>
        public double DefectRate { get; set; }

        /// <summary>Digital-twin process model (cycle time + measurement). Optional.</summary>
        public IStationProcessModel? ProcessModel { get; set; }

        public StationMeasurement? LastMeasurement { get; private set; }

        public Station(int id, string name, StationType type, double processTime, double defectRate = 0.05)
        {
            StationId = id;
            Name = name;
            Type = type;
            Status = StationStatus.Idle;
            ProcessTime = processTime;
            EffectiveProcessTime = processTime;
            ElapsedTime = 0;
            DefectRate = defectRate;
        }

        public bool Update(double deltaTime)
        {
            if (Status == StationStatus.Processing && CurrentPart != null)
            {
                ElapsedTime += deltaTime;
                if (ElapsedTime >= EffectiveProcessTime)
                {
                    Status = StationStatus.Complete;

                    string historyText = $"{Name} - {Type}";
                    var measurement = ProcessModel?.Measure(this, CurrentPart);
                    if (measurement != null)
                    {
                        LastMeasurement = measurement;
                        CurrentPart.Measurements.Add(measurement);
                        if (!measurement.InSpec)
                        {
                            CurrentPart.HasDefect = true;
                        }

                        historyText += $" | {measurement.Format()}";
                    }
                    else if (FallbackRandom.NextDouble() < DefectRate)
                    {
                        CurrentPart.HasDefect = true;
                    }

                    CurrentPart.CompletedStations++;
                    CurrentPart.CurrentLocation = Name;
                    CurrentPart.AddProcessHistory(historyText);
                    return true;
                }
            }
            return false;
        }

        public void StartProcessing(Part part)
        {
            CurrentPart = part;
            Status = StationStatus.Processing;
            ElapsedTime = 0;
            double factor = ProcessModel?.GetCycleTimeFactor(this) ?? 1.0;
            EffectiveProcessTime = ProcessTime * Math.Max(1.0, factor);
        }

        public Part? CompletePart()
        {
            Part? part = CurrentPart;
            CurrentPart = null;
            Status = StationStatus.Idle;
            ElapsedTime = 0;
            EffectiveProcessTime = ProcessTime;
            return part;
        }
    }
}
