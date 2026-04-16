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
        public int StationId { get; set; }
        public string Name { get; set; }
        public StationType Type { get; set; }
        public StationStatus Status { get; set; }
        public Part CurrentPart { get; set; }
        public double ProcessTime { get; set; }
        public double ElapsedTime { get; set; }
        public double DefectRate { get; set; }

        public Station(int id, string name, StationType type, double processTime, double defectRate = 0.05)
        {
            StationId = id;
            Name = name;
            Type = type;
            Status = StationStatus.Idle;
            ProcessTime = processTime;
            ElapsedTime = 0;
            DefectRate = defectRate;
        }

        public bool Update(double deltaTime)
        {
            if (Status == StationStatus.Processing && CurrentPart != null)
            {
                ElapsedTime += deltaTime;
                if (ElapsedTime >= ProcessTime)
                {
                    Status = StationStatus.Complete;

                    Random rand = new Random(Guid.NewGuid().GetHashCode());
                    if (rand.NextDouble() < DefectRate)
                    {
                        CurrentPart.HasDefect = true;
                    }

                    CurrentPart.AddProcessHistory($"{Name} - {Type}");
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
        }

        public Part CompletePart()
        {
            Part part = CurrentPart;
            CurrentPart = null;
            Status = StationStatus.Idle;
            ElapsedTime = 0;
            return part;
        }
    }
}
