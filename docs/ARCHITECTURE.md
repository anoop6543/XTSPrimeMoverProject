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

The solution follows **service-driven simulation + MVVM visualization**.

```text
MainWindow (View / visualization)
   ↕ binding
ViewModels (Main, Mover, Machine, Station, Robot)
   ↕ projection only
Services (XTSSimulationEngine, SimulationDataLogger, PLC/Motion FBs)
   ↕ orchestration + persistence
Models (Part, Mover, Robot, Machine, Station)
```

Core rule: process logic remains in Services/Models, not XAML.

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
- `MainViewModel`: runtime composition root; commands; aggregate metrics; speed control; watchdog status feed
- `MoverViewModel`: operator explanation fields (`FlowMeaning`, `WaitReason`, target info)
- `MachineViewModel`: action and ET/PT summaries
- `StationViewModel`: per-station ET/PT diagnostics
- `RobotViewModel`: transfer state and progress

### MainWindow.xaml
- Beckhoff-like oval track rendering (lane/seam aesthetics)
- machine mini-HMIs near track
- entry/load and exit/unload zones + blinkers
- right-side Line HMI diagnostics sections
- bottom execution logger

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

## 6. Persistence Contract

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

## 7. Continuation Rules (multi-machine/dev handoff)

1. Read `README.md`, this file, then `AGENTS.md`.
2. Keep process logic in `Services/` and `Models/`.
3. Add ViewModel fields before changing XAML bindings.
4. For read-only display values, use `Mode=OneWay` in XAML.
5. Always validate with build before commit.
6. Keep logs and watchdog messages operator-readable.

---

## 8. Next Practical Enhancements

- formal queue visualization (distance-to-next mover)
- optional step-through mode (single update tick advance)
- watchdog analytics trend tab
- automated regression tests for deadlock/stall scenarios
