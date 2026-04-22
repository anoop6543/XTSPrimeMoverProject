using System;

namespace XTSPrimeMoverProject.Models
{
    public enum MoverState
    {
        Idle,
        Moving,
        AtLoadStation,
        AtUnloadStation,
        Loaded
    }

    public class Mover
    {
        public int MoverId { get; set; }
        public double Position { get; set; }
        public double Velocity { get; set; }
        public MoverState State { get; set; }
        public Part? CurrentPart { get; set; }
        public int TargetStation { get; set; }

        public Mover(int id)
        {
            MoverId = id;
            Position = id * (360.0 / 10);
            Velocity = 30.0;
            State = MoverState.Idle;
            CurrentPart = null;
            TargetStation = -1;
        }

        public void UpdatePosition(double deltaTime)
        {
            Position += Velocity * deltaTime;
            if (Position >= 360.0)
                Position -= 360.0;
        }

        public bool IsAtStation(double stationAngle, double tolerance = 5.0)
        {
            double diff = Math.Abs(Position - stationAngle);
            return diff < tolerance || diff > (360 - tolerance);
        }
    }
}
