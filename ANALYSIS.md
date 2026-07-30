# XTS Prime Mover Manufacturing Simulation Analysis

## Identified Issues and Improvements

1.  **NuGet Vulnerability (Fixed)**
    *   **Issue:** The project implicitly references an older, vulnerable version (2.1.11) of `SQLitePCLRaw` through `Microsoft.Data.Sqlite` 10.0.0. `dotnet list package --include-transitive` and build warnings (NU1903) highlighted high severity vulnerability GHSA-2m69-gcr7-jv3q.
    *   **Improvement/Fix:** Explicit package references to `SQLitePCLRaw.bundle_e_sqlite3`, `SQLitePCLRaw.core`, `SQLitePCLRaw.lib.e_sqlite3`, and `SQLitePCLRaw.provider.e_sqlite3` version 2.1.12 were added to the `.csproj` file, mitigating the vulnerability.

2.  **Memory Leak in `XTSSimulationEngine.cs` (Fixed)**
    *   **Issue:** In `XTSSimulationEngine.SyncPartHistoryLogs`, the `_partHistoryLogIndex` dictionary mapped `PartId` (a GUID) to an integer tracking log progression. However, entries were never removed for parts that finished processing and exited the simulation, leading to an unbounded dictionary growth as the simulation ran over time.
    *   **Improvement/Fix:** Cleanup logic was added. `activePartIds` are retrieved via `GetActiveParts()`, and any key in `_partHistoryLogIndex` not in `activePartIds` is actively removed on each sync.

3.  **Memory Leak in `ErrorHandlingService.cs` (Fixed)**
    *   **Issue:** The `_throttleTracker` dictionary prevents redundant errors by logging throttle timestamps. The `throttleKey` format is `$"{category}:{source}:{message}"`. Dynamic messages caused a memory leak since old keys were never removed.
    *   **Improvement/Fix:** Implemented a cleanup mechanism (`CleanupThrottleTracker`). It routinely (once per minute minimum) scans for and removes dictionary entries whose timestamp is older than `ThrottleWindow` (2 seconds).

4.  **Simulation Loop and Database Contention**
    *   **Issue:** While a `DatabaseWriteQueue` background worker handles DB inserts, `ExportTableToCsv` runs synchronously and does not use the queue thread. In high-traffic environments, it may lock the `XTSFactorySim.db` during large read operations, potentially causing `SQLITE_BUSY` exceptions on the writer thread despite the retry circuit breaker.
    *   **Suggested Improvement:** Refactor export logic to read using a read-only SQLite connection, and consider isolating DB reads to prevent blocking writers. Add indexing on frequently queried columns in `SimulationDataLogger` (e.g., `PartId`, `TrackingNumber`).

5.  **Thread Safety on Simulation List Enumerations**
    *   **Issue:** `Movers`, `Robots`, and `Machines` are updated within the lock of `_simulationLock` but properties on them are accessed asynchronously by the ViewModels via event handlers. This might cause `InvalidOperationException` (collection modified during enumeration) when parts jump across subsystems in a highly concurrent scenario.
    *   **Suggested Improvement:** Use concurrent collections (e.g., `ConcurrentQueue`, `ConcurrentDictionary`) or copy snapshots before exposing to the UI.

## Missing Tests

Due to MSBuild package reference resolution issues when testing `net10.0-windows` projects with `<UseWPF>true</UseWPF>` in a containerized Linux environment, adding test projects is deferred. However, here is a list of required tests that should be implemented when the testing environment is configured:

### 1. `XTSSimulationEngine` Core Logic Tests
*   **Test_SimulationEngine_LoadNewParts:** Verify parts are generated up to the `MaxWipParts` limit when empty movers are at the `_entryLoadAngle`.
*   **Test_SimulationEngine_ProcessPrimeMoverExits:** Ensure a part's status dictates good/bad counters upon exit and it clears the mover queue properly.
*   **Test_SimulationEngine_WatchdogRecovery_Machine:** Force a machine stall by manipulating timestamps and verify `RecoverMachineStall` triggers, correctly resetting the machine and rehoming/scrapping the orphaned part.

### 2. `DatabaseWriteQueue` Threading Tests
*   **Test_DatabaseWriteQueue_Concurrency:** Enqueue thousands of mock database write actions simultaneously across multiple threads and verify the consumer queue successfully drains without throwing exceptions or race conditions.
*   **Test_DatabaseWriteQueue_GracefulShutdown:** Enqueue tasks, then immediately call `Dispose()` and assert that it allows pending operations to complete (or aborts cleanly) without hanging the disposing thread indefinitely.

### 3. `ErrorHandlingService` Reliability Tests
*   **Test_ErrorHandling_ThrottleTracker:** Log the exact same error message 10 times in one second. Verify that the `ErrorOccurred` event is only fired once.
*   **Test_ErrorHandling_ThrottleCleanup:** Ensure `_throttleTracker` reduces in size after `ThrottleCleanupInterval` has passed to validate memory leak fix.
*   **Test_ErrorHandling_CircuitBreaker_TripsAndResets:** Trigger a failing operation (e.g., mock DB error) repeatedly. Assert the circuit breaker opens after `FailureThreshold`. Fast-forward time (or mock timestamp) and verify the next attempt allows execution and resets on success.

### 4. `SimulationDataLogger` Queries
*   **Test_DataLogger_GetPartSummary:** Inject part records and test that `GetPartSummary` joins results correctly and handles parts currently in process vs. finished.

### 5. `ProductionOrchestration` Validation
*   **Test_Orchestration_ValidationRules:** Try to apply a malformed orchestration step sequence (e.g., skipping required stations or invalid loop) and confirm `TryApplyOrchestration` rejects it with appropriate error messages.