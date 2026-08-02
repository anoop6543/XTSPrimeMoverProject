import React from 'react'

interface Props { message: string }

export function AlarmBanner({ message }: Props) {
  return (
    <div style={{
      background: '#7f1d1d', color: '#fca5a5', padding: '8px 16px',
      display: 'flex', alignItems: 'center', gap: 8, fontSize: 13
    }}>
      <span style={{ fontSize: 16 }}>⚠</span>
      <strong>FAULT:</strong> {message}
    </div>
  )
}
