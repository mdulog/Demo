import { render, type RenderOptions } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter } from 'react-router-dom'
import { AuthContext } from '@/context/AuthContext'
import { ThemeProvider } from '@/context/ThemeContext'
import { vi } from 'vitest'
import type { ReactElement } from 'react'

function makeQueryClient() {
  return new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  })
}

interface RenderWithProvidersOptions extends Omit<RenderOptions, 'wrapper'> {
  initialEntries?: string[]
  authenticated?: boolean
}

export function renderWithProviders(
  ui: ReactElement,
  {
    initialEntries = ['/'],
    authenticated = false,
    ...renderOptions
  }: RenderWithProvidersOptions = {}
) {
  const authValue = {
    user: authenticated ? { userId: 'user-1', email: 'test@example.com' } : null,
    isAuthenticated: authenticated,
    login: vi.fn(),
    logout: vi.fn().mockResolvedValue(undefined),
  }

  return render(
    <ThemeProvider>
      <QueryClientProvider client={makeQueryClient()}>
        <AuthContext.Provider value={authValue}>
          <MemoryRouter initialEntries={initialEntries}>
            {ui}
          </MemoryRouter>
        </AuthContext.Provider>
      </QueryClientProvider>
    </ThemeProvider>,
    renderOptions
  )
}
