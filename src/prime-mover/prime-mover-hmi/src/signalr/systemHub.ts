import * as signalR from '@microsoft/signalr'
import { useState, useEffect } from 'react'

export interface MoverDto {
  moverId: number
  position: number
  velocity: number
  state: string
  loadedPartTrackingNumber?: string
}

export interface StationDto {
  stationId: number
  name: string
  type: string
  status: string
  processTime: number
  elapsedTime: number
  currentPartTrackingNumber?: string
}

export interface MachineDto {
  machineId: number
  name: string
  type: string
  sequencerState: string
  isOperational: boolean
  faultActive: boolean
  faultMessage: string
  partsEnteredCount: number
  partsExitedCount: number
  stations: StationDto[]
  currentStationIndex: number
  isIndexing: boolean
  rotaryAngle: number
}

export interface SystemStatus {
  isRunning: boolean
  totalPartsProduced: number
  goodPartsCount: number
  badPartsCount: number
  primeMoverEnteredCount: number
  primeMoverExitedCount: number
  movers: MoverDto[]
  machines: MachineDto[]
  timestamp: string
}

export function useSystemSignalR() {
  const [status, setStatus] = useState<SystemStatus | null>(null)
  const [connected, setConnected] = useState(false)

  useEffect(() => {
    const hubUrl = import.meta.env.VITE_SYSTEM_HUB_URL ?? '/hubs/system'
    const connection = new signalR.HubConnectionBuilder()
      .withUrl(hubUrl)
      .withAutomaticReconnect()
      .build()

    connection.on('SystemStatusUpdate', (data: SystemStatus) => setStatus(data))
    connection.onclose(() => setConnected(false))
    connection.onreconnecting(() => setConnected(false))
    connection.onreconnected(() => setConnected(true))

    connection.start()
      .then(() => setConnected(true))
      .catch(err => console.error('SignalR error:', err))

    // Poll REST as fallback at 2s
    const pollInterval = setInterval(async () => {
      try {
        const resp = await fetch('/api/system/status')
        if (resp.ok) {
          const data = await resp.json()
          setStatus(data)
        }
      } catch { /* ignore */ }
    }, 2000)

    return () => {
      clearInterval(pollInterval)
      connection.stop()
    }
  }, [])

  return { status, connected }
}
