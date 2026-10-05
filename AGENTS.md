# AGENTS.md - Copilot Working Context

This file is the persistent repo-level handoff context for future Copilot sessions.

## Project Identity
- Name: `XTSPrimeMoverProject`
- Type: EV battery module line simulation + AI digital twin (WPF HMI + cross-platform Core)
- Framework: `.NET 10` (`Core`: `net10.0`, HMI: `net10.0-windows`)
- Language: C# (+ JavaScript for the Three.js twin in `Web/twin`)
- Pattern: service-driven simulation + MVVM UI; Core has no WPF dependency

## Current Runtime Capabilities
- 12S prismatic EV battery module process (M0 stack + busbar laser weld, M1 CMU assembly + fastening, M2 3D vision/gauging, M3 EOL test + DMC)
- Fixed 20 ms PLC cycle, jerk-limited motion, no-overtaking gap control, precise docking, entry/exit docks
- Dual-gripper robot cells with outfeed nests (constraint-first loading, prefetch/staging, swap)
- Measurement-driven quality (spec limits, Cpk) instead of random defects
- AI layer: digital twins, residual anomaly detection, RUL, SPC, OEE, bottleneck, energy, autopilot (critical WIP + PdM), copilot (offline + optional Claude)
- What-if fault injection, maintenance lifecycle with breakdowns
- Three.js 3D digital twin (WebView2, live) + native HelixToolkit 3D + recorded MP4 tours (`docs/videos`)
- Beckhoff-style oval XTS visualization with entry/exit zones
- 10 movers, 4 machines, 4 robots
- 18 machine stations total
- PLC-style machine sequencing and TON timeout supervision
- Watchdog detection + controlled recovery/escalation
- SQLite traceability and alarms
- Operator-focused HMI with mover/machine/robot diagnostics
- Robot movement overlays on main canvas (moving glyphs + dashed transfer lines + direction arrowheads)
- Machine mini-HMIs pulled away from track for clarity
- Machine tab station cards enhanced with task-level visuals + tiny animated processing glyphs
- Gateway mode indicator in HMI (Local vs Remote TwinCAT mock)
- Auto-scrolling execution logger panel and speed control slider

## Core Runtime Files
- Engine: `Core/Services/XTSSimulationEngine.cs`
- Dispatcher abstraction / options: `Core/Services/SimulationDispatch.cs` (WPF adapter: `Infrastructure/WpfSimulationDispatcher.cs`)
- AI layer: `Core/Services/Intelligence/*.cs` (entry point `LineIntelligenceHub.cs`)
- 3D frame contract: `Core/Services/DigitalTwin3D/TwinFrame.cs`
- DB logger: `Core/Services/SimulationDataLogger.cs`
- Error handling: `Core/Services/ErrorHandlingService.cs`
- Gateway contracts: `Core/Services/HmiServiceContracts.cs`
- Local gateway: `Core/Services/LocalSimulationServiceGateway.cs`
- Remote machine mock: `Core/Services/RemoteTwinCatMock/RemoteTwinCatMachineGatewayMock.cs`
- PLC/Motion FBs:
  - `Core/Services/TwinCATMotionFunctionBlocks.cs`
  - `Core/Services/TwinCATPlcFunctionBlocks.cs`
- Models: `Core/Models/*.cs`
- Main VM: `ViewModels/MainViewModel.cs`; AI VM: `ViewModels/IntelligenceViewModel.cs`
- Main UI: `MainWindow.xaml`; 3D hosts: `Infrastructure/DigitalTwinWebHost.cs`, `Controls/NativeLineView3D.cs`
- Three.js twin: `Web/twin/` · Tests: `tests/` · Recorder/video: `tools/`

## DB Contract (current)
Tables expected:
- `Recipes`
- `Parts`
- `PartEvents`
- `MachineRuns`
- `Results`
- `ProductionSnapshots`
- `ErrorLogs`
- `Alarms`

Keep schema stable unless a migration is intentionally added.

## UI/Binding Safety Rules
- Prefer `Mode=OneWay` for read-only display bindings.
- Treat `Run.Text` bindings as explicit `Mode=OneWay` unless edit-input is needed.
- Keep process logic out of XAML/code-behind.

## Threading Model
- Engine runs on a **background thread** (`System.Threading.Timer`), integrating fixed 20 ms cycles.
- Engine notifications go through `ISimulationDispatcher`; never construct the engine in the HMI without the WPF dispatcher.
- `IntelligenceSnapshot` is immutable; read it lock-free from the UI.
- `StateChanged` is marshaled to the UI thread via `Dispatcher.Invoke` after each tick.
- `LogGenerated` uses `Dispatcher.BeginInvoke` to avoid blocking the simulation thread.
- All DB writes go through `DatabaseWriteQueue` (dedicated background consumer thread).
- CSV exports run on the thread pool via `Task.Run`.
- `RemoteTwinCatMachineGatewayMock` uses `Task.Delay` (not `Thread.Sleep`) for latency simulation.
- `MainWindow.Closed` disposes the engine, which stops the timer and drains the DB queue.
- See `docs/ARCHITECTURE.md` § Threading Architecture for full details.

## Working Assumptions
- Routing order remains: `M0 -> M1 -> M2 -> M3 -> Exit` (orchestration editor can change it)
- Movers must never overtake (tests assert the 12° pitch); keep the cell swap logic deadlock-free.
- The AI may only read sensors/measurements; true twin damage is for validation display only.
- Verify engine changes headless: `dotnet test tests/XTSPrimeMoverProject.Tests`.
- `Part.NextMachineIndex` is the machine targeting source of truth.
- Engine logs are operational diagnostics, not only dev traces.
- Machine/data boundary remains interface-driven (`IMachineGatewayService`, `IDataGatewayService`) even in local mode.

## Handoff / Continue on Another Laptop
1. Clone repo from GitHub.
2. Open in Visual Studio with same GitHub/Copilot account.
3. Read in order:
   1) `README.md`
   2) `docs/ARCHITECTURE.md`
   3) `AGENTS.md`
4. Build once before edits (`dotnet build XTSPrimeMoverProject.slnx`; on Linux add `-p:EnableWindowsTargeting=true`).
5. Follow change order:
   - Services/Models
   - ViewModels
   - XAML
6. Build again before commit.

## Copilot Continuity Note
Copilot chat/session history is environment-scoped. Persistent continuity comes from committed source + docs in this repo. Keep these docs updated with behavior changes.

## Current Priority Themes
- keep the AI measurable: benchmark autopilot vs manual headless before claiming improvements
- queue behavior clarity and deadlock avoidance
- machine transfer visibility and ET/PT diagnostics
- watchdog transparency (codes/counters/last object/message)
- maintain operator-grade HMI readability
- continue externalization path from local gateway to real TwinCAT/data service clients
