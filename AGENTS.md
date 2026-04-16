# AGENTS.md - Copilot Working Context

This file captures project-specific guidance for future AI coding sessions.

## Project Identity
- Name: XTSPrimeMoverProject
- Type: WPF desktop simulation
- Framework: .NET 10 (`net10.0-windows`)
- Language: C#
- Architecture: Service-driven simulation + MVVM UI

## Primary Objective
Simulate a Beckhoff XTS prime mover manufacturing line with full traceability:
- 10 movers
- 4 machines with multi-station processing
- 4 robots for mover↔machine transfer
- part lifecycle from prime mover entry to good/bad exit

## Current Reality (Important)
1. **Machine operation logic is in services/models** (not in XAML).
2. **UI is visualization layer only** with bindings/animations.
3. **SQLite logging is enabled** via `SimulationDataLogger`.
4. Part tracking is done with `TrackingNumber` (`TRK-xxxxx`) and rich events.

## Key Files
- Core engine: `Services/XTSSimulationEngine.cs`
- Logging DB: `Services/SimulationDataLogger.cs`
- Domain models: `Models/*.cs`
- UI binding root: `ViewModels/MainViewModel.cs`
- Main visualization: `MainWindow.xaml`
- Architecture doc: `docs/ARCHITECTURE.md`
- Product overview: `README.md`

## Database Contract (SQLite)
Tables currently expected:
- `Recipes`
- `Parts`
- `PartEvents`
- `MachineRuns`
- `Results`
- `ProductionSnapshots`
- `ErrorLogs`
- `Alarms`

When extending logging/reporting, preserve this schema unless migration is intentional.

## UI/Binding Safety Rules
- Prefer `Mode=OneWay` for display bindings.
- For animations, use named transforms (`Storyboard.TargetName`) to avoid immutable-object animation errors.
- Escape XML special chars in XAML text (e.g., `&amp;`).

## Simulation Behavior Expectations
- Routing order: M0 → M1 → M2 → M3 → Exit.
- Use `Part.NextMachineIndex` for deterministic machine targeting.
- Keep counters consistent:
  - prime mover entered/exited
  - machine entered/exited
  - good/bad/total

## Upcoming Planned Work
1. Part History Inspector tab
   - search by tracking number
   - timeline from `PartEvents`
2. CSV export from SQLite tables
   - table selection + optional filters/date range

## Change Strategy for Future Agents
1. Read `README.md` and `docs/ARCHITECTURE.md` first.
2. Keep simulation changes in `Services/` and `Models/`.
3. Add/adjust ViewModel fields before touching XAML.
4. Validate with `run_build` after edits.
5. Avoid introducing UI-only logic that mutates machine process flow.
