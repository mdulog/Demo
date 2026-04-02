import { createContext, useState, type ReactNode } from 'react'
import { tokenStore } from '@/lib/api'

interface AuthState {
  userId: string
  email: string
}

interface AuthContextValue {
  user: AuthState | null
  isAuthenticated: boolean
  login: (userId: string, email: string, token: string) => void
  logout: () => void
}

export const AuthContext = createContext<AuthContextValue | null>(null)

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<AuthState | null>(null)

  function login(userId: string, email: string, token: string) {
    tokenStore.set(token)
    setUser({ userId, email })
  }

  function logout() {
    tokenStore.clear()
    setUser(null)
  }

  return (
    <AuthContext.Provider value={{ user, isAuthenticated: user !== null, login, logout }}>
      {children}
    </AuthContext.Provider>
  )
}
