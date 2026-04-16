# Beckhoff XTS Prime Mover Manufacturing Simulation

## Overview
This project simulates a Beckhoff XTS-based manufacturing line using WPF (.NET 10) with:
- 10 XTS movers (prime mover track)
- 4 machines (each with 4–5 stations)
- 4 load/unload robots
- End-to-end part lifecycle tracking (entry to final good/bad exit)
- Local SQLite event/result logging

The application now separates machine/line operation logic from visualization so the process engine is independent of WPF rendering.

---

## Current Functional Scope

### Production Flow (Implemented)
1. New part enters prime mover on an available mover with tracking number (`TRK-xxxxx`).
2. Part routes through machines in order M0 → M1 → M2 → M3.
3. Each machine processes part through internal stations.
4. Robot transfers part back to mover.
5. Final machine sets final quality state (`Good` / `Bad`).
6. Part exits at prime mover exit zone and production counters update.

### Machine and Station Definitions (Implemented)
- **M0: Laser Welder** (4 stations)
- **M1: Precision Assembly** (5 stations)
- **M2: Quality Inspection** (4 stations)
- **M3: Functional Testing** (5 stations)

Total configured stations across the line: **18**.

### Quality / Defect Logic (Implemented)
- Each station has defect probability.
- Defect flags accumulate through process.
- Final result mapped to Good/Bad and logged.

---

## Visualization and HMI (Implemented)

### Main Track View
- Circular prime mover visualization
- Color-coded mover node states
- Inline mover ID + short tracking display
- Tooltip with full part tracking number

### Operator Information Panel
- Prime mover entered/exited counters
- Good/Bad counters and yield
- Detailed mover part status:
  - Tracking number
  - Part status
  - Next machine target
  - Current location
  - Completion progress

### Machine Mini-HMI
For each machine:
- Current station index/task
- Current part tracking
- Entered/Exited throughput counters
- Rotary station visualization with animation
- Station list with live status and part ID

### Robot Transfer Panel
- Robot state
- Assigned machine
- Currently held part tracking ID

---

## Data Logging (SQLite) (Implemented)
A local SQLite database is created automatically:
- Default file: `XTSFactorySim.db` (application output folder)

### Tables
- `Recipes`
- `Parts`
- `PartEvents`
- `MachineRuns`
- `Results`
- `ProductionSnapshots`
- `ErrorLogs`
- `Alarms`

### Logged Data
- Recipe/station seed data
- Part creation and prime mover entry/exit
- Machine entry/exit per part
- Station/process events and timeline
- Good/Bad final results
- Periodic production snapshots
- Runtime errors and alarms

---

## Architecture Summary

- **Models/**: domain objects (Part, Mover, Station, Machine, Robot)
- **Services/**:
  - `XTSSimulationEngine` — process orchestration and routing
  - `SimulationDataLogger` — SQLite persistence and event logging
- **ViewModels/**: UI-facing projections and calculated display values
- **MainWindow.xaml**: visualization-only concerns and data binding

The process simulation is service-driven and not coupled to WPF visual state transitions.

For more detail, see `docs/ARCHITECTURE.md`.

---

## Build / Run
- Target framework: `net10.0-windows`
- Start app from Visual Studio (F5)
- Controls: **START / STOP / RESET**

---

## Next Planned Enhancements
1. **Part History Inspector tab** (query/search timeline by tracking ID)
2. **CSV export** from SQLite tables (selectable tables/date range)
3. Additional operational analytics dashboards

---

## Tech Stack
- C#
- WPF
- .NET 10
- MVVM
- Microsoft.Data.Sqlite
