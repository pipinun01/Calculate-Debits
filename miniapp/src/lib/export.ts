import { api, download, query } from '../api/client'
import { inTelegram } from './telegram'

/**
 * Gets an Excel file to the user. Inside Telegram the bot sends it to the chat (a webview cannot save files reliably,
 * and a file in the chat is easy to forward); in a plain browser it is downloaded.
 * `path` is the GET endpoint; its `/send` twin does the chat delivery.
 */
export async function exportFile(path: string, period?: { from: string; to: string }): Promise<'sent' | 'downloaded'> {
  if (inTelegram()) {
    await api<void>('POST', `${path}/send`, period)
    return 'sent'
  }

  const { blob, fileName } = await download(path + query(period ?? {}))
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = url
  link.download = fileName
  document.body.appendChild(link)
  link.click()
  link.remove()
  window.setTimeout(() => URL.revokeObjectURL(url), 1000)
  return 'downloaded'
}
