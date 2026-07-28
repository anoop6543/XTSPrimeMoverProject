import React from 'react'

interface MachineDto {
  machineId: number
  name: string
  type: string
  sequencerState: string
  isOperational: boolean
  faultActive: boolean
  faultMessage: string
  partsEnteredCount: number
  partsExitedCount: number
  currentStationIndex: number
  isIndexing: boolean
}

interface Props { machineId: number; machine?: MachineDto }

const MACHINE_NAMES = ['Laser Welding', 'Precision Assembly', 'Quality Inspection', 'Functional Testing']

export function MachineMiniHmi({ machineId, machine }: Props) {
  const stateColor = !machine ? '#374151' : machine.faultActive ? '#ef4444' : machine.sequencerState === 'Run' ? '#3b82f6' : '#22c55e'

  return (
    <div style={{
      background: '#111827',
      border: `1px solid ${stateColor}`,
      borderRadius: 6, padding: '8px 10px', marginBottom: 8
    }}>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 4 }}>
        <span style={{ fontSize: 11, fontWeight: 600, color: '#e2e8f0' }}>
          M{machineId} — {MACHINE_NAMES[machineId]}
        </span>
        <span style={{ fontSize: 9, color: stateColor, fontWeight: 700 }}>
          {machine?.sequencerState ?? 'OFFLINE'}
        </span>
      </div>
      {machine ? (
        <div style={{ display: 'flex', gap: 10, fontSize: 10, color: '#6b7280' }}>
          <span>IN:{machine.partsEnteredCount}</span>
          <span>OUT:{machine.partsExitedCount}</span>
          <span>STA:{machine.currentStationIndex}</span>
          {machine.isIndexing && <span style={{ color: '#f59e0b' }}>↻</span>}
        </div>
      ) : (
        <div style={{ fontSize: 10, color: '#6b7280' }}>Connecting…</div>
      )}
      {machine?.faultActive && (
        <div style={{ fontSize: 9, color: '#fca5a5', marginTop: 3 }}>⚠ {machine.faultMessage}</div>
      )}
      {/* Link to full machine HMI */}
      <a href={`/machine/${machineId}`} target="_blank" rel="noreferrer"
        style={{ display: 'block', fontSize: 9, color: '#3b82f6', marginTop: 4, textDecoration: 'none' }}>
        Open Full HMI →
      </a>
    </div>
  )
}
