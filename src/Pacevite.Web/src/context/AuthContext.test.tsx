import { describe, it, expect, afterEach } from 'vitest'
import { renderHook, act } from '@testing-library/react'
import { AuthProvider } from '@/context/AuthContext'
import { useAuth } from '@/hooks/useAuth'
import { tokenStore } from '@/lib/api'

afterEach(() => {
  tokenStore.clear()
})

describe('AuthContext', () => {
  it('login sets user state and stores token', () => {
    const { result } = renderHook(() => useAuth(), { wrapper: AuthProvider })

    act(() => {
      result.current.login('user-42', 'runner@example.com', 'jwt-token-xyz')
    })

    expect(result.current.isAuthenticated).toBe(true)
    expect(result.current.user).toEqual({ userId: 'user-42', email: 'runner@example.com' })
    expect(tokenStore.get()).toBe('jwt-token-xyz')
  })

  it('logout clears user state and token', async () => {
    const { result } = renderHook(() => useAuth(), { wrapper: AuthProvider })

    act(() => {
      result.current.login('user-42', 'runner@example.com', 'jwt-token-xyz')
    })
    await act(async () => { await result.current.logout() })

    expect(result.current.isAuthenticated).toBe(false)
    expect(result.current.user).toBeNull()
    expect(tokenStore.get()).toBeNull()
  })

  it('starts unauthenticated', () => {
    const { result } = renderHook(() => useAuth(), { wrapper: AuthProvider })

    expect(result.current.isAuthenticated).toBe(false)
    expect(result.current.user).toBeNull()
  })
})
