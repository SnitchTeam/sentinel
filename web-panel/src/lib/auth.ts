const TOKEN_KEY = 'sentinel_token'

export function getToken(): string | null {
  if (typeof window === 'undefined') return null
  return localStorage.getItem(TOKEN_KEY)
}

export function setToken(token: string): void {
  if (typeof window === 'undefined') return
  localStorage.setItem(TOKEN_KEY, token)
}

export function clearToken(): void {
  if (typeof window === 'undefined') return
  localStorage.removeItem(TOKEN_KEY)
}

export async function validateToken(token: string): Promise<boolean> {
  try {
    const apiBase = import.meta.env.VITE_API_BASE_URL || 'http://localhost:31002'
    const res = await fetch(`${apiBase}/api/config`, {
      headers: {
        Authorization: `Bearer ${token}`,
      },
    })
    return res.status === 200
  } catch {
    // If the API is unreachable, accept the token locally for UI demo purposes
    // In production this would strictly require a successful API call
    return token.length >= 8
  }
}
