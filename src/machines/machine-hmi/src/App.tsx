import React from 'react'
import { useMachineSignalR } from './signalr/machineHub'
import { RotaryTable } from './components/RotaryTable'
import { StationCard } from './components/StationCard'
import { AlarmBanner } from './components/AlarmBanner'
import { KpiBar } from './components/KpiBar'

const MACHINE_ID = parseInt(new URLSearchParams(window.location.search).get('machineId') ?? '0')

export default function App() {
  const { status, connected } = useMachineSignalR(MACHINE_ID)

  return (
    <div style={styles.root}>
      <header style={styles.header}>
        <span style={styles.machineTag}>M{MACHINE_ID}</span>
        <span style={styles.machineName}>{status?.name ?? `Machine-${MACHINE_ID}`}</span>
        <span style={styles.stateTag(status?.sequencerState ?? 'INIT')}>{status?.sequencerState ?? 'INIT'}</span>
        <span style={{ marginLeft: 'auto', fontSize: 11, color: connected ? '#4ade80' : '#f87171' }}>
          ● {connected ? 'LIVE' : 'OFFLINE'}
        </span>
      </header>

      {status?.faultActive && <AlarmBanner message={status.faultMessage} />}

      <div style={styles.body}>
        <div style={styles.leftPanel}>
          <RotaryTable stations={status?.stations ?? []} rotaryAngle={status?.rotaryAngle ?? 0} isIndexing={status?.isIndexing ?? false} />
        </div>
        <div style={styles.rightPanel}>
          <KpiBar entered={status?.partsEnteredCount ?? 0} exited={status?.partsExitedCount ?? 0} />
          <div style={styles.stationGrid}>
            {(status?.stations ?? []).map(s => (
              <StationCard key={s.stationId} station={s} />
            ))}
          </div>
        </div>
      </div>
    </div>
  )
}

const styles = {
  root: { minHeight: '100vh', display: 'flex', flexDirection: 'column' as const },
  header: {
    display: 'flex', alignItems: 'center', gap: 12, padding: '8px 16px',
    background: '#111827', borderBottom: '1px solid #1f2937'
  },
  machineTag: {
    background: '#1d4ed8', color: '#fff', borderRadius: 4,
    padding: '2px 8px', fontSize: 13, fontWeight: 700
  },
  machineName: { fontSize: 15, fontWeight: 600, color: '#e2e8f0' },
  stateTag: (state: string) => ({
    fontSize: 11, padding: '2px 8px', borderRadius: 3, fontWeight: 700,
    background: state === 'Run' ? '#065f46' : state === 'Fault' ? '#7f1d1d' : state === 'Ready' ? '#1e3a5f' : '#374151',
    color: '#fff'
  }),
  body: { flex: 1, display: 'flex', gap: 16, padding: 16 },
  leftPanel: { width: 320 },
  rightPanel: { flex: 1, display: 'flex', flexDirection: 'column' as const, gap: 12 },
  stationGrid: { display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(200px, 1fr))', gap: 10 }
}
