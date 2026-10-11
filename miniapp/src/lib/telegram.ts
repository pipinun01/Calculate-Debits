/** Thin typed wrapper over the official telegram-web-app.js. Everything degrades to plain browser behaviour outside Telegram. */

interface TgBackButton {
  show(): void
  hide(): void
  onClick(cb: () => void): void
  offClick(cb: () => void): void
}

interface TgWebApp {
  initData: string
  colorScheme: 'light' | 'dark'
  platform: string
  ready(): void
  expand(): void
  close(): void
  BackButton: TgBackButton
  HapticFeedback?: {
    impactOccurred(style: 'light' | 'medium' | 'heavy'): void
    notificationOccurred(type: 'success' | 'warning' | 'error'): void
    selectionChanged(): void
  }
  onEvent(event: string, cb: () => void): void
  offEvent(event: string, cb: () => void): void
  openTelegramLink(url: string): void
  openLink(url: string): void
  showConfirm?(message: string, cb: (ok: boolean) => void): void
  setHeaderColor?(color: string): void
  setBackgroundColor?(color: string): void
  setBottomBarColor?(color: string): void
  disableVerticalSwipes?(): void
  isVersionAtLeast?(version: string): boolean
}

declare global {
  interface Window {
    Telegram?: { WebApp?: TgWebApp }
  }
}

const app = (): TgWebApp | undefined => window.Telegram?.WebApp

/** True when the page really runs inside a Telegram client (the script also loads in a normal browser, with empty initData). */
export const inTelegram = (): boolean => Boolean(app()?.initData)

export const initData = (): string => app()?.initData ?? ''

export function initTelegram(onTheme: (scheme: 'light' | 'dark') => void): void {
  const tg = app()
  if (!tg || !inTelegram()) {
    onTheme(window.matchMedia?.('(prefers-color-scheme: dark)').matches ? 'dark' : 'light')
    return
  }

  tg.ready()
  tg.expand()
  try {
    tg.disableVerticalSwipes?.()
  } catch {
    /* older clients */
  }
  onTheme(tg.colorScheme)
  tg.onEvent('themeChanged', () => onTheme(tg.colorScheme))
}

/** Paints Telegram's own chrome (header, overscroll area, bottom bar) in the app's colours so the frame does not clash with the page. */
export function paintChrome(background: string, bar: string): void {
  const tg = app()
  if (!tg || !inTelegram()) return
  try {
    tg.setHeaderColor?.(background)
    tg.setBackgroundColor?.(background)
    tg.setBottomBarColor?.(bar)
  } catch {
    /* older clients */
  }
}

export function haptic(kind: 'tap' | 'success' | 'error' | 'select' = 'tap'): void {
  const h = inTelegram() ? app()?.HapticFeedback : undefined
  if (!h) return
  try {
    if (kind === 'tap') h.impactOccurred('light')
    else if (kind === 'select') h.selectionChanged()
    else h.notificationOccurred(kind)
  } catch {
    /* not supported on this client */
  }
}

export function confirmDialog(message: string): Promise<boolean> {
  const tg = app()
  if (inTelegram() && tg?.showConfirm && tg.isVersionAtLeast?.('6.2')) {
    return new Promise((resolve) => tg.showConfirm!(message, resolve))
  }
  return Promise.resolve(window.confirm(message))
}

/** Opens Telegram's "share to chat" picker with a ready message (and an optional link shown as preview). */
export function shareToChat(text: string, url?: string): void {
  const link = url
    ? `https://t.me/share/url?url=${encodeURIComponent(url)}&text=${encodeURIComponent(text)}`
    : `https://t.me/share/url?url=${encodeURIComponent(text)}`
  const tg = app()
  if (inTelegram() && tg) tg.openTelegramLink(link)
  else window.open(link, '_blank', 'noopener')
}

export function backButton(visible: boolean, onClick: () => void): () => void {
  const tg = app()
  if (!tg || !inTelegram()) return () => {}
  if (!visible) {
    tg.BackButton.hide()
    return () => {}
  }
  tg.BackButton.onClick(onClick)
  tg.BackButton.show()
  return () => {
    tg.BackButton.offClick(onClick)
  }
}

export function closeApp(): void {
  if (inTelegram()) app()?.close()
}
