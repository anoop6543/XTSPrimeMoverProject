using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Threading;
using XTSPrimeMoverProject.Models;

namespace XTSPrimeMoverProject.Services
{
    public class XTSSimulationEngine
    {
        private DispatcherTimer _timer;
        private DateTime _lastUpdate;
        private Random _random;

        public List<Mover> Movers { get; private set; }
        public List<Machine> Machines { get; private set; }
        public List<Robot> Robots { get; private set; }

        public int TotalPartsProduced { get; private set; }
        public int GoodPartsCount { get; private set; }
        public int BadPartsCount { get; private set; }
        public bool IsRunning { get; private set; }

        public event EventHandler StateChanged;

        private double[] _machineLoadAngles = { 45, 135, 225, 315 };
        private int _nextPartCounter = 0;

        public XTSSimulationEngine()
        {
            _random = new Random();
            InitializeSystem();

            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromMilliseconds(50);
            _timer.Tick += OnTimerTick;
        }

        private void InitializeSystem()
        {
            Movers = new List<Mover>();
            for (int i = 0; i < 10; i++)
            {
                Movers.Add(new Mover(i));
            }

            Machines = new List<Machine>
            {
                new Machine(0, "Laser Welder", MachineType.LaserWelding, _machineLoadAngles[0]),
                new Machine(1, "Assembler", MachineType.PrecisionAssembly, _machineLoadAngles[1]),
                new Machine(2, "Inspector", MachineType.QualityInspection, _machineLoadAngles[2]),
                new Machine(3, "Tester", MachineType.FunctionalTesting, _machineLoadAngles[3])
            };

            Robots = new List<Robot>();
            for (int i = 0; i < 4; i++)
            {
                Robots.Add(new Robot(i, i));
            }

            TotalPartsProduced = 0;
            GoodPartsCount = 0;
            BadPartsCount = 0;
        }

        public void Start()
        {
            IsRunning = true;
            _lastUpdate = DateTime.Now;
            _timer.Start();
        }

        public void Stop()
        {
            IsRunning = false;
            _timer.Stop();
        }

        public void Reset()
        {
            Stop();
            InitializeSystem();
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        private void OnTimerTick(object sender, EventArgs e)
        {
            DateTime now = DateTime.Now;
            double deltaTime = (now - _lastUpdate).TotalSeconds;
            _lastUpdate = now;

            Update(deltaTime);
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        private void Update(double deltaTime)
        {
            UpdateMovers(deltaTime);
            UpdateRobots(deltaTime);
            UpdateMachines(deltaTime);
            ProcessRobotLogic();
            ProcessMachineLogic();
            LoadNewParts();
        }

        private void UpdateMovers(double deltaTime)
        {
            foreach (var mover in Movers)
            {
                if (mover.State == MoverState.Moving || mover.State == MoverState.Loaded)
                {
                    mover.UpdatePosition(deltaTime);
                }

                for (int i = 0; i < _machineLoadAngles.Length; i++)
                {
                    if (mover.IsAtStation(_machineLoadAngles[i]))
                    {
                        if (mover.CurrentPart == null)
                        {
                            mover.State = MoverState.AtLoadStation;
                            mover.TargetStation = i;
                        }
                        else
                        {
                            mover.State = MoverState.AtUnloadStation;
                            mover.TargetStation = i;
                        }
                        break;
                    }
                }
            }
        }

        private void UpdateRobots(double deltaTime)
        {
            foreach (var robot in Robots)
            {
                robot.Update(deltaTime);
            }
        }

        private void UpdateMachines(double deltaTime)
        {
            foreach (var machine in Machines)
            {
                machine.Update(deltaTime);
            }
        }

        private void ProcessRobotLogic()
        {
            for (int i = 0; i < Robots.Count; i++)
            {
                var robot = Robots[i];
                var machine = Machines[i];

                if (robot.State == RobotState.Idle)
                {
                    if (machine.TryMoveToNextStation())
                    {
                        Part completedPart = machine.UnloadPart();
                        if (completedPart != null)
                        {
                            robot.StartPickFromMachine(completedPart);
                        }
                    }
                    else if (machine.CanAcceptPart())
                    {
                        var mover = Movers.FirstOrDefault(m => 
                            m.State == MoverState.AtLoadStation && 
                            m.TargetStation == i && 
                            m.CurrentPart != null);

                        if (mover != null)
                        {
                            robot.StartPickFromMover(mover.CurrentPart);
                            mover.CurrentPart = null;
                            mover.State = MoverState.Moving;
                        }
                    }
                }
                else if (robot.State == RobotState.PlacingInMachine && robot.ActionProgress >= robot.ActionTime - 0.01)
                {
                    if (robot.HeldPart != null)
                    {
                        machine.LoadPart(robot.HeldPart);
                    }
                }
                else if (robot.State == RobotState.PlacingOnMover && robot.ActionProgress >= robot.ActionTime - 0.01)
                {
                    if (robot.HeldPart != null)
                    {
                        var mover = Movers.FirstOrDefault(m => 
                            m.State == MoverState.AtUnloadStation && 
                            m.TargetStation == i && 
                            m.CurrentPart == null);

                        if (mover != null)
                        {
                            mover.CurrentPart = robot.HeldPart;
                            mover.State = MoverState.Loaded;

                            if (mover.CurrentPart.HasDefect)
                            {
                                mover.CurrentPart.Status = PartStatus.Bad;
                            }
                            else if (i == Machines.Count - 1)
                            {
                                mover.CurrentPart.Status = PartStatus.Good;
                            }
                        }
                    }
                }
            }
        }

        private void ProcessMachineLogic()
        {
            foreach (var machine in Machines)
            {
                machine.TryMoveToNextStation();
            }
        }

        private void LoadNewParts()
        {
            if (_nextPartCounter <= 0)
            {
                var emptyMover = Movers.FirstOrDefault(m => 
                    m.CurrentPart == null && 
                    m.State != MoverState.AtLoadStation &&
                    m.State != MoverState.AtUnloadStation);

                if (emptyMover != null)
                {
                    var newPart = new Part();
                    newPart.AddProcessHistory("Loaded on XTS Mover");
                    emptyMover.CurrentPart = newPart;
                    emptyMover.State = MoverState.Loaded;
                    _nextPartCounter = _random.Next(20, 50);
                }
            }
            else
            {
                _nextPartCounter--;
            }

            foreach (var mover in Movers.Where(m => m.CurrentPart != null))
            {
                if (mover.CurrentPart.Status == PartStatus.Good)
                {
                    GoodPartsCount++;
                    TotalPartsProduced++;
                    mover.CurrentPart = null;
                    mover.State = MoverState.Moving;
                }
                else if (mover.CurrentPart.Status == PartStatus.Bad)
                {
                    BadPartsCount++;
                    TotalPartsProduced++;
                    mover.CurrentPart = null;
                    mover.State = MoverState.Moving;
                }
            }
        }
    }
}
