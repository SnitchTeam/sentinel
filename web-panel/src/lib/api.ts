import { getToken } from './auth'

const API_BASE = import.meta.env.VITE_API_BASE_URL || 'http://localhost:31002'

export async function apiFetch(path: string, options: RequestInit = {}) {
  const token = getToken()
  const headers = new Headers(options.headers)
  if (token) {
    headers.set('Authorization', `Bearer ${token}`)
  }
  headers.set('Content-Type', 'application/json')

  const res = await fetch(`${API_BASE}${path}`, {
    ...options,
    headers,
  })

  if (res.status === 401) {
    // Token invalid — let caller handle redirect
  }

  return res
}
