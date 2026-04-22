# Split Runtime Modernization Plan (Beckhoff + HMI + Data Service)

## 1. Objective
Move from a single-process WPF simulation to a separable architecture where:
- **Machine logic** runs on the Beckhoff/TwinCAT side.
- **HMI** runs as an independent client application.
- **Database layer** runs as an independent data service.

The first implementation phase keeps runtime behavior unchanged while introducing explicit contracts and adapter boundaries.

## 2. Current State (Today)
- WPF app hosts UI and orchestration in one process.
- `MainViewModel` talks directly to `XTSSimulationEngine`.
- `XTSSimulationEngine` directly owns simulation state + logger access.
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
- `Services/HmiServiceContracts.cs` added.
- `Services/LocalSimulationServiceGateway.cs` added.
- `MainViewModel` refactored to consume `ISimulationGatewayService`.
- Composition root updated in `MainWindow.xaml.cs`.

## 8. Phase A Implementation Status (Completed in this pass)
- Added architecture and migration documentation for split-runtime target.
- Added `ISimulationGatewayService` contract as HMI boundary.
- Added `LocalSimulationServiceGateway` adapter over `XTSSimulationEngine`.
- Refactored `MainViewModel` to consume gateway abstraction.
- Updated `MainWindow` composition root to inject engine + gateway.
- Build validation: successful.

## 9. Next Implementation Backlog (Execution-Ready)
1. Split current gateway into `IMachineGatewayService` and `IDataGatewayService` to isolate command/telemetry vs persistence concerns.
2. Convert gateway APIs to async where remote latency is expected (history queries, table reads, exports, orchestration apply/validate).
3. Add connection/session status model (Connected, Degraded, Reconnecting, Offline) surfaced to HMI.
4. Implement first remote machine gateway adapter (protocol candidate: OPC UA or gRPC) behind `IMachineGatewayService`.
5. Keep `LocalSimulationServiceGateway` as fallback/local test implementation.
6. Add contract tests ensuring identical behavior between local and remote gateway implementations.
7. Introduce audit-friendly command pipeline (who/when/what command) before enabling remote write commands in production.
