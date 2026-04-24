# XTS Prime Mover Simulation - Architecture

## 1. Purpose
This document describes the current architecture of the Beckhoff-style XTS simulation after iterative upgrades to:
- PLC-style machine control
- mover queue behavior
- watchdog self-recovery
- operator diagnostics/HMI explainability
- SQLite traceability

---

## 2. High-Level Design

The solution follows **service-driven simulation + MVVM visualization** and is being modernized toward **split runtime boundaries**.

```text
Current (in-process)
MainWindow (View / visualization)
   ↕ binding
ViewModels (Main, Mover, Machine, Station, Robot)
   ↕ projection only
Services (XTSSimulationEngine, SimulationDataLogger, PLC/Motion FBs)
   ↕ orchestration + persistence
Models (Part, Mover, Robot, Machine, Station)

Target (separated runtimes)
Beckhoff/TwinCAT machine runtime  <-->  HMI runtime  <-->  Data service runtime
```

Core rule: process logic remains in Services/Models, not XAML.
Service boundaries are now first-class so transport can move from local to remote without UI rewrite.

---

## 3. Layers

## 3.1 Domain Models
- `Part`: lifecycle, tracking, status, history, routing (`NextMachineIndex`)
- `Mover`: carrier position/state/target station/loaded part
- `Robot`: transfer state machine with timed action progression
- `Machine`: station chain + sequencer-related status fields
- `Station`: processing state, ET/PT timing, defect probability

## 3.2 Service Layer

### `XTSSimulationEngine`
Owns runtime orchestration:
- timer-driven updates
- mover state transitions + queue spacing
- robot transfers (mover↔machine)
- machine cycle execution via FBs
- part loading/unloading/exit accounting
- watchdog detection + controlled recovery
- execution logging events
- zone activity blink triggers (entry/exit)
- simulation speed scaling

### PLC/Motion Function Blocks
- `Services/TwinCATMotionFunctionBlocks.cs`
  - `McPowerFb`, `McMoveVelocityFb`, `McHaltFb`, `FbXtsMoverAxis`
- `Services/TwinCATPlcFunctionBlocks.cs`
  - `TonFb`, `AlarmLatchFb`, `FbMachineCycle`

Machine sequencer states:
- `Init`, `Ready`, `Run`, `Fault`, `Reset`

### Watchdog behavior
Root-cause codes and counters are tracked and exposed:
- machine stall codes
- robot stall codes
- mover stall codes
- rehome/scrap events

### `SimulationDataLogger`
SQLite persistence for events/results/alarms/errors/snapshots.

## 3.3 Presentation Layer

### ViewModels
- `MainViewModel`: runtime composition root; commands; aggregate metrics; speed control; watchdog status feed; gateway mode status
- `MoverViewModel`: operator explanation fields (`FlowMeaning`, `WaitReason`, target info)
- `MachineViewModel`: action and ET/PT summaries plus assigned-robot transfer visualization for machine tabs
- `StationViewModel`: per-station ET/PT diagnostics and operation-centric task descriptors
- `RobotViewModel`: transfer state, direction, stage, progress, canvas coordinates, and transfer arrowhead points

### MainWindow.xaml
- Beckhoff-like oval track rendering (lane/seam aesthetics)
- machine mini-HMIs offset outward from track for readability
- robot canvas overlays with moving glyphs, animated dashed transfer lines, and direction arrowheads
- entry/load and exit/unload zones + blinkers
- right-side Line HMI diagnostics sections
- enhanced station cards on machine tabs with live task-centric visuals and activity glyph animations
- bottom execution logger with auto-scroll to latest event

---

## 4. Runtime Sequence (current)

1. Eligible empty mover reaches entry zone.
2. New part loads onto mover (subject to WIP/empty-carrier constraints).
3. Mover queues/travels to target machine load interface.
4. Robot transfers part into machine.
5. Machine station chain processes and indexes through steps.
6. Robot returns processed part to mover.
7. Route continues until M3 completion.
8. Final quality determines Good/Bad exit.
9. Exit zone discharge updates counters/results/logs.

---

## 5. Observability / Debug Capability

Implemented operator debug signals:
- mover explanation text for wait/flow reason
- machine action + ET/PT snapshot
- station ET/PT details
- execution logger stream
- watchdog status panel (count, last object, last trigger/message)
- entry/exit activity blink indicators

---

## 6. Error Handling Architecture

The application uses a centralized error handling mechanism provided by `Services/ErrorHandlingService.cs`.

### Layers

| Layer | Mechanism | Behavior |
|---|---|---|
| **Global (App.xaml.cs)** | `DispatcherUnhandledException`, `AppDomain.UnhandledException`, `TaskScheduler.UnobservedTaskException` | Catches all unhandled exceptions, logs to `crash.log`, reports to `ErrorHandlingService`, shows user-friendly dialog |
| **Engine (XTSSimulationEngine)** | Per-subsystem isolation via `RunSubsystem()` | Each tick subsystem (movers, robots, machines, watchdogs, etc.) runs in its own try/catch so a failure in one does not halt the others |
| **Database (SimulationDataLogger)** | Retry with exponential backoff via `ExecuteWithRetry()` | All DB operations retry on transient SQLite errors (BUSY/LOCKED); circuit breaker prevents repeated hammering of a failing DB |
| **Gateway (Local + Remote Mock)** | Error wrapping + fallback returns | Commands log errors and degrade gracefully; queries return empty collections instead of throwing |
| **ViewModel (MainViewModel)** | Try/catch on commands + event handlers | User-facing errors surface via status properties; internal errors report to the centralized service |
| **Window (MainWindow)** | Constructor try/catch | Initialization failures show a MessageBox and report to the error service |

### ErrorHandlingService Features

- **Singleton** (`ErrorHandlingService.Instance`) — single point of error aggregation
- **Error classification** — severity (Info/Warning/Error/Critical) and category (Database/Engine/Gateway/ViewModel/Unhandled/Configuration)
- **Retry with exponential backoff** — `ExecuteWithRetry` for both void and return-value operations
- **Circuit breaker** — per-operation breaker that opens after 5 consecutive failures, with 30-second cooldown
- **Throttling** — duplicate errors within a 2-second window are suppressed
- **Error log** — in-memory ring buffer of last 500 errors, queryable by category
- **Event** — `ErrorOccurred` event for UI notification
- **Transient detection** — `IOException`, `TimeoutException`, and SQLite BUSY/LOCKED are classified as retryable

### Crash Log

A file-based `crash.log` is written to the application base directory for any unhandled exception caught by the global handlers. This persists across sessions and serves as a last-resort diagnostic when the in-memory error log is unavailable.

---

## 7. Persistence Contract

Current tables:
- `Recipes`
- `Parts`
- `PartEvents`
- `MachineRuns`
- `Results`
- `ProductionSnapshots`
- `ErrorLogs`
- `Alarms`

Schema changes should be treated as explicit migrations.

---

## 8. Continuation Rules (multi-machine/dev handoff)

1. Read `README.md`, this file, then `AGENTS.md`.
2. Keep process logic in `Services/` and `Models/`.
3. Add ViewModel fields before changing XAML bindings.
4. For read-only display values, use `Mode=OneWay` in XAML.
5. Always validate with build before commit.
6. Keep logs and watchdog messages operator-readable.

---

## 9. Split-Runtime Modernization Direction

The modernization target is:
- **Machine logic on Beckhoff/TwinCAT side**
- **HMI runtime as a separate client**
- **Database layer as a separate service**

Current implementation status:
- Gateway contract split completed:
  - `IMachineGatewayService` for machine commands/telemetry
  - `IDataGatewayService` for data/history/export APIs
- `LocalSimulationServiceGateway` implements both interfaces.
- `RemoteTwinCatMachineGatewayMock` added for remote-boundary simulation (latency-aware).
- Runtime machine gateway toggle implemented through `App.xaml` resources.

Phase-1 keeps all behavior in one process but now uses explicit service boundaries and a swappable machine gateway implementation.

See detailed plan: `docs/SEPARATED-RUNTIME-PLAN.md`.
