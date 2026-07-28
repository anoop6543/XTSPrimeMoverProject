import React from 'react'

interface Station {
  stationId: number
  name: string
  status: string
  elapsedTime: number
  processTime: number
  currentPartTrackingNumber?: string
}

interface Props {
  stations: Station[]
  rotaryAngle: number
  isIndexing: boolean
}

export function RotaryTable({ stations, rotaryAngle, isIndexing }: Props) {
  const cx = 150, cy = 150, r = 100

  return (
    <div style={{ background: '#111827', borderRadius: 8, padding: 12 }}>
      <div style={{ fontSize: 11, color: '#6b7280', marginBottom: 8 }}>ROTARY TABLE</div>
      <svg width={300} height={300} viewBox="0 0 300 300">
        {/* Outer ring */}
        <circle cx={cx} cy={cy} r={r + 20} fill="none" stroke="#1f2937" strokeWidth={2} />
        {/* Rotating table disk */}
        <g transform={`rotate(${rotaryAngle}, ${cx}, ${cy})`}>
          <circle cx={cx} cy={cy} r={r} fill="#1a2035" stroke={isIndexing ? '#f59e0b' : '#374151'} strokeWidth={isIndexing ? 3 : 1} />
          {/* Station slots on rotary */}
          {stations.map((s, i) => {
            const angle = (i * 360) / stations.length
            const rad = (angle * Math.PI) / 180
            const sx = cx + r * 0.65 * Math.cos(rad)
            const sy = cy + r * 0.65 * Math.sin(rad)
            const color = s.status === 'Processing' ? '#3b82f6' : s.status === 'Complete' ? '#22c55e' : s.status === 'Error' ? '#ef4444' : '#374151'
            return (
              <g key={s.stationId}>
                <circle cx={sx} cy={sy} r={18} fill={color} opacity={0.9} />
                <text x={sx} y={sy + 1} textAnchor="middle" dominantBaseline="middle" fontSize={9} fill="#fff">{s.stationId}</text>
              </g>
            )
          })}
          {/* Center hub */}
          <circle cx={cx} cy={cy} r={16} fill="#374151" stroke="#4b5563" strokeWidth={2} />
          <text x={cx} y={cy + 1} textAnchor="middle" dominantBaseline="middle" fontSize={9} fill="#9ca3af">HUB</text>
        </g>
        {/* Load position indicator (fixed) */}
        <line x1={cx + r + 10} y1={cy} x2={cx + r + 28} y2={cy} stroke="#f59e0b" strokeWidth={2} strokeDasharray="4,2" />
        <text x={cx + r + 32} y={cy + 4} fontSize={9} fill="#f59e0b">LOAD</text>
      </svg>
    </div>
  )
}
