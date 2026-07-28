import * as signalR from '@microsoft/signalr'
import { useState, useEffect } from 'react'

export interface StationDto {
  stationId: number
  name: string
  type: string
  status: string
  processTime: number
  elapsedTime: number
  currentPartTrackingNumber?: string
}

export interface MachineStatus {
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

export function useMachineSignalR(machineId: number) {
  const [status, setStatus] = useState<MachineStatus | null>(null)
  const [connected, setConnected] = useState(false)

  useEffect(() => {
    const hubUrl = import.meta.env.VITE_MACHINE_HUB_URL ?? '/hubs/machine'
    const connection = new signalR.HubConnectionBuilder()
      .withUrl(hubUrl)
      .withAutomaticReconnect()
      .build()

    connection.on('MachineStatusUpdate', (data: MachineStatus) => setStatus(data))

    connection.onclose(() => setConnected(false))
    connection.onreconnecting(() => setConnected(false))
    connection.onreconnected(() => {
      setConnected(true)
      connection.invoke('JoinMachineGroup', machineId).catch(console.error)
    })

    connection.start()
      .then(() => {
        setConnected(true)
        return connection.invoke('JoinMachineGroup', machineId)
      })
      .catch(err => console.error('SignalR connection error:', err))

    // Also poll REST as fallback
    const pollInterval = setInterval(async () => {
      try {
        const resp = await fetch(`/machine/${machineId}/status`)
        if (resp.ok) setStatus(await resp.json())
      } catch { /* ignore */ }
    }, 2000)

    return () => {
      clearInterval(pollInterval)
      connection.stop()
    }
  }, [machineId])

  return { status, connected }
}
