# AGENTS.md - Copilot Working Context

This file is the persistent repo-level handoff context for future Copilot sessions.

## Project Identity
- Name: `XTSPrimeMoverProject`
- Type: WPF desktop simulation
- Framework: `.NET 10` (`net10.0-windows`)
- Language: C#
- Pattern: service-driven simulation + MVVM UI

## Current Runtime Capabilities
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
- Engine: `Services/XTSSimulationEngine.cs`
- DB logger: `Services/SimulationDataLogger.cs`
- Error handling: `Services/ErrorHandlingService.cs`
- Gateway contracts: `Services/HmiServiceContracts.cs`
- Local gateway: `Services/LocalSimulationServiceGateway.cs`
- Remote machine mock: `Services/RemoteTwinCatMock/RemoteTwinCatMachineGatewayMock.cs`
- PLC/Motion FBs:
  - `Services/TwinCATMotionFunctionBlocks.cs`
  - `Services/TwinCATPlcFunctionBlocks.cs`
- Models: `Models/*.cs`
- Main VM: `ViewModels/MainViewModel.cs`
- Main UI: `MainWindow.xaml`

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

## Working Assumptions
- Routing order remains: `M0 -> M1 -> M2 -> M3 -> Exit`
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
4. Build once before edits.
5. Follow change order:
   - Services/Models
   - ViewModels
   - XAML
6. Build again before commit.

## Copilot Continuity Note
Copilot chat/session history is environment-scoped. Persistent continuity comes from committed source + docs in this repo. Keep these docs updated with behavior changes.

## Current Priority Themes
- queue behavior clarity and deadlock avoidance
- machine transfer visibility and ET/PT diagnostics
- watchdog transparency (codes/counters/last object/message)
- maintain operator-grade HMI readability
- continue externalization path from local gateway to real TwinCAT/data service clients
