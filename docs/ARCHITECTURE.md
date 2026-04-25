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

## 6. Threading Architecture

The application separates concerns across dedicated threads to keep the UI responsive and prevent I/O from blocking simulation computation.

### Thread Topology

| Thread | Responsibility | Mechanism |
|---|---|---|
| **UI thread** | WPF rendering, data binding, command handlers, `ObservableCollection` updates | WPF Dispatcher |
| **Simulation thread** | Engine tick computation (movers, robots, machines, watchdogs) | `System.Threading.Timer` callback via thread pool |
| **DB-WriteQueue thread** | All database INSERT/UPDATE operations | Dedicated `Thread` consuming a `BlockingCollection<Action>` |
| **Thread pool (ad-hoc)** | CSV export, network latency simulation in remote mock | `Task.Run` |

### Synchronization

- **`_simulationLock`** (in `XTSSimulationEngine`) — held during the entire `Update(deltaTime)` call, and also during `Start()` / `Stop()` / `Reset()` to prevent concurrent mutation.
- **`Interlocked._tickActive`** — re-entrancy guard preventing overlapping timer callbacks.
- **`Dispatcher.Invoke`** — after each simulation tick, the engine synchronously marshals `StateChanged` to the UI thread, ensuring the UI reads consistent state while the engine is paused between ticks.
- **`Dispatcher.BeginInvoke`** — used for `LogGenerated` events and `OnEngineLogGenerated` in the ViewModel to avoid blocking the simulation thread on log delivery.
- **Value snapshots** — all `SimulationDataLogger` write methods capture scalar values from model objects *before* queuing, preventing the background writer from reading stale or torn state.

### Database Write Queue

`DatabaseWriteQueue` uses a producer-consumer pattern:
- Bounded capacity of 2048 pending writes to provide back-pressure
- Single dedicated background thread (`ThreadPriority.BelowNormal`)
- Graceful shutdown via `CompleteAdding()` + `Join(5s)` timeout
- Write failures are logged but do not crash the consumer thread

### Remote Gateway Latency

`RemoteTwinCatMachineGatewayMock` simulates network latency using `Task.Delay` instead of `Thread.Sleep`, dispatching commands to the thread pool so the UI thread is never blocked. Query methods that return values remain synchronous since they are fast in-memory reads.

### Shutdown

- `MainWindow.Closed` event calls `XTSSimulationEngine.Dispose()` which stops the timer and drains the DB write queue.
- `App.OnStartup` registers global exception handlers for `DispatcherUnhandledException`, `AppDomain.UnhandledException`, and `TaskScheduler.UnobservedTaskException` with crash log persistence.

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
