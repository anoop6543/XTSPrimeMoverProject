# XTS Prime Mover Simulation - Architecture

## 1. Purpose
This document describes the current architecture of the XTS prime mover simulation after recent upgrades (tracking, machine throughput visibility, and SQLite persistence).

---

## 2. High-Level Design

The solution follows a **service-driven simulation + MVVM visualization** architecture.

- **Simulation Layer (independent of WPF rendering)**
  - Owns timing, routing, machine logic, robot transfers, station progression, defects, and lifecycle state.
- **Persistence Layer**
  - Captures recipes, events, results, snapshots, alarms, and errors in SQLite.
- **Presentation Layer (WPF + ViewModels)**
  - Displays state from ViewModels only; does not drive core manufacturing logic.

```text
MainWindow (View)
   ↕ data binding
ViewModels (Main/Mover/Machine/Station/Robot)
   ↕ projection of model/service state
Services (XTSSimulationEngine, SimulationDataLogger)
   ↕ domain operations + persistence
Models (Part, Mover, Machine, Station, Robot)
```

---

## 3. Core Components

## 3.1 Models
### `Part`
Tracks lifecycle and traceability:
- `PartId`, `TrackingNumber`
- `Status` (BaseLayer/InProcess/Assembled/Tested/Good/Bad)
- `EnteredPrimeMoverAt`, `ExitedPrimeMoverAt`
- `NextMachineIndex`
- `CompletedStations`
- `CurrentLocation`
- process history entries

### `Mover`
Represents one XTS shuttle:
- angular track position
- movement state
- loaded part reference
- target station context

### `Machine`
Represents a machine cell with station chain:
- machine metadata and load angle
- internal stations (4–5 per machine)
- throughput counters (`PartsEnteredCount`, `PartsExitedCount`)
- station advancement + completed-part readiness

### `Station`
Represents a machine process step:
- station type, process time, defect rate
- processing state and elapsed time
- increments part completion and logs process step on completion

### `Robot`
Represents machine entry/exit transfer robot:
- transfer state machine
- held part
- timed action progression

---

## 3.2 Services

### `XTSSimulationEngine`
Primary process orchestrator.
Responsibilities:
- periodic update loop (`DispatcherTimer`)
- mover position/state updates
- robot step execution
- machine station advancement
- deterministic routing (M0→M1→M2→M3→Exit)
- prime mover entry/exit accounting
- part status transitions and final quality outcome
- event snapshot cadence
- error capture and controlled stop

Notable exposed metrics:
- `PrimeMoverEnteredCount`
- `PrimeMoverExitedCount`
- `GoodPartsCount`, `BadPartsCount`, `TotalPartsProduced`
- `TotalStationCount`
- `DatabasePath`

### `SimulationDataLogger`
SQLite adapter for structured local data capture.
Responsibilities:
- database/file initialization
- table creation
- recipe seeding
- part/machine/result/event persistence
- snapshots, alarms, and errors

Database tables:
- `Recipes`
- `Parts`
- `PartEvents`
- `MachineRuns`
- `Results`
- `ProductionSnapshots`
- `ErrorLogs`
- `Alarms`

---

## 3.3 ViewModels

### `MainViewModel`
Application composition root for the UI:
- wires commands (`Start`, `Stop`, `Reset`)
- tracks aggregate counters/yield
- exposes DB path and global status
- propagates tick updates to child viewmodels

### `MoverViewModel`
Operator-friendly mover tracking:
- tracking number and short tracking
- next machine
- current location
- completion percentage

### `MachineViewModel`
Machine HMI projection:
- current station
- current part tracking
- progress
- throughput in/out
- station viewmodel collection

### `StationViewModel`
Station-level display:
- station task/type/status
- station part tracking
- active-index indication

### `RobotViewModel`
Robot transfer visibility:
- transfer state
- held part tracking
- progress

---

## 4. Runtime Flow

1. Engine creates part and assigns mover.
2. Part routed to target machine via `NextMachineIndex`.
3. Robot unloads from mover and loads machine station 0.
4. Machine stations process sequentially.
5. Robot unloads completed machine part and reloads mover.
6. Final machine sets Good/Bad.
7. Part exits at designated prime mover exit angle.
8. Engine updates counters and logs final result.

---

## 5. Separation of Concerns Rules

- **Do not move process logic into XAML/UI code-behind.**
- XAML should remain display/animation/data-binding only.
- Engine and models must remain testable without WPF visual tree.
- Persistence concerns stay in `SimulationDataLogger` (or repository/service abstractions under `Services`).

---

## 6. Known Next Steps (Planned)

1. Part History Inspector tab:
   - query by tracking number
   - timeline view from `PartEvents`
   - machine/station/result summary
2. CSV export:
   - export selectable tables
   - optional date range and filters
   - output path selection
