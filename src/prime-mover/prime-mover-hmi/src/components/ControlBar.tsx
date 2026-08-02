import React from 'react'

interface Props {
  isRunning: boolean
  speed: number
  onSpeedChange: (s: number) => void
}

export function ControlBar({ isRunning, speed, onSpeedChange }: Props) {
  const post = (path: string, body?: unknown) =>
    fetch(path, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: body ? JSON.stringify(body) : undefined })

  return (
    <div style={{
      display: 'flex', alignItems: 'center', gap: 12, padding: '8px 16px',
      background: '#0a1020', borderBottom: '1px solid #1e293b'
    }}>
      <button
        onClick={() => post(isRunning ? '/api/system/stop' : '/api/system/start')}
        style={{
          padding: '5px 16px', borderRadius: 4, border: 'none', cursor: 'pointer', fontWeight: 700, fontSize: 12,
          background: isRunning ? '#7f1d1d' : '#065f46', color: '#fff'
        }}
      >
        {isRunning ? '⏹ STOP' : '▶ START'}
      </button>

      <button
        onClick={() => post('/api/system/reset')}
        style={{ padding: '5px 12px', borderRadius: 4, border: '1px solid #374151', cursor: 'pointer', background: '#1f2937', color: '#9ca3af', fontSize: 12 }}
      >
        ↺ RESET
      </button>

      <div style={{ display: 'flex', alignItems: 'center', gap: 8, marginLeft: 'auto' }}>
        <span style={{ fontSize: 11, color: '#475569' }}>SPEED</span>
        <input
          type="range" min={0.1} max={5} step={0.1} value={speed}
          onChange={e => {
            const v = parseFloat(e.target.value)
            onSpeedChange(v)
            post(`/api/system/set-speed?factor=${v}`)
          }}
          style={{ width: 100 }}
        />
        <span style={{ fontSize: 11, color: '#60a5fa', minWidth: 30 }}>{speed.toFixed(1)}×</span>
      </div>
    </div>
  )
}
