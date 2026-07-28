# XTS Prime Mover — Temporal + Kubernetes Architecture

## Overview

This document describes the distributed microservices architecture that replaces the in-process WPF simulation engine with a fully cloud-native, Temporal-orchestrated system. Every machine runs in its own Kubernetes pod, every part's lifecycle is a live Temporal workflow instance, and all HMIs are accessible via web browsers in addition to the existing WPF desktop client.

---

## Repository Structure

```
src/
├── shared/
│   ├── Contracts/          # Shared DTOs, gRPC protos, Temporal interfaces
│   └── Database/           # PostgreSQL migration scripts
├── temporal/
│   ├── master-worker/      # Prime mover + part lifecycle Temporal worker
│   └── machine-worker/     # Machine cycle Temporal worker (one per machine)
├── machines/
│   ├── MachineService/     # Standalone .NET PLC engine (gRPC + REST)
│   ├── MachineApi/         # ASP.NET Core REST + SignalR bridge
│   └── machine-hmi/        # React TypeScript machine HMI
├── prime-mover/
│   ├── PrimeMoverService/  # XTS track engine (gRPC + REST)
│   ├── PrimeMoverApi/      # Aggregating REST API + SignalR hub
│   └── prime-mover-hmi/   # React TypeScript control room HMI
└── desktop-hmi/
    └── RemoteRestGateway.cs  # WPF REST/SignalR client (replaces LocalSimulationServiceGateway)

k8s/
├── namespace.yaml
├── temporal/               # Temporal server deployment
├── prime-mover/            # Prime mover 3-container pod
├── machines/               # Helm chart for machine pods (×4)
├── monitoring/             # Prometheus, Grafana, Jaeger
└── ingress/                # NGINX ingress rules

docker/
├── *.Dockerfile            # Per-service Dockerfiles
└── docker-compose.dev.yml  # Full local dev stack
```

---

## Temporal Workflow Architecture

### Core Innovation: Every Part is a Living Workflow

When a part enters the XTS track at the entry zone, a `PartLifecycleWorkflow` is spawned in Temporal. This workflow:

- Carries the complete part state (status, route, defect history) as workflow-internal state
- Receives Temporal Signals at each lifecycle event (mover assigned, loaded to machine, station complete, exited)
- Can be queried synchronously for the current part status — no database round-trip needed
- Temporal's event history IS the audit trail — no separate `PartEvents` table required
- On machine fault: Temporal retry policy drives the recovery (replaces custom watchdog escalation)

```
PartLifecycleWorkflow("PART-000042")
  Signal: MoverAssigned(moverId=3)
  Signal: LoadedToMachine(machineId=0)
  Signal: StationComplete(stationId=0, "Pre-Heat", hadDefect=false)
  Signal: StationComplete(stationId=1, "Laser Weld", hadDefect=false)
  Signal: StationComplete(stationId=2, "Cool Down", hadDefect=false)
  Signal: StationComplete(stationId=3, "Weld Inspection", hadDefect=false)
  Signal: UnloadedFromMachine(machineId=0, hasDefect=false, status="BaseLayer")
  Signal: LoadedToMachine(machineId=1)
  ...
  Signal: Exited(good=true)
  → Result: { TrackingNumber: "PART-000042", Good: true, CycleTime: 47.3s }
```

### Workflow Types

| Workflow | Instances | Lifetime | Purpose |
|---|---|---|---|
| `XTSPrimeMoverWorkflow` | 1 (global) | Long-running (weeks) | Track engine orchestration, mover routing |
| `PartLifecycleWorkflow` | 1 per part (~6 concurrent) | ~60s (one cycle) | Part audit trail, state machine |
| `MachineCycleWorkflow` | 1 per machine (4 total) | Always running | Station sequencing, fault handling |
| `RobotTransferWorkflow` | 1 per transfer (transient) | ~3s | Robot pick-and-place coordination |

### Machine Cycle — Watchdog via Temporal

The original custom `_machineWatchSeconds` dictionary is replaced entirely by Temporal activity timeouts:

```csharp
// OLD: Custom watchdog in XTSSimulationEngine
_machineWatchSeconds[machineId] += deltaTime;
if (_machineWatchSeconds[machineId] > MachineStallThresholdSeconds) { /* escalate */ }

// NEW: Temporal handles timeouts declaratively
await Workflow.ExecuteActivityAsync(
    a => a.ExecuteStationSequenceAsync(...),
    new ActivityOptions {
        StartToCloseTimeout = TimeSpan.FromSeconds(120),
        RetryPolicy = new RetryPolicy { MaximumAttempts = 3, InitialInterval = TimeSpan.FromSeconds(2) }
    });
// If activity times out → Temporal automatically retries, then raises ApplicationFailure
// MachineCycleWorkflow catches the failure → sets fault state → waits for ResetFault signal
```

---

## Machine Pod Architecture

Each of the 4 machines runs as a Kubernetes pod with 4 containers sharing a network namespace:

```
Pod: machine-laser-welding
├── Container: machine-service  (.NET, port 8080 REST + 9090 gRPC)
│   └── MachinePlcEngine (station timer loop, sequencer states)
├── Container: machine-api      (.NET ASP.NET Core, port 8081)
│   └── REST endpoints + SignalR hub + Temporal workflow proxy
├── Container: machine-worker   (.NET Temporal worker)
│   └── MachineCycleWorkflow + MachineActivities
└── Container: machine-hmi      (nginx + React, port 80)
    └── Rotary table SVG, station cards, ET/PT bars, alarm banner
```

All containers in the same pod communicate via `localhost` — no network hop.

---

## Communication Matrix

| From → To | Channel | Protocol | Latency |
|---|---|---|---|
| Prime Mover → Machine | Temporal Signal | TCP/gRPC | ~10ms |
| Machine → Prime Mover | Temporal Signal | TCP/gRPC | ~10ms |
| Machine Service → Machine API | localhost | HTTP | <1ms |
| Prime Mover API → Machine APIs | REST | HTTP | ~5ms |
| HMI Browser → API | SignalR | WebSocket | ~5ms |
| WPF Desktop → API | REST + SignalR | HTTP + WS | ~10ms |
| Services → Observability | Prometheus scrape | HTTP | async |
| Cross-pod alarms | Redis Pub/Sub | TCP | ~2ms |
| Machine sensors | MQTT | TCP | ~5ms |

---

## Running Locally (Docker Compose)

```bash
# Start full stack
cd docker
docker compose -f docker-compose.dev.yml up -d

# Access points:
# Main HMI:         http://localhost:3000
# Machine 0 HMI:    http://localhost:3001?machineId=0
# Machine 1 HMI:    http://localhost:3002?machineId=1
# Machine 2 HMI:    http://localhost:3003?machineId=2
# Machine 3 HMI:    http://localhost:3004?machineId=3
# Prime Mover API:  http://localhost:8082/swagger
# Machine 0 API:    http://localhost:8091/swagger
# Temporal UI:      http://localhost:8088
# Grafana:          http://localhost:3100  (admin / xts_grafana)
# Prometheus:       http://localhost:9999
# Jaeger:           http://localhost:16686
```

---

## Deploying to Kubernetes

```bash
# Create namespace
kubectl apply -f k8s/namespace.yaml

# Deploy infrastructure
kubectl apply -f k8s/temporal/temporal-deployment.yaml

# Deploy prime mover
kubectl apply -f k8s/prime-mover/prime-mover-deployment.yaml

# Deploy machines via Helm (one chart, 4 releases)
helm install machine-laser-welding k8s/machines/helm -f k8s/machines/values-laser-welding.yaml -n xts-system
helm install machine-precision-asm k8s/machines/helm -f k8s/machines/values-precision-assembly.yaml -n xts-system
helm install machine-quality-insp k8s/machines/helm -f k8s/machines/values-quality-inspection.yaml -n xts-system
helm install machine-func-testing k8s/machines/helm -f k8s/machines/values-functional-testing.yaml -n xts-system

# Deploy observability
kubectl apply -f k8s/monitoring/
kubectl apply -f k8s/ingress/nginx-ingress.yaml
```

---

## WPF Desktop App in Remote Mode

The existing WPF application is preserved. To switch to remote mode, construct `RemoteRestGateway` instead of `LocalSimulationServiceGateway` in `App.xaml.cs`:

```csharp
// Local mode (existing behavior — engine runs in-process):
var gateway = new LocalSimulationServiceGateway(new XTSSimulationEngine());

// Remote mode (engine runs in Kubernetes):
var gateway = new RemoteRestGateway("http://api.xts.local");
```

All ViewModels, XAML bindings, and orchestration commands work identically. Only the gateway implementation differs.

---

## Key Design Decisions

### Why Temporal?
- **Durability**: Workflow state survives pod restarts, network partitions, and process crashes
- **Traceability**: Temporal's event history replaces SQLite `PartEvents` — every signal is an immutable record
- **Timeout management**: Activity timeouts replace all custom watchdog dictionaries — the runtime handles escalation
- **Visibility**: Temporal Web UI shows every active workflow, its history, and pending signals in real-time

### Why 3-container pods?
- Service, API, and HMI containers in the same pod communicate via `localhost` (zero network latency)
- Independent scaling: the HMI container can be updated without restarting the PLC engine container
- Resource isolation: each container has its own limits/requests
- Rolling updates: Kubernetes updates containers independently within the pod

### Why Helm for machines?
- All 4 machines share identical deployment structure — only `machine.id`, `machine.type`, and `machine.name` differ
- A single chart parameterizes all differences via `values-*.yaml`
- Adding a 5th machine requires only a new `values-machine4.yaml` + `helm install`

---

## Threading Model

| Component | Threading |
|---|---|
| `MachinePlcEngine` | System.Threading.Timer (background thread), lock-protected state |
| `XtsTrackEngine` | System.Threading.Timer (background thread), lock-protected state |
| `MachineApi` SignalR broadcaster | `BackgroundService` (hosted service thread) |
| `PrimeMoverApi` SignalR broadcaster | `BackgroundService` (hosted service thread) |
| Temporal workflows | Temporalio SDK event loop (deterministic, single-threaded per workflow) |
| Temporal activities | Thread pool (non-deterministic, can do I/O) |
| WPF `RemoteRestGateway` | SignalR events dispatched to calling thread; commands fire-and-forget on thread pool |

---

*Last updated: 2026-07-28 | Architecture version: 2.0 (Temporal + Kubernetes)*
