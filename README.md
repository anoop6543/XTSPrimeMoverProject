# Beckhoff XTS Prime Mover Manufacturing Simulation

WPF + .NET 10 simulation of a Beckhoff-style XTS transport line with service-driven logic, MVVM visualization, SQLite traceability, watchdog recovery, and operator-focused HMI diagnostics.

## Current Implemented Scope

- .NET target: `net10.0-windows`
- UI: WPF (MVVM)
- Transport: 10 movers on oval/stadium XTS track visualization
- Process cells: 4 machines (Laser Welder, Assembler, Inspector, Tester)
- Transfers: 4 robots (mover ↔ machine)
- Station chains: 18 total stations
- Persistence: SQLite (`XTSFactorySim.db` in output path)
- Runtime split-ready gateway boundaries:
  - `IMachineGatewayService` (machine commands + telemetry)
  - `IDataGatewayService` (history/tables/export)
  - `LocalSimulationServiceGateway` implements both interfaces for local mode
  - `RemoteTwinCatMachineGatewayMock` simulates remote machine boundary with configurable latency
- Runtime gateway mode toggle via app resources in `App.xaml`
- PLC/TwinCAT-style FBs:
  - Motion FBs (`McPower`, `McMoveVelocity`, `McHalt`, `FbXtsMoverAxis`)
  - Process FBs (`TON`, alarm latch, machine cycle sequencer)

## Runtime Behavior (as implemented)

- Deterministic routing: `M0 -> M1 -> M2 -> M3 -> Exit`
- Machine sequencer states: `Init`, `Ready`, `Run`, `Fault`, `Reset`
- ET/PT timeout supervision per station (TON)
- Queue spacing on movers to reduce overlap/stacking
- Entry/Exit zone blink indicators tied to real load/unload events
- Watchdog supervision + controlled recovery:
  - machine stall
  - robot stall
  - mover stall
  - rehome/scrap fallback + root-cause alarm codes

## HMI / Operator Diagnostics

### Main visualization
- Beckhoff-like oval track with lane markings, seams/module look
- Entry / Load and Exit / Unload zones with live blinkers
- Machine mini-HMIs on main canvas with outward spacing for readability
- Robot transfer overlays on canvas:
  - moving robot glyphs between mover dock and machine dock points
  - animated dashed transfer lines with glow
  - direction arrowheads at active destination end

### Visualization Screens
> Indexed in navigation order for first-time visitors.

#### 1) Main line visualization overview
![Main Visualization Screen](docs/screenshots/01-main-visualization.png)

#### 2) Production statistics section
![Production Statistics Section](docs/screenshots/ProductionStatistics_Section.png)

#### 3) Mover live explainer section
![Mover Live Explainer Section](docs/screenshots/MoverLiveExplainer_Screen.png)

#### 4) Machine runtime tracking section
![Machine Runtime Tracking Section](docs/screenshots/Machine_RunTime__Tracking_Section.png)

#### 5) Robot transfers (mover to machine)
![Robot Transfers Mover to Machine Section](docs/screenshots/RobotTransfers-MoverToMachineSection.png)

#### 6) Laser welder station view
![Laser Welder Station](docs/screenshots/LaserWelderStation.png)

#### 7) Assembly station view
![Assembly Station](docs/screenshots/AssemblyStation.png)

#### 8) Inspection station view
![Inspection Station](docs/screenshots/Inspection_Station.png)

#### 9) Testing station view
![Testing Station](docs/screenshots/Testing_Station.png)

#### 10) Orchestration editor screen
![Orchestration Editor Screen](docs/screenshots/OrchestationEditor_Screen.png)

#### 11) Part history inspection screen
![Part History Inspection](docs/screenshots/PartHistoryInspection.png)

#### 12) Database tables view screen
![Database Tables View Screen](docs/screenshots/DB_Tables_VIew_Screen.png)

#### 13) Runtime execution logger
![Execution Logger](docs/screenshots/03-execution-logger.png)

### HMI Screen Recording
- [Watch the HMI runtime recording (YouTube - Unlisted)](https://www.youtube.com/watch?v=cIq1iMvu8MY)

[![Watch HMI runtime recording](https://img.youtube.com/vi/cIq1iMvu8MY/hqdefault.jpg)](https://www.youtube.com/watch?v=cIq1iMvu8MY)

- Citation: YouTube (unlisted) project runtime demo — `https://www.youtube.com/watch?v=cIq1iMvu8MY`

### Line HMI (right panel)
- Production counters and yield
- Gateway mode indicator (Local vs Remote TwinCAT Mock)
- Color/flow legend (what yellow/BaseLayer means, etc.)
- Mover live explainer:
  - state, part, next target, wait reason, position
- Machine runtime tracking:
  - action, station, part, ET/PT, fault text
- Robot transfer status and progress:
  - lane direction, stage, transfer progress, active/idle badge
- Watchdog status table (code/count/last object/time/message)

### Execution Logger (bottom)
- Auto-scroll runtime log stream for movement, transfers, alarms, recoveries

## SQLite Logging

Tables currently used:
- `Recipes`
- `Parts`
- `PartEvents`
- `MachineRuns`
- `Results`
- `ProductionSnapshots`
- `ErrorLogs`
- `Alarms`

## Build / Run (Desktop WPF Simulation)

1. Open solution in Visual Studio 2026+.
2. Restore/build (`Debug | Any CPU`).
3. Run with `F5`.
4. Use header controls:
   - `START`
   - `STOP`
   - `RESET`
   - Speed slider (`0.1x` .. `5.0x`)

## Full-Stack Test Guide (Temporal + APIs + HMIs)

This verifies the distributed stack end-to-end: infrastructure, orchestration, machine services, APIs, HMIs, and observability.

### 1) Prerequisites

- Docker Desktop or Docker Engine with Compose v2
- At least 8 CPU cores / 16 GB RAM recommended
- Ports available: `3000-3004`, `5432`, `6379`, `7233`, `8080`, `8082`, `8088`, `8090-8097`, `9200`, `9999`, `16686`

### 2) Start the full stack

From repository root:

```bash
cd docker
docker compose -f docker-compose.dev.yml up -d --build
docker compose -f docker-compose.dev.yml ps
```

Expected: containers for Temporal, PostgreSQL, Redis, prime mover service/API/HMI, 4 machine service/API/HMI sets, workers, and observability are `Up`.

### 3) Smoke-check core endpoints

Open and confirm these load without errors:

- Prime mover HMI: `http://localhost:3000`
- Machine HMIs: `http://localhost:3001`, `3002`, `3003`, `3004`
- Prime mover API Swagger: `http://localhost:8082/swagger`
- Machine API Swagger examples: `http://localhost:8091/swagger`, `http://localhost:8093/swagger`
- Temporal UI: `http://localhost:8088`
- Prometheus: `http://localhost:9999`
- Grafana: `http://localhost:3100` (`admin / xts_grafana`)
- Jaeger: `http://localhost:16686`

### 4) Functional end-to-end validation

1. In Prime mover HMI, start production flow.
2. Verify parts enter, route machine-by-machine (`M0 -> M1 -> M2 -> M3 -> Exit`), and exit as Good/Bad.
3. Open each machine HMI and confirm live station progression + ET/PT style runtime changes.
4. In Temporal UI:
   - Confirm active workflow execution for prime mover orchestration.
   - Confirm per-part lifecycle workflow creation and completion.
   - Confirm machine workflow activity transitions while parts are processed.
5. In APIs (Swagger), call read/status endpoints and verify responses update as runtime state changes.
6. Validate alarms/watchdog behavior by observing fault and recovery events in HMIs/log streams (if a stall/fault is triggered).

### 5) Data and observability validation

- Verify metrics appear in Prometheus targets and queries.
- Verify Grafana connects to Prometheus and dashboards update over time.
- Verify traces/events are visible in Jaeger for workflow/service operations.
- Verify database-backed runtime records continue updating while production runs.

### 6) Pass/Fail checklist

Pass when all are true:

- All required containers stay healthy and do not crash-loop.
- Prime mover + all machine HMIs load and show live-changing runtime state.
- Temporal workflows are created, progress, and complete without repeated failure.
- APIs remain responsive during active production.
- Observability tools (Prometheus/Grafana/Jaeger) show current runtime signals.

Fail if any service is unavailable, state is not progressing, or workflow retries/failures persist without recovery.

## Embedded Web HMIs Inside WPF

The desktop WPF HMI now includes an **Embedded Web HMIs** tab that hosts the existing React HMIs inside the desktop shell through WebView2.

- Prime mover web HMI default: `http://localhost:3000`
- Machine web HMI defaults: `http://localhost:3001` through `http://localhost:3004`
- Override launch targets with environment variables:
  - `XTS_PRIME_MOVER_HMI_URL`
  - `XTS_MACHINE_0_HMI_URL`
  - `XTS_MACHINE_1_HMI_URL`
  - `XTS_MACHINE_2_HMI_URL`
  - `XTS_MACHINE_3_HMI_URL`

This keeps the backend shared while allowing the same web screens to run both in a browser and inside the .NET WPF operator client.

### 7) Stop and clean up

```bash
cd docker
docker compose -f docker-compose.dev.yml down
```

For full reset (including local volumes/data):

```bash
cd docker
docker compose -f docker-compose.dev.yml down -v
```

## Demo Runbook for Larger Audience

Use this sequence for team demos, leadership reviews, or stakeholder walkthroughs:

1. **Context (2-3 min):** Explain architecture boundaries (machine runtime, HMI runtime, data/observability).
2. **Live startup (2 min):** Show stack is already running (`docker compose ... ps`) and all major endpoints are reachable.
3. **Production flow (5-7 min):** Start line in Prime mover HMI and narrate part journey from entry to exit.
4. **Machine deep-dive (4-5 min):** Open one machine HMI and explain station-level ET/PT progression.
5. **Orchestration proof (3-4 min):** Show Temporal workflows for prime mover + parts + machines.
6. **Reliability proof (3-4 min):** Show alarms/watchdog/fault visibility and recovery behavior.
7. **Observability proof (3-4 min):** Show Prometheus metrics, Grafana dashboard updates, and Jaeger traces.
8. **Q&A ready artifacts:** Keep links/ports list, screenshots, and one short recording ready for follow-up sharing.

Recommended presenter roles for larger crowd:
- **Narrator:** explains business flow and success criteria.
- **Operator:** drives HMI interactions.
- **Observer:** watches Temporal/monitoring tabs and calls out evidence in real time.

## Continue Development on Another Laptop (Copilot-friendly)

1. Clone repo:
   - `git clone https://github.com/anoop6543/XTSPrimeMoverProject`
2. Open in Visual Studio with same GitHub account used for Copilot.
3. Ensure Copilot is enabled in VS.
4. Read these first:
   - `README.md`
   - `docs/ARCHITECTURE.md`
   - `AGENTS.md`
5. Build once before edits.
6. Prefer service/model changes first, then ViewModel, then XAML.

### About “Copilot history retained”
- Retain history context by committing/pushing project docs (`README`, `ARCHITECTURE`, `AGENTS`) and code changes.
- Use same GitHub account + same repo branch context.
- Chat/session history itself is environment-dependent; the canonical persistent context for future Copilot runs is the repo content and these docs.

## Key Files

- Engine: `Services/XTSSimulationEngine.cs`
- Logging: `Services/SimulationDataLogger.cs`
- PLC/Motion FBs: `Services/TwinCAT*.cs`
- Models: `Models/*.cs`
- View root: `ViewModels/MainViewModel.cs`
- Machine/data gateway contracts: `Services/HmiServiceContracts.cs`
- Local gateway adapter: `Services/LocalSimulationServiceGateway.cs`
- Remote machine mock: `Services/RemoteTwinCatMock/RemoteTwinCatMachineGatewayMock.cs`
- Main UI: `MainWindow.xaml`
- Architecture: `docs/ARCHITECTURE.md`
- Split-runtime plan: `docs/SEPARATED-RUNTIME-PLAN.md`
- Agent context: `AGENTS.md`
