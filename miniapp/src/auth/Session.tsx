import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { api, login, setShopId } from '../api/client'
import type { Language, Me, Membership } from '../api/types'
import i18n from '../i18n'

interface Session {
  me: Me
  /** Membership in the shop the app is currently working in; null when the user has no shops yet. */
  shop: Membership | null
  isOwner: boolean
  switchShop(shopId: string): Promise<void>
  setLanguage(language: Language): Promise<void>
  /** Re-reads the user and shop list (after creating / leaving a shop). */
  reload(): Promise<Me>
}

const SessionContext = createContext<Session | null>(null)

export function useSession(): Session {
  const session = useContext(SessionContext)
  if (!session) throw new Error('useSession outside SessionProvider')
  return session
}

function pickShop(me: Me): Membership | null {
  return me.shops.find((s) => s.shopId === me.activeShopId) ?? me.shops[0] ?? null
}

interface Props {
  children: ReactNode
  loading: ReactNode
  error: (retry: () => void, error: unknown) => ReactNode
}

export function SessionProvider({ children, loading, error }: Props) {
  const queryClient = useQueryClient()
  const [me, setMe] = useState<Me | null>(null)
  const [failure, setFailure] = useState<unknown>(null)

  const apply = useCallback((next: Me) => {
    setShopId(pickShop(next)?.shopId ?? null)
    if (i18n.language !== next.language) void i18n.changeLanguage(next.language)
    document.documentElement.lang = next.language
    setMe(next)
  }, [])

  const signIn = useCallback(() => {
    login()
      .then(async (auth) => {
        // A reminder from the bot opens the app with ?shop=<id>: start in that shop, not in the last active one.
        const wanted = new URLSearchParams(window.location.search).get('shop')
        const user = auth.user
        if (wanted && wanted !== user.activeShopId && user.shops.some((s) => s.shopId === wanted)) {
          return apply(await api<Me>('PATCH', '/api/me', { activeShopId: wanted }))
        }
        apply(user)
      })
      .catch(setFailure)
  }, [apply])

  useEffect(signIn, [signIn])

  const retry = () => {
    setFailure(null)
    signIn()
  }

  const session = useMemo<Session | null>(() => {
    if (!me) return null
    const shop = pickShop(me)
    return {
      me,
      shop,
      isOwner: shop?.role === 'owner',
      async switchShop(shopId) {
        const next = await api<Me>('PATCH', '/api/me', { activeShopId: shopId })
        queryClient.clear()
        apply(next)
      },
      async setLanguage(language) {
        apply(await api<Me>('PATCH', '/api/me', { language }))
      },
      async reload() {
        const next = await api<Me>('GET', '/api/me')
        queryClient.clear()
        apply(next)
        return next
      },
    }
  }, [me, apply, queryClient])

  if (failure) return <>{error(retry, failure)}</>
  if (!session) return <>{loading}</>
  return <SessionContext.Provider value={session}>{children}</SessionContext.Provider>
}
