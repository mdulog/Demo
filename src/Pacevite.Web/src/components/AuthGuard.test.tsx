import { describe, it, expect } from 'vitest'
import { screen } from '@testing-library/react'
import { Routes, Route } from 'react-router-dom'
import { AuthGuard } from '@/components/AuthGuard'
import { renderWithProviders } from '@/test/render'

describe('AuthGuard', () => {
  it('renders children when authenticated', () => {
    renderWithProviders(
      <Routes>
        <Route path="/" element={<AuthGuard><div>protected content</div></AuthGuard>} />
        <Route path="/login" element={<div>login page</div>} />
      </Routes>,
      { authenticated: true, initialEntries: ['/'] }
    )

    expect(screen.getByText('protected content')).toBeInTheDocument()
    expect(screen.queryByText('login page')).not.toBeInTheDocument()
  })

  it('redirects to /login when not authenticated', () => {
    renderWithProviders(
      <Routes>
        <Route path="/" element={<AuthGuard><div>protected content</div></AuthGuard>} />
        <Route path="/login" element={<div>login page</div>} />
      </Routes>,
      { authenticated: false, initialEntries: ['/'] }
    )

    expect(screen.getByText('login page')).toBeInTheDocument()
    expect(screen.queryByText('protected content')).not.toBeInTheDocument()
  })
})
