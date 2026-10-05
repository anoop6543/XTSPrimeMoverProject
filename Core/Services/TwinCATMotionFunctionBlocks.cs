using System;
using XTSPrimeMoverProject.Models;

namespace XTSPrimeMoverProject.Services
{
    public class McPowerFb
    {
        public bool Enable { get; set; }
        public bool Status { get; private set; }
        public bool Busy { get; private set; }
        public bool Error { get; private set; }

        public void Update()
        {
            Busy = true;
            Status = Enable;
            Error = false;
            Busy = false;
        }
    }

    /// <summary>
    /// Velocity command with a jerk-limited (S-curve) profile, like MC_MoveVelocity with a jerk parameter.
    /// The acceleration ramps at <see cref="Jerk"/> towards the value that lands exactly on the target
    /// velocity, so there is no overshoot and no acceleration step (smooth, low-vibration motion).
    /// </summary>
    public class McMoveVelocityFb
    {
        public bool Execute { get; set; }
        public double Velocity { get; set; }
        public double Acceleration { get; set; } = 160.0;
        public double Deceleration { get; set; } = 220.0;
        public double Jerk { get; set; } = 4000.0;

        public bool Busy { get; private set; }
        public bool InVelocity { get; private set; }
        public double ActualVelocity { get; private set; }
        public double ActualAcceleration { get; private set; }

        public void Update(double deltaTime, bool axisReady)
        {
            if (!axisReady || !Execute)
            {
                Busy = false;
                InVelocity = false;
                return;
            }

            Busy = true;
            double v = ActualVelocity;
            double a = ActualAcceleration;
            JerkLimitedStep(ref v, ref a, Velocity, Acceleration, Deceleration, Jerk, deltaTime);
            ActualVelocity = v;
            ActualAcceleration = a;
            InVelocity = Math.Abs(ActualVelocity - Velocity) < 0.01;
        }

        public void ForceVelocity(double velocity)
        {
            ActualVelocity = velocity;
            ActualAcceleration = 0;
            InVelocity = Math.Abs(ActualVelocity) < 0.01;
            Busy = false;
        }

        /// <summary>One jerk-limited integration step toward <paramref name="target"/> (velocities are non-negative on the loop).</summary>
        public static void JerkLimitedStep(ref double velocity, ref double acceleration, double target, double maxAcceleration, double maxDeceleration, double jerk, double dt)
        {
            double dv = target - velocity;
            if (Math.Abs(dv) < 1e-4 && Math.Abs(acceleration) < 1e-2)
            {
                velocity = target;
                acceleration = 0;
                return;
            }

            double direction = Math.Sign(dv);
            double limit = direction > 0 ? maxAcceleration : maxDeceleration;
            double maxDelta = jerk * dt;

            // Discrete-time braking curve: the largest acceleration from which ramping down at the jerk
            // limit, one PLC cycle at a time, lands exactly on the target velocity.
            double halfStep = 0.5 * maxDelta;
            double landing = Math.Sqrt(2.0 * jerk * Math.Abs(dv) + halfStep * halfStep) - halfStep;
            double desired = direction * Math.Min(limit, landing);
            acceleration += Math.Clamp(desired - acceleration, -maxDelta, maxDelta);

            double next = velocity + acceleration * dt;
            if ((target - next) * direction <= 0 || Math.Abs(target - next) < 1e-6)
            {
                next = target;
                acceleration = Math.Abs(acceleration) <= maxDelta ? 0 : acceleration - Math.Sign(acceleration) * maxDelta;
            }

            velocity = Math.Max(0, next);
        }
    }

    public class McHaltFb
    {
        public bool Execute { get; set; }
        public double Deceleration { get; set; } = 220.0;

        public bool Busy { get; private set; }
        public bool Done { get; private set; }

        public double Update(double currentVelocity, double deltaTime)
        {
            if (!Execute)
            {
                Busy = false;
                Done = false;
                return currentVelocity;
            }

            Busy = true;
            double delta = Deceleration * deltaTime;

            if (Math.Abs(currentVelocity) <= delta)
            {
                Busy = false;
                Done = true;
                return 0;
            }

            Done = false;
            return currentVelocity - Math.Sign(currentVelocity) * delta;
        }
    }

    /// <summary>
    /// XTS mover axis: power + jerk-limited velocity control. The engine's motion planner supplies a
    /// velocity limit (cruise, braking curve to the next dock, gap control to the mover ahead) and the
    /// axis integrates the S-curve. Units: degrees of loop position (see <see cref="XtsTrackGeometry"/>).
    /// </summary>
    public class FbXtsMoverAxis
    {
        public const double EmptyCruiseVelocity = 34.0;   // ≈ 0.57 m/s
        public const double LoadedCruiseVelocity = 26.0;  // ≈ 0.43 m/s with a 12 kg module
        public const double MaxAcceleration = 160.0;      // ≈ 2.7 m/s²
        public const double MaxDeceleration = 220.0;      // ≈ 3.7 m/s²
        public const double MaxJerk = 4000.0;             // ≈ 67 m/s³

        public McPowerFb Power { get; } = new();
        public McMoveVelocityFb MoveVelocity { get; } = new()
        {
            Acceleration = MaxAcceleration,
            Deceleration = MaxDeceleration,
            Jerk = MaxJerk
        };
        public McHaltFb Halt { get; } = new() { Deceleration = MaxDeceleration };

        public bool Powered => Power.Status;
        public double ActualVelocity => MoveVelocity.ActualVelocity;

        /// <summary>Runs one PLC cycle and returns the commanded displacement in degrees.</summary>
        public double Cycle(Mover mover, double deltaTime, bool enable, double velocityLimit)
        {
            Power.Enable = enable;
            Power.Update();

            MoveVelocity.Execute = enable;
            MoveVelocity.Velocity = Math.Max(0, velocityLimit);
            MoveVelocity.Update(deltaTime, Power.Status);

            if (!Power.Status)
            {
                Halt.Execute = true;
                MoveVelocity.ForceVelocity(Halt.Update(MoveVelocity.ActualVelocity, deltaTime));
            }
            else
            {
                Halt.Execute = false;
            }

            mover.Velocity = MoveVelocity.ActualVelocity;
            mover.Acceleration = MoveVelocity.ActualAcceleration;
            return mover.Velocity * deltaTime;
        }

        /// <summary>Hard stop (dock reached or collision clamp).</summary>
        public void Stop(Mover mover)
        {
            MoveVelocity.ForceVelocity(0);
            mover.Velocity = 0;
            mover.Acceleration = 0;
        }

        /// <summary>Clamp to the mover ahead's speed when the gap controller had to intervene.</summary>
        public void LimitVelocity(Mover mover, double velocity)
        {
            if (MoveVelocity.ActualVelocity > velocity)
            {
                MoveVelocity.ForceVelocity(velocity);
                mover.Velocity = velocity;
                mover.Acceleration = 0;
            }
        }
    }
}
