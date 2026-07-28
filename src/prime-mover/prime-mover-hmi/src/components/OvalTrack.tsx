import React from 'react'

interface MoverDto {
  moverId: number
  position: number
  velocity: number
  state: string
  loadedPartTrackingNumber?: string
}

interface MachineDto {
  machineId: number
  name: string
  sequencerState: string
  faultActive: boolean
}

interface Props {
  movers: MoverDto[]
  machines: MachineDto[]
}

const TRACK_W = 580
const TRACK_H = 320
const CX = TRACK_W / 2
const CY = TRACK_H / 2
const RX = 240
const RY = 120

function polarToXY(angleDeg: number): { x: number; y: number } {
  const rad = ((angleDeg - 90) * Math.PI) / 180
  return {
    x: CX + RX * Math.cos(rad),
    y: CY + RY * Math.sin(rad)
  }
}

const MACHINE_ANGLES = [45, 135, 225, 315]
const MACHINE_LABELS = ['M0 Laser', 'M1 Assembly', 'M2 Quality', 'M3 Test']
const ENTRY_ANGLE = 205
const EXIT_ANGLE = 0

export function OvalTrack({ movers, machines }: Props) {
  return (
    <div style={{ background: '#0d1526', borderRadius: 10, padding: 12 }}>
      <div style={{ fontSize: 10, color: '#475569', marginBottom: 8, letterSpacing: 1 }}>XTS OVAL TRACK</div>
      <svg width={TRACK_W} height={TRACK_H} viewBox={`0 0 ${TRACK_W} ${TRACK_H}`}>
        {/* Track lanes */}
        <ellipse cx={CX} cy={CY} rx={RX + 18} ry={RY + 18} fill="none" stroke="#1e3a5f" strokeWidth={36} />
        <ellipse cx={CX} cy={CY} rx={RX} ry={RY} fill="none" stroke="#0f2040" strokeWidth={2} />

        {/* Entry zone */}
        {(() => { const p = polarToXY(ENTRY_ANGLE); return <circle cx={p.x} cy={p.y} r={14} fill="#065f46" stroke="#4ade80" strokeWidth={1.5} /> })()}
        {(() => { const p = polarToXY(ENTRY_ANGLE); return <text x={p.x} y={p.y + 24} textAnchor="middle" fontSize={8} fill="#4ade80">ENTRY</text> })()}

        {/* Exit zone */}
        {(() => { const p = polarToXY(EXIT_ANGLE); return <circle cx={p.x} cy={p.y} r={14} fill="#7f1d1d" stroke="#f87171" strokeWidth={1.5} /> })()}
        {(() => { const p = polarToXY(EXIT_ANGLE); return <text x={p.x} y={p.y + 24} textAnchor="middle" fontSize={8} fill="#f87171">EXIT</text> })()}

        {/* Machine stations */}
        {MACHINE_ANGLES.map((angle, i) => {
          const p = polarToXY(angle)
          const m = machines[i]
          const color = !m ? '#374151' : m.faultActive ? '#7f1d1d' : m.sequencerState === 'Run' ? '#1e3a5f' : '#065f46'
          const borderColor = !m ? '#4b5563' : m.faultActive ? '#ef4444' : m.sequencerState === 'Run' ? '#3b82f6' : '#22c55e'
          return (
            <g key={i}>
              <rect x={p.x - 36} y={p.y - 18} width={72} height={36} rx={4} fill={color} stroke={borderColor} strokeWidth={1.5} />
              <text x={p.x} y={p.y - 4} textAnchor="middle" fontSize={8} fill="#e2e8f0" fontWeight={700}>{MACHINE_LABELS[i]}</text>
              <text x={p.x} y={p.y + 8} textAnchor="middle" fontSize={7} fill={borderColor}>{m?.sequencerState ?? 'OFFLINE'}</text>
            </g>
          )
        })}

        {/* Movers */}
        {movers.map(m => {
          const p = polarToXY(m.position)
          const loaded = !!m.loadedPartTrackingNumber
          const color = m.state === 'AtMachine' ? '#7c3aed' : loaded ? '#1d4ed8' : '#374151'
          return (
            <g key={m.moverId}>
              <circle cx={p.x} cy={p.y} r={9} fill={color} stroke={loaded ? '#60a5fa' : '#6b7280'} strokeWidth={1.5} />
              <text x={p.x} y={p.y + 1} textAnchor="middle" dominantBaseline="middle" fontSize={7} fill="#fff" fontWeight={700}>
                {m.moverId}
              </text>
              {loaded && <circle cx={p.x + 7} cy={p.y - 7} r={4} fill="#f59e0b" />}
            </g>
          )
        })}
      </svg>

      {/* Legend */}
      <div style={{ display: 'flex', gap: 16, marginTop: 8, fontSize: 10, color: '#475569' }}>
        <span><span style={{ color: '#374151' }}>● </span>Empty</span>
        <span><span style={{ color: '#1d4ed8' }}>● </span>Loaded</span>
        <span><span style={{ color: '#7c3aed' }}>● </span>At Machine</span>
        <span><span style={{ color: '#f59e0b' }}>◆ </span>Part</span>
      </div>
    </div>
  )
}
