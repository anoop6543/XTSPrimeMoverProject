using System.Globalization;
using XTSPrimeMoverProject.Services;
using XTSPrimeMoverProject.Services.DigitalTwin3D;
using XTSPrimeMoverProject.Services.Intelligence;

// Usage:
//   dotnet run --project tools/TwinRecorder -- --out frames.json [--seconds 40] [--fps 30] [--speed 1]
//       [--warmup 120] [--seed 42] [--autopilot] [--fault laser|spindle|vision|fixture|gripper] [--fault-at 5]
var options = ParseArgs(args);
string output = options.GetValueOrDefault("out", "twin-frames.json");
double seconds = Double(options, "seconds", 40);
double fps = Double(options, "fps", 30);
double speed = Double(options, "speed", 1);
double warmup = Double(options, "warmup", 120);
double faultAt = Double(options, "fault-at", 5);
int seed = (int)Double(options, "seed", 42);
bool autopilot = options.ContainsKey("autopilot");
FaultScenario? fault = options.GetValueOrDefault("fault") switch
{
    "laser" => FaultScenario.LaserOpticsContamination,
    "spindle" => FaultScenario.SpindleBearingDefect,
    "vision" => FaultScenario.VisionLightingDrift,
    "fixture" => FaultScenario.FixtureContactWear,
    "gripper" => FaultScenario.RobotGripperLeak,
    _ => null
};

string db = Path.Combine(Path.GetTempPath(), $"xts-recorder-{Guid.NewGuid():N}.db");
using var engine = new XTSSimulationEngine(InlineSimulationDispatcher.Instance, new SimulationOptions { Seed = seed, DatabasePath = db, AutopilotEnabled = autopilot });
var gateway = new LocalSimulationServiceGateway(engine);

Console.WriteLine($"Warm-up {warmup:F0} s (seed {seed}, autopilot {autopilot})...");
engine.AdvanceManually(warmup);

int frameCount = (int)Math.Round(seconds * fps);
double simPerFrame = speed / fps;
var frames = new List<TwinFrame>(frameCount);
bool injected = false;
for (int i = 0; i < frameCount; i++)
{
    double videoTime = i / fps;
    if (fault.HasValue && !injected && videoTime >= faultAt)
    {
        Console.WriteLine(gateway.InjectFault(fault.Value));
        injected = true;
    }

    engine.AdvanceManually(simPerFrame);
    frames.Add(TwinFrameBuilder.Build(gateway));
}

File.WriteAllText(output, TwinFrameBuilder.ToJson(frames));
var s = engine.GetIntelligenceSnapshot();
Console.WriteLine($"Wrote {frames.Count} frames to {output}. Good {engine.GoodPartsCount}, bad {engine.BadPartsCount}, OEE {s.Line.Oee:P0}, {s.Line.ThroughputPerMinute:F2}/min.");
foreach (var d in s.Decisions.TakeLast(5))
{
    Console.WriteLine($"  [{d.Category}] {d.Action}");
}

static Dictionary<string, string> ParseArgs(string[] args)
{
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (int i = 0; i < args.Length; i++)
    {
        if (!args[i].StartsWith("--", StringComparison.Ordinal)) continue;
        string key = args[i][2..];
        string value = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal) ? args[++i] : "true";
        result[key] = value;
    }

    return result;
}

static double Double(Dictionary<string, string> o, string key, double fallback) =>
    o.TryGetValue(key, out var v) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : fallback;
