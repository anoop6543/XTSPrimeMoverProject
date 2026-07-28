import React from 'react'

interface Station {
  stationId: number
  name: string
  type: string
  status: string
  processTime: number
  elapsedTime: number
  currentPartTrackingNumber?: string
}

interface Props { station: Station }

export function StationCard({ station }: Props) {
  const pct = station.processTime > 0 ? Math.min(100, (station.elapsedTime / station.processTime) * 100) : 0
  const statusColor = station.status === 'Processing' ? '#3b82f6' : station.status === 'Complete' ? '#22c55e' : station.status === 'Error' ? '#ef4444' : '#4b5563'

  return (
    <div style={{ background: '#111827', border: `1px solid ${statusColor}`, borderRadius: 6, padding: 10 }}>
      <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: 6 }}>
        <span style={{ fontSize: 12, fontWeight: 600, color: '#e2e8f0' }}>{station.name}</span>
        <span style={{ fontSize: 10, color: statusColor, fontWeight: 700 }}>{station.status.toUpperCase()}</span>
      </div>
      <div style={{ fontSize: 10, color: '#6b7280', marginBottom: 6 }}>{station.type}</div>
      {/* ET/PT bar */}
      <div style={{ background: '#1f2937', borderRadius: 2, height: 4, marginBottom: 4 }}>
        <div style={{ background: statusColor, borderRadius: 2, height: 4, width: `${pct}%`, transition: 'width 0.2s' }} />
      </div>
      <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: 9, color: '#6b7280' }}>
        <span>ET {station.elapsedTime.toFixed(1)}s</span>
        <span>PT {station.processTime.toFixed(1)}s</span>
      </div>
      {station.currentPartTrackingNumber && (
        <div style={{ fontSize: 9, color: '#60a5fa', marginTop: 4 }}>↳ {station.currentPartTrackingNumber}</div>
      )}
    </div>
  )
}
