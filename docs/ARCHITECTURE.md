# XTS Prime Mover – EV Battery Module Line · Architecture

## 1. Purpose

Physics-true simulation of a Beckhoff-style XTS line assembling 12S prismatic EV battery modules, with an
AI intelligence layer (digital twins, predictive maintenance, SPC, OEE, bottleneck detection, energy,
autopilot, copilot), a live Three.js 3D digital twin and a WPF HMI. No hardware is required; every behaviour
is produced by the runtime core and can be verified headless.

---

## 2. Solution layout

| Project | Target | Role |
|---|---|---|
| `Core/XTSPrimeMoverProject.Core.csproj` | `net10.0` | Models, PLC/motion FBs, engine, gateways, AI layer, SQLite logging, 3D frame contract. **No WPF.** |
| `XTSPrimeMoverProject.csproj` | `net10.0-windows` (WPF) | HMI: views, view models, WPF controls, WebView2 + HelixToolkit 3D hosts. References Core. |
| `tests/XTSPrimeMoverProject.Tests` | `net10.0` | xUnit: statistics, analytics, whole-line simulation and fault scenarios on the real engine. |
| `tools/TwinRecorder` | `net10.0` | Runs the engine headless and exports `TwinFrame` JSON for the 3D twin / videos. |
| `tools/video` | Node | Playwright capture of `Web/twin` + ffmpeg encoding (`render-all.sh`). |
| `Web/twin` | static web | Three.js digital twin (vendored three.js r186, offline). Copied to the WPF output. |

The WPF project excludes `Core/`, `tests/`, `tools/` from its own compile items (`Compile Remove`) and references Core.
Namespaces are unchanged (`XTSPrimeMoverProject.Models`, `XTSPrimeMoverProject.Services`).

```text
Core (net10.0)                                             HMI (WPF)
Models ── Station / Machine / Mover / Robot / Part           MainWindow.xaml (tabs, 2D canvas, AI Command Center)
Services                                                     ViewModels (Main, Intelligence, Machine, Station, Mover, Robot)
 ├ XTSSimulationEngine  ◄── ISimulationDispatcher ◄──────── Infrastructure/WpfSimulationDispatcher
 ├ TwinCAT motion + PLC FBs                                  Infrastructure/DigitalTwinWebHost ──► WebView2 ──► Web/twin
 ├ ProductionOrchestration                                   Controls/NativeLineView3D (HelixToolkit)
 ├ Intelligence/ (LineIntelligenceHub …)                     Controls/TrendChart, RingGauge
 ├ DigitalTwin3D/TwinFrame ─────────────────────────────►  (also used by tools/TwinRecorder)
 ├ IMachineGatewayService / IDataGatewayService
 └ SimulationDataLogger → DatabaseWriteQueue → SQLite
```

Core rule (unchanged): process logic lives in `Core`, views only project state; the WebView2/Helix hosts are
view-layer adapters that read the gateway.

---

## 3. Runtime model (`XTSSimulationEngine`)

### 3.1 Fixed PLC cycle
The wall-clock timer (50 ms) only feeds time in; the engine integrates in **fixed 20 ms cycles**
(`RunFixedSteps`) so behaviour is identical at 0.1×–5× speed and in headless runs (`AdvanceManually`).
Per cycle: machines → robots → robot cells → entry/exit docks → mover motion → traceability → watchdogs →
snapshots → zone blinkers → intelligence.

### 3.2 Transport
- Mover position is degrees on a 6 m stadium loop (`XtsTrackGeometry` converts to metres).
- `FbXtsMoverAxis` runs MC_Power + jerk-limited MC_MoveVelocity (160 °/s² ≈ 2.7 m/s², 4000 °/s³).
- The planner computes a velocity limit = min(cruise, braking curve to the next stop, gap curve to the mover ahead)
  and clamps position so movers **never overtake or touch** (12° ≈ 0.20 m pitch).
- Stop points: machine docks (45/135/225/315°), entry (205°), exit (0°). A mover stops only if it has business there:
  its part targets that machine and the machine is available; it is the reserved empty mover for a pickup; it is the
  reserved empty mover for the entry; or it carries a finished (Good/Bad) part to the exit.
- Loaded movers whose target machine is under maintenance/faulted **recirculate** instead of blocking the dock.

### 3.3 Robot cells (dual gripper, outfeed nest)
- A finished part leaves the last station into the machine's `OutfeedNest` (internal shuttle), so the machine can
  accept the next part immediately.
- Cell controller priorities: (1) load a raw part from a docked mover (machine first – protects the constraint),
  (2) pre-fetch the nest part and **stage** it at the dock, (3) release movers with nothing to do.
- Staged robot + docked empty mover → place (≈0.8 s dwell). Staged robot + docked loaded mover → **swap**: gripper A
  picks the raw part, gripper B places the finished one (≈1.6 s dwell), then the robot loads the machine.
- Deadlock freedom on a single no-overtaking loop: a docked mover only waits for its own machine to finish
  processing; the nest is always emptied by the next service.

### 3.4 Quality
`Station` consults an `IStationProcessModel` (implemented by `LineIntelligenceHub`) for its cycle-time factor and its
measurement. Each characteristic (`EvModuleQualityCatalog`) has nominal, spec limits and a qualified Cpk; healthy
sigma = distance-to-limit / (3·Cpk). Degradation shifts the mean and inflates sigma. Out-of-spec → `HasDefect`; the final
machine resolves Good/Bad. Measurements are appended to the part history (and thus to `PartEvents`).

### 3.5 Maintenance lifecycle
`Machine.Maintenance` = None → Pending (no new parts, current part drains, nest emptied, robot clear) → InProgress
(one technician; breakdowns have priority) → None (twin restored, detectors recommissioned, RUL reset).
Breakdowns occur when hidden damage reaches 1.0: parts in the cell are damaged, repair ≈ 35 s vs 9 s planned.

### 3.6 Watchdogs
Machine stall (14 s), robot stall (9 s; staged robots are exempt – they are waiting, not stalled), docked-mover stall
(45 s → released to recirculate). Codes/counters as before (`WD-*`).

---

## 4. AI intelligence layer (`Core/Services/Intelligence`)

| File | Responsibility |
|---|---|
| `LineIntelligenceHub.cs` | Orchestrates everything per cycle; implements `IStationProcessModel`; maintenance lifecycle; fault injection; publishes immutable `IntelligenceSnapshot`s |
| `DigitalTwin.cs` | `MachineDigitalTwin` (hidden damage on a P-F curve, thermal model + healthy shadow model, sensors), `RobotDigitalTwin` (vacuum) |
| `QualityCatalog.cs` | EV module characteristics, spec limits, Cpk, failure modes and sensor signatures |
| `AnomalyDetector.cs` | Residual detectors: commissioning fingerprint, EWMA (exact limits) + two-sided CUSUM, adaptive baseline, context-aware running/idle |
| `RulEstimator.cs` | Exponential degradation (ln(y+Φ) linear in operating time), multi-horizon LSQ, cross-horizon confidence |
| `SpcMonitor.cs` | Individuals chart, Western Electric 1–4 + Nelson trend, rolling Cpk |
| `ProductionAnalytics.cs` | `OeeTracker` (ISO 22400), `BottleneckDetector` (active-period method), `EnergyMonitor` |
| `AutopilotController.cs` | Critical-WIP release (W₀ = r_b × T₀, +35 %), decision log |
| `CopilotReasoner.cs` | Ranked insights, offline Q&A, grounded JSON for the LLM |
| `ClaudeCopilotClient.cs` | Optional Claude copilot (official Anthropic SDK, `claude-opus-5-5`, server-side refusal fallback) |
| `Statistics.cs` | Normal CDF/inverse, EWMA, ring buffer, regression, Holt forecaster |

Information discipline: the AI only reads sensors and measurements. True damage is exposed only as
"twin ground truth" in the HMI for validation.

Predictive maintenance trigger (autopilot): RUL < max(150 s, 2.5 × (drain + PM time)) with confidence ≥ 0.45, or
health < 42 %, or an SPC rule on the key characteristic **and** a sensor anomaly, or a degraded robot gripper.

---

## 5. 3D digital twin

- Contract: `TwinFrame` (movers, machines with stations/nest/maintenance/health, robots with both grippers, KPIs,
  alerts, decisions). Built by `TwinFrameBuilder.Build(IMachineGatewayService)`.
- Live: `DigitalTwinWebHost` maps `Web/twin` to `https://twin.local`, posts a frame every ≈66 ms; the page renders
  ≈100 ms behind and interpolates (mover angles with wrap-around, robot/station progress).
- Replay: `?replay=frames.json` (recorded by `tools/TwinRecorder`). Capture: `&capture=1&tour=overview|stations|ai`
  exposes `window.twinCapture.renderFrame(i)` for deterministic video rendering.
- Scene: factory hall, custom image-based lighting, PCF shadows, bloom only above HDR 1.6 (true emitters), XTS modules
  with LEDs, movers with rollers, 6-axis robots with analytic two-link IK, rotary dials with process tools
  (laser with beam, nutrunner, structured-light 3D vision, laser line scanner, HiPot beacon), andon towers, live cell
  HMI canvases, technician during maintenance, CSS2D labels.
- `NativeLineView3D` (HelixToolkit) is a no-runtime fallback with presets and a fly-through tour.

---

## 6. Error Handling Architecture

The application uses a centralized error handling mechanism provided by `Core/Services/ErrorHandlingService.cs`.

### Layers

| Layer | Mechanism | Behavior |
|---|---|---|
| **Global (App.xaml.cs)** | `DispatcherUnhandledException`, `AppDomain.UnhandledException`, `TaskScheduler.UnobservedTaskException` | Catches all unhandled exceptions, logs to `crash.log`, reports to `ErrorHandlingService`, shows user-friendly dialog |
| **Engine (XTSSimulationEngine)** | Per-subsystem isolation via `RunSubsystem()` | Each tick subsystem (movers, robots, machines, watchdogs, etc.) runs in its own try/catch so a failure in one does not halt the others |
| **Database (SimulationDataLogger)** | Retry with exponential backoff via `ExecuteWithRetry()` | All DB read operations retry on transient SQLite errors (BUSY/LOCKED); circuit breaker prevents repeated hammering of a failing DB |
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

## 7. Threading Architecture

| Thread | Responsibility | Mechanism |
|---|---|---|
| UI thread | WPF rendering, bindings, commands, WebView2/Helix updates | WPF Dispatcher |
| Simulation thread | Fixed-cycle engine + intelligence hub | `System.Threading.Timer` callback |
| DB-WriteQueue thread | All SQLite writes | dedicated `Thread` + `BlockingCollection<Action>` |
| Thread pool | CSV export, remote-mock latency, offline copilot answers, Claude requests | `Task.Run` / async |

- `_simulationLock` guards `Update`, start/stop/reset and every operator command (autopilot, fault injection,
  maintenance requests).
- The engine marshals `StateChanged` through `ISimulationDispatcher` (`WpfSimulationDispatcher` in the HMI,
  `InlineSimulationDispatcher` headless) **outside** the lock, so the UI never deadlocks with the simulation thread.
- `IntelligenceSnapshot` is immutable and published via a volatile reference: the UI reads it without locks.
- `DatabaseWriteQueue` shutdown never disposes the collection under a still-draining consumer and swallows
  shutdown races on both sides (previously this could crash the process on close).

---

## 8. Persistence Contract

Tables (unchanged): `Recipes`, `Parts`, `PartEvents`, `MachineRuns`, `Results`, `ProductionSnapshots`, `ErrorLogs`, `Alarms`.
Station measurements travel in the part history (`PartEvents.ProcessStep`); AI events go to `Alarms`.
Schema changes should be treated as explicit migrations.

---

## 8a. Verification

- `dotnet test tests/XTSPrimeMoverProject.Tests` (Linux/macOS/Windows): 26 tests – statistics, SPC, anomaly detection,
  RUL convergence, OEE, bottleneck, energy, critical WIP, 10-minute whole-line run (no collisions, no deadlock),
  fault scenario with/without autopilot, copilot answers and LLM context, maintenance lifecycle, measurement genealogy.
- `dotnet build XTSPrimeMoverProject.csproj -p:EnableWindowsTargeting=true` compiles the WPF HMI (incl. XAML) off Windows.

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


Since the Core split the machine runtime is a self-contained library: a future out-of-process runtime (gRPC/OPC UA
server hosting `XTSSimulationEngine`) only needs to serve `IMachineGatewayService` and `TwinFrame`s.
