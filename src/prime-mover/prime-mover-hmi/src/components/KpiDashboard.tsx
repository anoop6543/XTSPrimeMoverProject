import React from 'react'
import type { SystemStatus } from '../signalr/systemHub'

interface Props { status: SystemStatus | null }

export function KpiDashboard({ status }: Props) {
  const oee = status && status.totalPartsProduced > 0
    ? ((status.goodPartsCount / status.totalPartsProduced) * 100).toFixed(1)
    : '0.0'

  return (
    <div style={{ background: '#0d1526', borderRadius: 8, padding: 12 }}>
      <div style={{ fontSize: 10, color: '#475569', marginBottom: 10, letterSpacing: 1 }}>PRODUCTION KPIs</div>
      <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 8 }}>
        {[
          { label: 'TOTAL', value: status?.totalPartsProduced ?? 0, color: '#60a5fa' },
          { label: 'GOOD', value: status?.goodPartsCount ?? 0, color: '#4ade80' },
          { label: 'BAD', value: status?.badPartsCount ?? 0, color: '#f87171' },
          { label: 'OEE', value: `${oee}%`, color: '#a78bfa' },
          { label: 'ENTERED', value: status?.primeMoverEnteredCount ?? 0, color: '#fbbf24' },
          { label: 'EXITED', value: status?.primeMoverExitedCount ?? 0, color: '#34d399' },
        ].map(kpi => (
          <div key={kpi.label} style={{ background: '#111827', borderRadius: 6, padding: '8px 10px' }}>
            <div style={{ fontSize: 9, color: '#6b7280' }}>{kpi.label}</div>
            <div style={{ fontSize: 22, fontWeight: 700, color: kpi.color }}>{kpi.value}</div>
          </div>
        ))}
      </div>
    </div>
  )
}
