# Split Runtime Modernization Plan (Beckhoff + HMI + Data Service)

## 1. Objective
Move from a single-process WPF simulation to a separable architecture where:
- **Machine logic** runs on the Beckhoff/TwinCAT side.
- **HMI** runs as an independent client application.
- **Database layer** runs as an independent data service.

The first implementation phase keeps runtime behavior unchanged while introducing explicit contracts and adapter boundaries.

## 2. Current State (Today)
- WPF app hosts UI and orchestration in one process.
- `MainViewModel` uses split service boundaries (`IMachineGatewayService`, `IDataGatewayService`).
- `XTSSimulationEngine` still owns local simulation state + logger access.
- `SimulationDataLogger` writes SQLite in-process.

## 3. Target State (End Goal)

```text
+------------------------------+        +----------------------------------+
| Beckhoff / TwinCAT Runtime   |<------>| HMI Runtime (WPF or Web Client) |
| - machine cycle logic        |  API   | - operator screens              |
| - mover/robot orchestration  |        | - commands, diagnostics, trends |
+------------------------------+        +-------------------+--------------+
															|
															| Data API
															v
										 +----------------------------------+
										 | Data Service                      |
										 | - part history                    |
										 | - alarms, machine runs, snapshots |
										 | - export/report endpoints         |
										 +----------------------------------+
```

### 3.1 Communication Principle
Use **service contracts** at boundaries so transport can change later without rewriting UI logic:
- Phase 1 transport: in-process adapter (local gateway).
- Future transport: gRPC/REST/OPC UA/event bus.

### 3.2 Ownership Boundaries
- **Machine side owns**: real-time control state, motion state, machine execution state, watchdog evaluation.
- **HMI side owns**: presentation composition, operator input, local view state.
- **Data service owns**: persistence schema, querying/filtering/export APIs.

## 4. Phased Execution

### Phase A (Now): Introduce contracts and local gateway
- Add HMI-facing interfaces and DTOs.
- Add a local adapter over `XTSSimulationEngine`.
- Refactor `MainViewModel` to use interface abstraction.
- Keep behavior and UI unchanged.

### Phase B: Split data boundary
- Move direct DB table and export operations behind dedicated data service interface.
- Route HMI database reads/writes through gateway methods only.
- Keep SQLite initially; isolate persistence implementation.

### Phase C: Externalize machine runtime boundary
- Replace local machine adapter with remote client (TwinCAT-facing bridge).
- Map command/telemetry contracts to chosen protocol.
- Introduce reconnect, heartbeat, and degraded-mode UI behavior.

### Phase D: Production hardening
- Add authN/authZ and command audit trail.
- Add resilient buffering/retry policy for telemetry and DB writes.
- Add contract tests and integration tests for all service boundaries.

## 5. Contract Design Rules
1. No UI type references in service interfaces.
2. Keep command methods explicit and side-effect clear.
3. Keep snapshots immutable from consumer perspective.
4. Expose list/query results as read-only collections.
5. Keep asynchronous surface area ready for future remote calls.

## 6. Risks and Controls
- **Risk**: unclear transport choice delays externalization.
  - **Control**: freeze contracts first; transport remains pluggable.
- **Risk**: real-time update cadence mismatch after remote split.
  - **Control**: define polling/push cadence contract and backpressure behavior.
- **Risk**: command safety regressions.
  - **Control**: keep interlock validation at machine side and return explicit gate results.

## 7. Immediate Deliverables (this implementation pass)
- `Services/HmiServiceContracts.cs` added and split into:
  - `IMachineGatewayService`
  - `IDataGatewayService`
- `Services/LocalSimulationServiceGateway.cs` added and updated to implement both interfaces.
- `Services/RemoteTwinCatMock/RemoteTwinCatMachineGatewayMock.cs` added for machine-boundary mock integration.
- `MainViewModel` refactored to consume machine + data gateway interfaces.
- Composition root updated in `MainWindow.xaml.cs` with runtime toggle-driven machine gateway selection.

## 8. Phase A Implementation Status (Completed in this pass)
- Added architecture and migration documentation for split-runtime target.
- Completed gateway contract split (`IMachineGatewayService`, `IDataGatewayService`).
- Added `LocalSimulationServiceGateway` adapter over `XTSSimulationEngine` implementing both interfaces.
- Added `RemoteTwinCatMachineGatewayMock` to simulate remote machine-side integration.
- Added runtime machine gateway mode toggle in `App.xaml` resources.
- Refactored `MainViewModel` to consume machine + data gateway abstractions.
- Updated `MainWindow` composition root to route machine gateway through local or remote mock selection.
- Build validation: successful.

## 9. Execution Task Board (Phase-by-Phase, Repo-Backed)

Use this section as the working checklist for the remaining split-runtime work. Keep checkboxes current as implementation progresses.

### Phase B1 - Make gateway contracts remote-ready

**Goal**: Remove synchronous-only assumptions from the HMI-to-runtime boundary so remote transport can be introduced without reshaping the UI again.

#### Files, classes, and methods
- `Services/HmiServiceContracts.cs`
  - `IMachineGatewayService`
    - `GetOrchestrationSteps()`
    - `TryApplyOrchestration(IReadOnlyList<OrchestrationStepDefinition> stepDefinitions, out string message)`
    - `PreviewOrchestrationValidation(IReadOnlyList<OrchestrationStepDefinition> stepDefinitions)`
    - `GetOrchestrationSafetyGateStatuses()`
    - `GetWatchdogStatus()`
  - `IDataGatewayService`
    - `GetPartHistory(string trackingNumber)`
    - `GetPartSummary(string trackingNumber)`
    - `GetExportableTables()`
    - `GetAllTables()`
    - `GetTableColumns(string tableName)`
    - `GetTableRowCount(string tableName)`
    - `GetTableRows(string tableName, int maxRows = 500)`
    - `ExportTableToCsv(string tableName, string? exportDirectory = null)`
- `Services/LocalSimulationServiceGateway.cs`
  - `LocalSimulationServiceGateway`
    - `GetWatchdogStatus()`
    - `GetOrchestrationSteps()`
    - `TryApplyOrchestration(...)`
    - `PreviewOrchestrationValidation(...)`
    - `GetOrchestrationSafetyGateStatuses()`
    - `GetPartHistory(string trackingNumber)`
    - `GetPartSummary(string trackingNumber)`
    - `GetExportableTables()`
    - `GetAllTables()`
    - `GetTableColumns(string tableName)`
    - `GetTableRowCount(string tableName)`
    - `GetTableRows(string tableName, int maxRows = 500)`
    - `ExportTableToCsv(string tableName, string? exportDirectory = null)`
- `Services/RemoteTwinCatMock/RemoteTwinCatMachineGatewayMock.cs`
  - `RemoteTwinCatMachineGatewayMock`
    - `GetWatchdogStatus()`
    - `GetOrchestrationSteps()`
    - `TryApplyOrchestration(...)`
    - `PreviewOrchestrationValidation(...)`
    - `GetOrchestrationSafetyGateStatuses()`
    - `DispatchWithLatency(Action command)`
    - `SimulateNetworkLatencySync()`
- `Services/XTSSimulationEngine.cs`
  - `XTSSimulationEngine`
    - `GetOrchestrationSteps()`
    - `TryApplyOrchestration(IReadOnlyList<int> orderedMachineIds, out string message)`
    - `TryApplyOrchestration(IReadOnlyList<OrchestrationStepDefinition> stepDefinitions, out string message)`
    - `PreviewOrchestrationValidation(IReadOnlyList<OrchestrationStepDefinition> stepDefinitions)`
    - `GetOrchestrationSafetyGateStatuses()`
    - `GetWatchdogStatus()`
    - `GetPartHistory(string trackingNumber)`
    - `GetPartSummary(string trackingNumber)`
    - `GetExportableTables()`
    - `GetAllTables()`
    - `GetTableColumns(string tableName)`
    - `GetTableRowCount(string tableName)`
    - `GetTableRows(string tableName, int maxRows = 500)`
    - `ExportTableToCsv(string tableName, string? exportDirectory = null)`
- `ViewModels/MainViewModel.cs`
  - `MainViewModel`
    - `LoadExportTables()`
    - `LoadDbTables()`
    - `LoadSelectedDbTableRows()`
    - `LoadOrchestrationSteps()`
    - `ApplyOrchestrationFromHmi()`
    - `PreviewOrchestrationValidation()`
    - `InspectPartHistory()`
    - `ExportCsv()`
    - `RefreshWatchdogStatuses()`

#### Tasks
- [x] Convert remote-latency-sensitive gateway methods to async contract signatures.
- [x] Add cancellation support where queries or exports may run long.
- [x] Refactor local gateway implementation to match the async contract.
- [x] Refactor remote mock to simulate async latency consistently instead of mixing fire-and-forget and blocking calls.
- [x] Update `MainViewModel` methods to await gateway calls instead of assuming immediate in-process responses.

#### Phase B1 status
- Status: Completed
- Implemented in:
  - `Services/HmiServiceContracts.cs`
  - `Services/LocalSimulationServiceGateway.cs`
  - `Services/RemoteTwinCatMock/RemoteTwinCatMachineGatewayMock.cs`
  - `ViewModels/MainViewModel.cs`
- Notes:
  - Gateway query/orchestration APIs are now `Task`-based with `CancellationToken` support.
  - Orchestration apply now returns an explicit `OrchestrationApplyResult` instead of using an `out` parameter.
  - `XTSSimulationEngine` remains internally synchronous for now; async readiness is provided at the gateway boundary in this phase.
  - `MainViewModel` now consumes gateway data/orchestration/watchdog/export flows asynchronously.

---

### Phase B2 - Add gateway connection and session status

**Goal**: Surface actual runtime health to the HMI instead of only showing gateway mode text.

#### Files, classes, and methods
- `Services/HmiServiceContracts.cs`
  - Add a gateway/session status model alongside `IMachineGatewayService` and `IDataGatewayService`.
- `Services/LocalSimulationServiceGateway.cs`
  - `LocalSimulationServiceGateway`
    - constructor `LocalSimulationServiceGateway(XTSSimulationEngine engine)`
    - `OnEngineStateChanged(object? sender, EventArgs e)`
    - `OnEngineLogGenerated(object? sender, string message)`
- `Services/RemoteTwinCatMock/RemoteTwinCatMachineGatewayMock.cs`
  - `RemoteTwinCatMachineGatewayMock`
    - constructor `RemoteTwinCatMachineGatewayMock(IMachineGatewayService inner, int commandLatencyMs = 40)`
    - `DispatchWithLatency(Action command)`
    - `OnInnerStateChanged(object? sender, EventArgs e)`
    - `OnInnerLogGenerated(object? sender, string message)`
- `ViewModels/MainViewModel.cs`
  - `MainViewModel`
    - constructor `MainViewModel(IMachineGatewayService machine, IDataGatewayService data, string gatewayModeStatus = "Machine Gateway: Local")`
    - property `GatewayModeStatus`
    - `UpdateStatus()`
    - `OnEngineStateChanged(object? sender, EventArgs e)`
- `MainWindow.xaml`
  - Gateway mode/status display area in the line HMI panel.
- `MainWindow.xaml.cs`
  - `MainWindow()`

#### Tasks
- [x] Introduce `Connected`, `Degraded`, `Reconnecting`, and `Offline` gateway/session states.
- [x] Expose live connection/session state through the gateway abstraction.
- [x] Replace static status-only usage in `MainViewModel` with live state properties.
- [x] Add HMI binding updates in `MainWindow.xaml` to show mode and health separately.
- [x] Keep local gateway reporting a stable healthy state while remote implementations can transition.

#### Phase B2 status
- Status: Completed
- Implemented in:
  - `Services/HmiServiceContracts.cs`
  - `Services/LocalSimulationServiceGateway.cs`
  - `Services/RemoteTwinCatMock/RemoteTwinCatMachineGatewayMock.cs`
  - `ViewModels/MainViewModel.cs`
  - `MainWindow.xaml`
- Notes:
  - Added `GatewayConnectionState` and `GatewaySessionStatus` to the shared machine gateway contract.
  - `LocalSimulationServiceGateway` now reports a stable local connected state and degrades only when event forwarding encounters recoverable issues.
  - `RemoteTwinCatMachineGatewayMock` now publishes reconnecting, connected, degraded, and offline transitions based on delayed command dispatch and remote-style read/apply outcomes.
  - `MainViewModel` now exposes gateway health summary, state text, detail text, and color for the HMI.
  - The Line HMI now shows gateway mode separately from live session health.

---

### Phase C1 - Implement first real remote machine gateway

**Goal**: Replace the placeholder mock-only remote boundary with a real protocol-backed machine gateway.

#### Files, classes, and methods
- `Services/HmiServiceContracts.cs`
  - `IMachineGatewayService`
- `MainWindow.xaml.cs`
  - `MainWindow()`
  - `ReadAppBoolSetting(string key, bool defaultValue)`
  - `ReadAppIntSetting(string key, int defaultValue)`
- `App.xaml`
  - `UseRemoteTwinCatMachineGatewayMock`
  - `RemoteTwinCatMachineGatewayMockLatencyMs`
- `Services/RemoteTwinCatMock/RemoteTwinCatMachineGatewayMock.cs`
  - `RemoteTwinCatMachineGatewayMock` remains fallback/test-only after real gateway exists.
- New remote gateway implementation file(s)
  - Suggested location: `Services/RemoteTwinCat/`
  - Suggested class: a real `IMachineGatewayService` implementation for the selected transport.

#### Tasks
- [x] Choose and document the first production protocol candidate.
- [x] Add a real remote machine gateway implementation behind `IMachineGatewayService`.
- [x] Extend app configuration/resources with endpoint, timeout, and protocol settings.
- [x] Update `MainWindow()` composition logic to select local, mock remote, or real remote gateway.
- [x] Keep `RemoteTwinCatMachineGatewayMock` available for fallback and latency testing.

#### Phase C1 status
- Status: Completed
- Production protocol candidate: HTTP/JSON
- Implemented in:
  - `Services/RemoteTwinCat/RemoteTwinCatHttpGateway.cs`
  - `App.xaml`
  - `MainWindow.xaml.cs`
- Notes:
  - Added the first real remote machine gateway implementation as `RemoteTwinCatHttpGateway`, using `HttpClient` and JSON payloads against configurable remote endpoints.
  - Added application resources for `MachineGatewayMode`, `RemoteTwinCatHttpGatewayBaseAddress`, and `RemoteTwinCatHttpGatewayTimeoutMs` while preserving the legacy mock toggle.
  - `MainWindow` composition can now select `Local`, `RemoteMock`, or `RemoteHttp` machine gateway modes.
  - `RemoteTwinCatMachineGatewayMock` remains available as a fallback and latency-testing path.
  - The HTTP gateway surfaces explicit reconnecting/offline/degraded behavior when the configured remote endpoint is unavailable or returns invalid responses.

---

### Phase C2 - Externalize the data gateway

**Goal**: Move database reads and exports behind a dedicated data boundary instead of routing them through the simulation engine.

#### Files, classes, and methods
- `Services/HmiServiceContracts.cs`
  - `IDataGatewayService`
- `Services/LocalSimulationServiceGateway.cs`
  - `LocalSimulationServiceGateway`
    - all `IDataGatewayService` members currently implemented in this class
- `Services/XTSSimulationEngine.cs`
  - `XTSSimulationEngine`
    - `GetPartHistory(string trackingNumber)`
    - `GetPartSummary(string trackingNumber)`
    - `GetExportableTables()`
    - `GetAllTables()`
    - `GetTableColumns(string tableName)`
    - `GetTableRowCount(string tableName)`
    - `GetTableRows(string tableName, int maxRows = 500)`
    - `ExportTableToCsv(string tableName, string? exportDirectory = null)`
    - `GetDefaultExportDirectory()`
- `Services/SimulationDataLogger.cs`
  - `SimulationDataLogger`
    - `GetPartHistory(string trackingNumber)`
    - `GetPartSummary(string trackingNumber)`
    - `GetExportableTables()`
    - `GetAllTables()`
    - `GetTableColumns(string tableName)`
    - `GetTableRowCount(string tableName)`
    - `GetTableRows(string tableName, int maxRows = 500)`
    - `ExportTableToCsv(string tableName, string? exportDirectory = null)`
- `MainWindow.xaml.cs`
  - `MainWindow()`

#### Tasks
- [ ] Separate data gateway composition from machine gateway composition in `MainWindow()`.
- [ ] Reduce or remove the engine façade methods that only forward data access.
- [ ] Introduce a standalone local data gateway over `SimulationDataLogger`.
- [ ] Preserve `IDataGatewayService` contract stability so local and future remote data services stay swappable.
- [ ] Keep SQLite-backed local behavior as the default fallback path.

---

### Phase D1 - Add contract and integration tests

**Goal**: Prove that local, mock remote, and real remote gateways behave consistently.

#### Files, classes, and methods
- New test project
  - Suggested path: `XTSPrimeMoverProject.Tests/`
- Test targets
  - `Services/HmiServiceContracts.cs`
  - `Services/LocalSimulationServiceGateway.cs`
  - `Services/RemoteTwinCatMock/RemoteTwinCatMachineGatewayMock.cs`
  - real remote gateway implementation file(s)
  - `ViewModels/MainViewModel.cs`
- `ViewModels/MainViewModel.cs`
  - `LoadExportTables()`
  - `LoadDbTables()`
  - `LoadSelectedDbTableRows()`
  - `LoadOrchestrationSteps()`
  - `ApplyOrchestrationFromHmi()`
  - `PreviewOrchestrationValidation()`
  - `InspectPartHistory()`
  - `ExportCsv()`
  - `RefreshSafetyGates()`
  - `RefreshWatchdogStatuses()`

#### Tasks
- [ ] Create a test project using the framework already preferred for the solution.
- [ ] Add contract parity tests for local and remote machine gateway implementations.
- [ ] Add tests for degraded/offline/reconnect session-state behavior.
- [ ] Add `MainViewModel` tests for async command/query paths.
- [ ] Add orchestration safety-gate and apply-validation tests.

---

### Phase D2 - Add command audit trail and production safety controls

**Goal**: Ensure remote-capable commands are attributable, reviewable, and safe before production write enablement.

#### Files, classes, and methods
- `Services/HmiServiceContracts.cs`
  - `IMachineGatewayService`
    - `Start()`
    - `Stop()`
    - `Reset()`
    - `SetSimulationSpeed(double speed)`
    - `TryApplyOrchestration(...)`
- `Services/LocalSimulationServiceGateway.cs`
  - `Start()`
  - `Stop()`
  - `Reset()`
  - `SetSimulationSpeed(double speed)`
  - `TryApplyOrchestration(...)`
- `Services/RemoteTwinCatMock/RemoteTwinCatMachineGatewayMock.cs`
  - `Start()`
  - `Stop()`
  - `Reset()`
  - `SetSimulationSpeed(double speed)`
  - `TryApplyOrchestration(...)`
- `Services/SimulationDataLogger.cs`
  - `LogAlarm(string severity, string source, string message, bool isActive)`
  - `LogError(string source, string message, string? detail = null)`
  - add new audit persistence methods/tables here or in a dedicated audit service.
- `Services/DatabaseWriteQueue.cs`
  - `Enqueue(Action writeOperation)`
- `ViewModels/MainViewModel.cs`
  - command entry points:
    - `StartCommand`
    - `StopCommand`
    - `ResetCommand`
    - `ApplyOrchestrationCommand`
    - `ExportCsvCommand`

#### Tasks
- [ ] Define an audit record model for who, when, what command, target, and result.
- [ ] Record command attempts and outcomes for all machine write operations.
- [ ] Persist audit entries through the existing write queue or a dedicated audit path.
- [ ] Make orchestration changes audit-visible before enabling remote production writes.
- [ ] Add operator/session identity plumbing needed for audit attribution.

---

### Phase D3 - Add security, resilience, and degraded-mode behavior

**Goal**: Harden the split runtime for production-style operation.

#### Files, classes, and methods
- `Services/ErrorHandlingService.cs`
  - `ExecuteWithRetry(Action action, string operationName, ErrorCategory category, int maxRetries = 3, int baseDelayMs = 50)`
  - `ExecuteWithRetry<T>(Func<T> func, string operationName, ErrorCategory category, T? fallback = default, int maxRetries = 3, int baseDelayMs = 50)`
- `Services/RemoteTwinCatMock/RemoteTwinCatMachineGatewayMock.cs`
  - use as the design reference for latency/degraded-mode simulation, not as the final production design.
- real remote gateway implementation file(s)
  - reconnect
  - heartbeat
  - retry/backoff
  - auth handling
- `ViewModels/MainViewModel.cs`
  - `UpdateStatus()`
  - `OnEngineStateChanged(object? sender, EventArgs e)`
  - command enablement and status messaging logic
- `MainWindow.xaml`
  - status and operator diagnostics areas for degraded/offline/auth failure state
- `App.xaml`
  - runtime settings for remote endpoint, timeout, retry, and security configuration

#### Tasks
- [ ] Add authentication handling for remote runtime access.
- [ ] Add authorization rules for remote write commands.
- [ ] Add heartbeat and reconnect behavior.
- [ ] Add retry/backoff and buffering where remote dependency loss is transient.
- [ ] Surface degraded/offline/auth failure clearly in the HMI.

## 10. Suggested Working Order

1. Phase B1 - async/remote-ready contracts
2. Phase B2 - connection/session state in HMI
3. Phase C1 - first real remote machine gateway
4. Phase C2 - standalone data gateway
5. Phase D1 - contract and integration tests
6. Phase D2 - audit trail
7. Phase D3 - security and resilience

## 11. Working Notes

- Update this file first when scope or ordering changes.
- Check off items only after code is implemented and validated.
- Keep `LocalSimulationServiceGateway` usable as the in-process fallback throughout the migration.
- Keep `RemoteTwinCatMachineGatewayMock` available for latency/degraded-mode testing even after the real remote gateway exists.
