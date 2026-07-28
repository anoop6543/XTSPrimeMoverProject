import React, { useState } from 'react'
import { OvalTrack } from './components/OvalTrack'
import { KpiDashboard } from './components/KpiDashboard'
import { MachineMiniHmi } from './components/MachineMiniHmi'
import { ControlBar } from './components/ControlBar'
import { useSystemSignalR } from './signalr/systemHub'

export default function App() {
  const { status, connected } = useSystemSignalR()
  const [speed, setSpeed] = useState(1.0)

  return (
    <div style={styles.root}>
      <header style={styles.header}>
        <span style={styles.logo}>⚙ XTS PRIME MOVER</span>
        <span style={styles.subtitle}>Temporal Distributed Control Room</span>
        <span style={{ marginLeft: 'auto', fontSize: 11, color: connected ? '#4ade80' : '#f87171' }}>
          ● {connected ? 'LIVE' : 'RECONNECTING'}
        </span>
      </header>

      <ControlBar isRunning={status?.isRunning ?? false} speed={speed} onSpeedChange={setSpeed} />

      <div style={styles.main}>
        <div style={styles.trackSection}>
          <OvalTrack
            movers={status?.movers ?? []}
            machines={status?.machines ?? []}
          />
        </div>
        <div style={styles.sidePanel}>
          <KpiDashboard status={status} />
          <div style={{ marginTop: 12 }}>
            <div style={styles.sectionLabel}>MACHINE PANELS</div>
            {Array.from({ length: 4 }, (_, i) => (
              <MachineMiniHmi key={i} machineId={i} machine={(status?.machines ?? [])[i]} />
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
    display: 'flex', alignItems: 'center', gap: 12, padding: '10px 20px',
    background: '#0d1526', borderBottom: '1px solid #1e293b'
  },
  logo: { fontSize: 16, fontWeight: 700, color: '#60a5fa', letterSpacing: 1 },
  subtitle: { fontSize: 11, color: '#475569' },
  main: { flex: 1, display: 'flex', gap: 0 },
  trackSection: { flex: 1, padding: 16 },
  sidePanel: { width: 320, padding: 16, background: '#0a1020', borderLeft: '1px solid #1e293b', overflowY: 'auto' as const },
  sectionLabel: { fontSize: 10, color: '#475569', marginBottom: 8, letterSpacing: 1 }
}
