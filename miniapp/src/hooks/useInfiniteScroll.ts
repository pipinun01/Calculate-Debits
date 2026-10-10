import { useEffect, useRef } from 'react'

/** Calls `loadMore` when the returned sentinel element scrolls into view. */
export function useInfiniteScroll<T extends HTMLElement>(enabled: boolean, loadMore: () => void) {
  const ref = useRef<T | null>(null)
  useEffect(() => {
    const el = ref.current
    if (!el || !enabled || typeof IntersectionObserver === 'undefined') return
    const observer = new IntersectionObserver((entries) => entries[0]?.isIntersecting && loadMore(), { rootMargin: '300px' })
    observer.observe(el)
    return () => observer.disconnect()
  }, [enabled, loadMore])
  return ref
}
