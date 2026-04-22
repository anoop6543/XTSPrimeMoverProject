# XTS Prime Mover Project

WPF + .NET 10 manufacturing simulation of a Beckhoff-style XTS line with movers, robots, machine stations, runtime diagnostics, and SQLite traceability.

## What this repository contains

- Desktop simulation app built with C# (`net10.0-windows`)
- Service-driven runtime engine with MVVM HMI
- Beckhoff/TwinCAT-inspired motion and PLC function block modeling
- Gateway interfaces for local and remote machine/data service boundaries
- Operational logging and alarm/traceability persistence to SQLite

## Core capabilities

- 10 movers on an oval XTS track
- 4 machines and 4 robots with transfer orchestration
- 18 total machine stations
- Deterministic product route: `M0 -> M1 -> M2 -> M3 -> Exit`
- ET/PT timeout supervision and watchdog fault recovery
- Operator-oriented diagnostics for mover/machine/robot state
- Runtime execution logger with live event feed

## Architecture at a glance

The app follows **service-driven simulation + MVVM UI**:

- `Services/` — simulation engine, gateways, PLC/motion function blocks, DB logger
- `Models/` — domain entities (`Part`, `Mover`, `Machine`, `Robot`, `Station`)
- `ViewModels/` — HMI projection and command orchestration
- `MainWindow.xaml` — primary operator UI

See [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) for full details.

## Data logging

SQLite tables currently used:

- `Recipes`
- `Parts`
- `PartEvents`
- `MachineRuns`
- `Results`
- `ProductionSnapshots`
- `ErrorLogs`
- `Alarms`

## Getting started

1. Clone this repository.
2. Open `XTSPrimeMoverProject.slnx` in Visual Studio (Windows).
3. Restore/build the solution.
4. Run the app and use `START`, `STOP`, `RESET`, and speed control from the header panel.

## Key files

- Simulation engine: `Services/XTSSimulationEngine.cs`
- Data logger: `Services/SimulationDataLogger.cs`
- Gateway contracts: `Services/HmiServiceContracts.cs`
- Local gateway: `Services/LocalSimulationServiceGateway.cs`
- Remote machine mock: `Services/RemoteTwinCatMock/RemoteTwinCatMachineGatewayMock.cs`
- Main view model: `ViewModels/MainViewModel.cs`
- Main UI: `MainWindow.xaml`
