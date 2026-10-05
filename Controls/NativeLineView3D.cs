using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using HelixToolkit.Wpf;
using XTSPrimeMoverProject.Models;
using XTSPrimeMoverProject.Services;
using XTSPrimeMoverProject.Services.Intelligence;

namespace XTSPrimeMoverProject.Controls
{
    /// <summary>
    /// Native WPF 3D view of the line (HelixToolkit): no browser runtime needed. Orbit / zoom with the
    /// mouse, camera presets and an automatic fly-through tour. Uses the same loop geometry as the engine.
    /// Helix is Z-up: (x, height, depth) of the web twin maps to (x, -depth, height).
    /// </summary>
    public sealed class NativeLineView3D : ContentControl
    {
        private const double Radius = 0.55;
        private const double TrackLength = 6.0;
        private static readonly double Straight = (TrackLength - 2 * Math.PI * Radius) / 2;
        private const double TrackTop = 0.95;
        private const double CellOffset = 1.12;

        private readonly HelixViewport3D _viewport;
        private readonly List<(ModelVisual3D Mover, ModelVisual3D Part, GeometryModel3D PartModel)> _movers = new();
        private readonly List<CellVisual> _cells = new();
        private readonly DispatcherTimer _tourTimer;
        private readonly BillboardTextVisual3D _banner;
        private int _tourStep;
        private DateTime _lastUpdate = DateTime.MinValue;

        private static readonly Material Aluminium = Mat(Color.FromRgb(0xB8, 0xBE, 0xC6), 60);
        private static readonly Material Anodized = Mat(Color.FromRgb(0x22, 0x25, 0x2A), 30);
        private static readonly Material Cabinet = Mat(Color.FromRgb(0xAE, 0xB3, 0xB0), 20);
        private static readonly Material RobotOrange = Mat(Color.FromRgb(0xFF, 0x7A, 0x1A), 40);
        private static readonly Material CellBlue = Mat(Color.FromRgb(0x1D, 0x4F, 0x9C), 30);
        private static readonly Material GoodGreen = Mat(Color.FromRgb(0x22, 0xC5, 0x5E), 10);
        private static readonly Material BadRed = Mat(Color.FromRgb(0xEF, 0x44, 0x44), 10);
        private static readonly Material Floor = Mat(Color.FromRgb(0x4A, 0x50, 0x58), 5);

        public NativeLineView3D()
        {
            _viewport = new HelixViewport3D
            {
                Background = new LinearGradientBrush(Color.FromRgb(0x18, 0x1E, 0x26), Color.FromRgb(0x0A, 0x0D, 0x12), 90),
                ShowViewCube = true,
                ShowCoordinateSystem = false,
                ZoomExtentsWhenLoaded = false,
                IsHeadLightEnabled = false,
                ModelUpDirection = new Vector3D(0, 0, 1),
                CameraRotationMode = CameraRotationMode.Turnball,
                InfiniteSpin = false,
                Camera = new PerspectiveCamera
                {
                    Position = new Point3D(4.4, -5.2, 3.6),
                    LookDirection = new Vector3D(-4.4, 5.2, -2.8),
                    UpDirection = new Vector3D(0, 0, 1),
                    FieldOfView = 45,
                    NearPlaneDistance = 0.05
                }
            };
            _viewport.Children.Add(new SunLight { Altitude = 55, Azimuth = 140, Brightness = 0.9, Ambient = 0.35 });
            _viewport.Children.Add(new DefaultLights());
            Content = _viewport;

            BuildScene();
            _banner = new BillboardTextVisual3D
            {
                Text = "Native WPF 3D · drag to orbit · wheel to zoom",
                Position = new Point3D(0, 0, 2.9),
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromArgb(150, 10, 14, 20)),
                FontSize = 13,
                Padding = new Thickness(6, 3, 6, 3)
            };
            _viewport.Children.Add(_banner);
            SetPreset("overview", 0);

            _tourTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
            _tourTimer.Tick += (_, _) => NextTourStep();
        }

        public void SetPreset(string preset, double animationSeconds = 1.2)
        {
            _tourTimer?.Stop();
            ApplyPreset(preset, animationSeconds);
        }

        public void StartTour()
        {
            _tourStep = 0;
            NextTourStep();
            _tourTimer.Start();
        }

        /// <summary>Call on the UI thread after engine ticks (throttled to ≈15 Hz).</summary>
        public void Update(IMachineGatewayService gateway)
        {
            if (!IsVisible || (DateTime.UtcNow - _lastUpdate).TotalMilliseconds < 66)
            {
                return;
            }

            _lastUpdate = DateTime.UtcNow;
            var movers = gateway.Movers;
            for (int i = 0; i < movers.Count && i < _movers.Count; i++)
            {
                var m = movers[i];
                var (x, depth, tx, tdepth, nx, ndepth) = PathAt(m.Position);
                double yaw = Math.Atan2(-tdepth, tx) * 180 / Math.PI;
                var transform = new Transform3DGroup();
                transform.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 0, 1), -yaw)));
                transform.Children.Add(new TranslateTransform3D(x + nx * 0.09, -(depth + ndepth * 0.09), 0));
                _movers[i].Mover.Transform = transform;

                var part = m.CurrentPart;
                _movers[i].Part.Transform = part == null ? new ScaleTransform3D(0, 0, 0) : Transform3D.Identity;
                if (part != null)
                {
                    _movers[i].PartModel.Material = part.Status == PartStatus.Good ? GoodGreen : part.Status == PartStatus.Bad ? BadRed : CellBlue;
                }
            }

            var snapshot = gateway.GetIntelligenceSnapshot();
            for (int i = 0; i < _cells.Count && i < gateway.Machines.Count; i++)
            {
                var machine = gateway.Machines[i];
                var ai = snapshot.Machines.FirstOrDefault(s => s.MachineId == machine.MachineId);
                var robot = gateway.Robots.FirstOrDefault(r => r.AssignedMachineId == machine.MachineId);
                _cells[i].Update(machine, ai, robot);
            }

            _banner.Text = $"OEE {snapshot.Line.Oee:P0} · {snapshot.Line.ThroughputPerMinute:F2}/min · {snapshot.Line.PowerKw:F1} kW · {(snapshot.AutopilotEnabled ? "AI autopilot ON" : "manual")}";
        }

        private void NextTourStep()
        {
            string[] tour = { "overview", "m0", "m1", "m2", "m3", "top" };
            ApplyPreset(tour[_tourStep % tour.Length], 2.5);
            _tourStep++;
        }

        private void ApplyPreset(string preset, double animationSeconds)
        {
            Point3D target;
            Point3D position;
            if (preset.Length == 2 && preset[0] == 'm' && char.IsDigit(preset[1]))
            {
                int index = preset[1] - '0';
                double angle = XTSSimulationEngine.MachineLoadAngles[Math.Clamp(index, 0, 3)];
                var (x, depth, tx, tdepth, nx, ndepth) = PathAt(angle);
                target = new Point3D(x + nx * 0.7, -(depth + ndepth * 0.7), 1.0);
                position = new Point3D(x - nx * 1.0 + tx * 0.8, -(depth - ndepth * 1.0 + tdepth * 0.8), 2.0);
            }
            else if (preset == "top")
            {
                target = new Point3D(0, 0, 0.8);
                position = new Point3D(0.01, -0.6, 7.5);
            }
            else
            {
                target = new Point3D(0, 0, 0.8);
                position = new Point3D(4.4, -5.2, 3.6);
            }

            if (_viewport.Camera is not ProjectionCamera camera)
            {
                return;
            }

            var direction = target - position;
            if (animationSeconds <= 0)
            {
                camera.Position = position;
                camera.LookDirection = direction;
                camera.UpDirection = new Vector3D(0, 0, 1);
            }
            else
            {
                CameraHelper.AnimateTo(camera, position, direction, new Vector3D(0, 0, 1), animationSeconds * 1000);
            }
        }

        private void BuildScene()
        {
            var floor = new RectangleVisual3D { Origin = new Point3D(0, 0, 0), Width = 14, Length = 14, Fill = new SolidColorBrush(Color.FromRgb(0x4A, 0x50, 0x58)) };
            floor.Material = Floor;
            _viewport.Children.Add(floor);
            _viewport.Children.Add(new GridLinesVisual3D { Width = 14, Length = 14, MinorDistance = 0.5, MajorDistance = 2, Thickness = 0.01, Fill = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)), Center = new Point3D(0, 0, 0.002) });

            // Machine table + XTS motor modules
            var path = new Point3DCollection();
            for (int i = 0; i <= 240; i++)
            {
                var (x, depth, _, _, _, _) = PathAt(i * 1.5);
                path.Add(new Point3D(x, -depth, 0.86));
            }

            _viewport.Children.Add(new TubeVisual3D { Path = path, Diameter = 0.09, ThetaDiv = 12, Material = Anodized, IsPathClosed = true });
            _viewport.Children.Add(new BoxVisual3D { Center = new Point3D(0, 0, 0.39), Length = Straight + 0.9, Width = 1.0, Height = 0.78, Material = Mat(Color.FromRgb(0x2D, 0x32, 0x38), 10) });

            for (int i = 0; i < 10; i++)
            {
                var mover = new ModelVisual3D();
                mover.Children.Add(Box(new Point3D(0, 0, 0.87), 0.12, 0.03, 0.11, Aluminium));
                mover.Children.Add(Box(new Point3D(0, 0.05, TrackTop - 0.006), 0.17, 0.15, 0.012, Aluminium));
                var part = new ModelVisual3D();
                var partModel = new GeometryModel3D(MeshBox(0.2, 0.11, 0.085), CellBlue);
                part.Content = new Model3DGroup { Children = { partModel }, Transform = new TranslateTransform3D(0, 0.05, TrackTop + 0.0425) };
                mover.Children.Add(part);
                _viewport.Children.Add(mover);
                _movers.Add((mover, part, partModel));
            }

            for (int i = 0; i < 4; i++)
            {
                var cell = new CellVisual(XTSSimulationEngine.MachineLoadAngles[i]);
                _viewport.Children.Add(cell.Root);
                _cells.Add(cell);
            }
        }

        /// <summary>Same stadium mapping as the engine/2D canvas: returns (x, depth, tangent, outward normal).</summary>
        internal static (double X, double Depth, double Tx, double Tdepth, double Nx, double Ndepth) PathAt(double degrees)
        {
            double d = ((degrees % 360) + 360) % 360;
            double s = d / 360 * TrackLength;
            double L = Straight, R = Radius, A = Math.PI * R;
            if (s < L) return (-L / 2 + s, -R, 1, 0, 0, -1);
            if (s < L + A)
            {
                double t = (s - L) / R;
                return (L / 2 + R * Math.Sin(t), -R * Math.Cos(t), Math.Cos(t), Math.Sin(t), Math.Sin(t), -Math.Cos(t));
            }

            if (s < 2 * L + A) return (L / 2 - (s - L - A), R, -1, 0, 0, 1);
            {
                double t = (s - 2 * L - A) / R;
                return (-L / 2 - R * Math.Sin(t), R * Math.Cos(t), -Math.Cos(t), -Math.Sin(t), -Math.Sin(t), Math.Cos(t));
            }
        }

        private static ModelVisual3D Box(Point3D center, double lx, double ly, double lz, Material material)
        {
            var model = new GeometryModel3D(MeshBox(lx, ly, lz), material) { Transform = new TranslateTransform3D(center.X, center.Y, center.Z) };
            return new ModelVisual3D { Content = model };
        }

        private static MeshGeometry3D MeshBox(double lx, double ly, double lz)
        {
            var builder = new MeshBuilder();
            builder.AddBox(new Point3D(0, 0, 0), lx, ly, lz);
            return builder.ToMesh(true);
        }

        private static Material Mat(Color color, double specularPower)
        {
            var group = new MaterialGroup();
            group.Children.Add(new DiffuseMaterial(new SolidColorBrush(color)));
            group.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)), specularPower));
            group.Freeze();
            return group;
        }

        private sealed class CellVisual
        {
            private readonly GeometryModel3D _cabinet;
            private readonly GeometryModel3D _andon;
            private readonly PipeVisual3D _arm;
            private readonly ModelVisual3D _heldPart;
            private Point3D _lastTool;
            private readonly BillboardTextVisual3D _label;
            private readonly Point3D _robotBase;
            private readonly Vector3D _toTrack;
            private readonly Vector3D _toDial;
            private string _lastLabel = string.Empty;

            public CellVisual(double angle)
            {
                Root = new ModelVisual3D();
                var (x, depth, tx, tdepth, nx, ndepth) = PathAt(angle);
                var center = new Point3D(x + nx * CellOffset, -(depth + ndepth * CellOffset), 0);
                double yaw = Math.Atan2(-tdepth, tx) * 180 / Math.PI;

                var group = new Model3DGroup();
                _cabinet = new GeometryModel3D(MeshBox(1.0, 0.9, 0.85), Cabinet) { Transform = new TranslateTransform3D(0, 0, 0.425) };
                group.Children.Add(_cabinet);
                group.Children.Add(new GeometryModel3D(MeshCylinder(0.3, 0.04), Aluminium) { Transform = new TranslateTransform3D(0, 0, 0.88) });
                _andon = new GeometryModel3D(MeshCylinder(0.035, 0.22), GoodGreen) { Transform = new TranslateTransform3D(0.44, 0.4, 2.2) };
                group.Children.Add(_andon);
                foreach (var (px, py) in new[] { (-0.5, -0.45), (0.5, -0.45), (-0.5, 0.45), (0.5, 0.45) })
                {
                    group.Children.Add(new GeometryModel3D(MeshBox(0.04, 0.04, 1.2), Aluminium) { Transform = new TranslateTransform3D(px, py, 1.5) });
                }

                var cellTransform = new Transform3DGroup();
                cellTransform.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 0, 1), -yaw)));
                cellTransform.Children.Add(new TranslateTransform3D(center.X, center.Y, 0));
                group.Transform = cellTransform;
                Root.Content = group;

                _toTrack = new Vector3D(-nx, ndepth, 0);
                _toDial = new Vector3D(nx, -ndepth, 0);
                _robotBase = new Point3D(x + nx * 0.46 + tx * 0.34, -(depth + ndepth * 0.46 + tdepth * 0.34), 0);
                var pedestal = new PipeVisual3D { Point1 = _robotBase, Point2 = _robotBase + new Vector3D(0, 0, 0.7), Diameter = 0.24, Material = Anodized };
                Root.Children.Add(pedestal);
                var shoulder = _robotBase + new Vector3D(0, 0, 0.86);
                _arm = new PipeVisual3D { Point1 = shoulder, Point2 = shoulder + new Vector3D(0, 0, 0.15), Diameter = 0.09, Material = RobotOrange };
                Root.Children.Add(_arm);
                _heldPart = Box(new Point3D(0, 0, 0), 0.2, 0.11, 0.085, CellBlue);
                _heldPart.Transform = new ScaleTransform3D(0, 0, 0);
                Root.Children.Add(_heldPart);

                _label = new BillboardTextVisual3D
                {
                    Position = center + new Vector3D(0, 0, 2.6),
                    Foreground = Brushes.White,
                    Background = new SolidColorBrush(Color.FromArgb(170, 8, 12, 18)),
                    FontSize = 12,
                    Padding = new Thickness(5, 2, 5, 2)
                };
                Root.Children.Add(_label);
            }

            public ModelVisual3D Root { get; }

            public void Update(Machine machine, MachineIntelligenceState? ai, Robot? robot)
            {
                bool maint = machine.Maintenance != MaintenanceMode.None;
                bool breakdown = maint && machine.MaintenanceKind == MaintenanceKind.Breakdown;
                var activity = ai?.Activity ?? MachineActivity.Stopped;
                _andon.Material = breakdown ? BadRed
                    : maint ? Mat(Color.FromRgb(0x2F, 0x7B, 0xFF), 10)
                    : activity == MachineActivity.Running ? GoodGreen
                    : Mat(Color.FromRgb(0xF5, 0x9E, 0x0B), 10);

                // Robot arm points at the mover dock, the dial or home depending on its state
                var reach = robot?.State switch
                {
                    RobotState.PickingFromMover or RobotState.PlacingOnMover or RobotState.MovingToMover => _toTrack * 0.45,
                    RobotState.PlacingInMachine or RobotState.PickingFromMachine or RobotState.MovingToMachine => _toDial * 0.5,
                    _ => new Vector3D(0, 0, 0.15)
                };
                var shoulder = _robotBase + new Vector3D(0, 0, 0.86);
                var tool = shoulder + reach + new Vector3D(0, 0, 0.05);
                if (tool != _lastTool)
                {
                    _arm.Point2 = tool; // only regenerate the arm mesh when the pose actually changes
                    _lastTool = tool;
                }

                var held = tool - new Vector3D(0, 0, 0.1);
                _heldPart.Transform = robot?.HeldPart != null
                    ? new TranslateTransform3D(held.X, held.Y, held.Z)
                    : new ScaleTransform3D(0, 0, 0);

                string label = $"{machine.Name}  {(ai?.HealthEstimate ?? 1):P0}{(ai?.AnomalyFlag == true ? "  ⚠ ANOMALY" : string.Empty)}{(maint ? (breakdown ? "  ✖ BREAKDOWN" : "  🔧 PM") : string.Empty)}{(ai?.IsBottleneck == true ? "  ⚑ constraint" : string.Empty)}";
                if (label != _lastLabel)
                {
                    _label.Text = label;
                    _lastLabel = label;
                }
            }

            private static MeshGeometry3D MeshCylinder(double radius, double height)
            {
                var builder = new MeshBuilder();
                builder.AddCylinder(new Point3D(0, 0, -height / 2), new Point3D(0, 0, height / 2), radius * 2, 32);
                return builder.ToMesh(true);
            }
        }
    }
}
