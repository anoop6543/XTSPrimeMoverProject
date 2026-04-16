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

    public class McMoveVelocityFb
    {
        public bool Execute { get; set; }
        public double Velocity { get; set; }
        public double Acceleration { get; set; } = 160.0;

        public bool Busy { get; private set; }
        public bool InVelocity { get; private set; }
        public double ActualVelocity { get; private set; }

        public void Update(double deltaTime, bool axisReady)
        {
            if (!axisReady || !Execute)
            {
                Busy = false;
                InVelocity = false;
                return;
            }

            Busy = true;

            double delta = Acceleration * deltaTime;
            if (Math.Abs(ActualVelocity - Velocity) <= delta)
            {
                ActualVelocity = Velocity;
            }
            else
            {
                ActualVelocity += Math.Sign(Velocity - ActualVelocity) * delta;
            }

            InVelocity = Math.Abs(ActualVelocity - Velocity) < 0.01;
        }

        public void ForceVelocity(double velocity)
        {
            ActualVelocity = velocity;
            InVelocity = Math.Abs(ActualVelocity) < 0.01;
            Busy = false;
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

    public class FbXtsMoverAxis
    {
        public McPowerFb Power { get; } = new();
        public McMoveVelocityFb MoveVelocity { get; } = new();
        public McHaltFb Halt { get; } = new();

        public bool Powered => Power.Status;
        public double ActualVelocity => MoveVelocity.ActualVelocity;

        public void Cycle(Mover mover, double deltaTime, bool runCommand, bool holdAtStation)
        {
            Power.Enable = runCommand;
            Power.Update();

            MoveVelocity.Execute = runCommand && !holdAtStation;
            MoveVelocity.Velocity = mover.CurrentPart == null ? 34.0 : 26.0;
            MoveVelocity.Update(deltaTime, Power.Status);

            Halt.Execute = runCommand && holdAtStation;

            double commandedVelocity = MoveVelocity.ActualVelocity;
            commandedVelocity = Halt.Update(commandedVelocity, deltaTime);
            MoveVelocity.ForceVelocity(commandedVelocity);

            mover.Velocity = commandedVelocity;
            if (Math.Abs(commandedVelocity) > 0.0001)
            {
                mover.UpdatePosition(deltaTime);
            }
        }
    }
}
