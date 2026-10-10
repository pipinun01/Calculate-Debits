import { useCallback, useEffect, useRef, useState } from 'react'

const MIME_CANDIDATES = ['audio/webm;codecs=opus', 'audio/webm', 'audio/mp4', 'audio/ogg;codecs=opus']

function pickMime(): string | undefined {
  if (typeof MediaRecorder === 'undefined') return undefined
  return MIME_CANDIDATES.find((m) => MediaRecorder.isTypeSupported(m))
}

export const recorderSupported = (): boolean =>
  typeof MediaRecorder !== 'undefined' && Boolean(navigator.mediaDevices?.getUserMedia) && pickMime() !== undefined

export interface Recording {
  blob: Blob
  fileName: string
}

/** Microphone recording through MediaRecorder; stops by itself after `maxSeconds`. */
export function useRecorder(maxSeconds: number, onDone: (recording: Recording) => void, onError: (error: unknown) => void) {
  const [recording, setRecording] = useState(false)
  const recorder = useRef<MediaRecorder | null>(null)
  const timer = useRef<number | undefined>(undefined)
  const callbacks = useRef({ onDone, onError })
  useEffect(() => {
    callbacks.current = { onDone, onError }
  })

  const stop = useCallback(() => {
    window.clearTimeout(timer.current)
    if (recorder.current?.state === 'recording') recorder.current.stop()
  }, [])

  const start = useCallback(async () => {
    try {
      const mimeType = pickMime()
      const stream = await navigator.mediaDevices.getUserMedia({ audio: true })
      const chunks: Blob[] = []
      const rec = new MediaRecorder(stream, mimeType ? { mimeType } : undefined)
      rec.ondataavailable = (e) => e.data.size > 0 && chunks.push(e.data)
      rec.onstop = () => {
        stream.getTracks().forEach((track) => track.stop())
        setRecording(false)
        const type = rec.mimeType || mimeType || 'audio/webm'
        const ext = type.includes('mp4') ? 'm4a' : type.includes('ogg') ? 'ogg' : 'webm'
        const blob = new Blob(chunks, { type })
        if (blob.size > 0) callbacks.current.onDone({ blob, fileName: `voice.${ext}` })
      }
      recorder.current = rec
      rec.start()
      setRecording(true)
      timer.current = window.setTimeout(stop, maxSeconds * 1000)
    } catch (error) {
      setRecording(false)
      callbacks.current.onError(error)
    }
  }, [maxSeconds, stop])

  useEffect(() => stop, [stop])

  return { recording, start, stop }
}
