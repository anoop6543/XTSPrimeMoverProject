import React from 'react'

interface Props { entered: number; exited: number }

export function KpiBar({ entered, exited }: Props) {
  return (
    <div style={{ display: 'flex', gap: 16, background: '#111827', borderRadius: 6, padding: '8px 12px' }}>
      <div>
        <div style={{ fontSize: 10, color: '#6b7280' }}>PARTS IN</div>
        <div style={{ fontSize: 20, fontWeight: 700, color: '#60a5fa' }}>{entered}</div>
      </div>
      <div>
        <div style={{ fontSize: 10, color: '#6b7280' }}>PARTS OUT</div>
        <div style={{ fontSize: 20, fontWeight: 700, color: '#4ade80' }}>{exited}</div>
      </div>
      <div>
        <div style={{ fontSize: 10, color: '#6b7280' }}>EFFICIENCY</div>
        <div style={{ fontSize: 20, fontWeight: 700, color: '#a78bfa' }}>
          {entered > 0 ? ((exited / entered) * 100).toFixed(0) : 0}%
        </div>
      </div>
    </div>
  )
}
