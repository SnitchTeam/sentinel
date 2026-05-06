let listeners: ((route: string) => void)[] = []

export function getRoute(): string {
  if (typeof window === 'undefined') return '/login'
  return window.location.pathname || '/login'
}

export function navigate(path: string) {
  if (typeof window === 'undefined') return
  window.history.pushState({}, '', path)
  window.dispatchEvent(new PopStateEvent('popstate'))
}

export function onRouteChange(cb: (route: string) => void) {
  listeners.push(cb)
  const handler = () => cb(getRoute())
  if (typeof window !== 'undefined') {
    window.addEventListener('popstate', handler)
    cb(getRoute())
  }
  return () => {
    listeners = listeners.filter((l) => l !== cb)
    if (typeof window !== 'undefined') {
      window.removeEventListener('popstate', handler)
    }
  }
}
