using System;

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
        public PartStatus Status { get; set; }
        public DateTime CreatedAt { get; set; }
        public int ProcessStep { get; set; }
        public string[] ProcessHistory { get; set; }
        public bool HasDefect { get; set; }

        public Part()
        {
            PartId = Guid.NewGuid();
            Status = PartStatus.BaseLayer;
            CreatedAt = DateTime.Now;
            ProcessStep = 0;
            ProcessHistory = new string[20];
            HasDefect = false;
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
