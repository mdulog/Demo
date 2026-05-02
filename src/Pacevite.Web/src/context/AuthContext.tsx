import { createContext, useEffect, useState, type ReactNode } from 'react'
import { apiClient, setLogoutCallback, tokenStore } from '@/lib/api'

interface AuthState {
  userId: string
  email: string
}

interface AuthContextValue {
  user: AuthState | null
  isAuthenticated: boolean
  login: (userId: string, email: string, token: string) => void
  logout: () => Promise<void>
}

export const AuthContext = createContext<AuthContextValue | null>(null)

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<AuthState | null>(null)

  // Register the force-logout callback used by the Axios interceptor when
  // a silent refresh fails (expired or revoked refresh token).
  useEffect(() => {
    setLogoutCallback(() => {
      tokenStore.clear()
      setUser(null)
    })
    return () => setLogoutCallback(() => {})
  }, [])

  function login(userId: string, email: string, token: string) {
    tokenStore.set(token)
    setUser({ userId, email })
  }

  async function logout() {
    try {
      await apiClient.post('/auth/logout')
    } catch {
      // Network error or already-expired access token — clear client state regardless
    } finally {
      tokenStore.clear()
      setUser(null)
    }
  }

  return (
    <AuthContext.Provider value={{ user, isAuthenticated: user !== null, login, logout }}>
      {children}
    </AuthContext.Provider>
  )
}
