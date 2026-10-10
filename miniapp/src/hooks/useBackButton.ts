import { useEffect } from 'react'
import { useNavigate } from 'react-router-dom'
import { backButton } from '../lib/telegram'

/** Shows Telegram's native back button on inner screens; it goes one step back in history. */
export function useBackButton(visible: boolean): void {
  const navigate = useNavigate()
  useEffect(() => backButton(visible, () => navigate(-1)), [visible, navigate])
}
