using System;

namespace XTSPrimeMoverProject.Models
{
    public enum MoverState
    {
        Idle,
        /// <summary>Empty carrier in transit.</summary>
        Moving,
        /// <summary>Docked at a machine, delivering its part to the cell robot.</summary>
        AtLoadStation,
        /// <summary>Docked at a machine, empty, receiving a finished part.</summary>
        AtUnloadStation,
        /// <summary>Loaded carrier in transit.</summary>
        Loaded,
        /// <summary>Stopped behind another mover (anti-collision gap control).</summary>
        Queued,
        /// <summary>Docked at the entry station while a new cell stack is loaded.</summary>
        AtEntryStation,
        /// <summary>Docked at the exit station while the finished module is unloaded.</summary>
        AtExitStation
    }

    public class Mover
    {
        public int MoverId { get; set; }
        public double Position { get; set; }
        public double Velocity { get; set; }
        public MoverState State { get; set; }
        public Part? CurrentPart { get; set; }
        public int TargetStation { get; set; }

        /// <summary>Actual acceleration from the jerk-limited motion profile (deg/s^2).</summary>
        public double Acceleration { get; set; }

        /// <summary>Velocity override (0..1], e.g. a reduced-speed zone or manual jog.</summary>
        public double VelocityScale { get; set; } = 1.0;

        /// <summary>Total distance travelled in meters (mover roller maintenance is mileage based on a real XTS).</summary>
        public double OdometerMeters { get; set; }

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
            double step = Velocity * deltaTime;
            Position += step;
            OdometerMeters += Math.Abs(XtsTrackGeometry.ToMeters(step));
            if (Position >= 360.0)
                Position -= 360.0;
        }

        /// <summary>Moves the carrier forward along the loop (wraps at 360°) and updates the odometer.</summary>
        public void Advance(double deltaDegrees)
        {
            Position += deltaDegrees;
            while (Position >= 360.0) Position -= 360.0;
            while (Position < 0) Position += 360.0;
            OdometerMeters += Math.Abs(XtsTrackGeometry.ToMeters(deltaDegrees));
        }

        public bool IsAtStation(double stationAngle, double tolerance = 5.0)
        {
            double diff = Math.Abs(Position - stationAngle);
            return diff < tolerance || diff > (360 - tolerance);
        }
    }
}
