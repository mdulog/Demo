import { describe, it, expect } from 'vitest'
import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { Routes, Route } from 'react-router-dom'
import { http, HttpResponse } from 'msw'
import { server } from '@/test/handlers'
import { RegisterPage } from '@/pages/RegisterPage'
import { renderWithProviders } from '@/test/render'

function renderRegisterPage() {
  return renderWithProviders(
    <Routes>
      <Route path="/register" element={<RegisterPage />} />
      <Route path="/dashboard" element={<div>dashboard</div>} />
    </Routes>,
    { initialEntries: ['/register'] }
  )
}

describe('RegisterPage', () => {
  it('navigates to /dashboard on successful registration', async () => {
    renderRegisterPage()

    await userEvent.type(screen.getByLabelText(/email/i), 'runner@example.com')
    await userEvent.type(screen.getByLabelText(/password/i), 'SecurePass1!')
    await userEvent.click(screen.getByRole('button', { name: /create account/i }))

    await waitFor(() => {
      expect(screen.getByText('dashboard')).toBeInTheDocument()
    })
  })

  it('shows email field error on 409 duplicate email', async () => {
    server.use(
      http.post('http://localhost/api/auth/register', () =>
        new HttpResponse(null, { status: 409 })
      )
    )

    renderRegisterPage()

    await userEvent.type(screen.getByLabelText(/email/i), 'existing@example.com')
    await userEvent.type(screen.getByLabelText(/password/i), 'SecurePass1!')
    await userEvent.click(screen.getByRole('button', { name: /create account/i }))

    await waitFor(() => {
      expect(
        screen.getByText('This email is already registered. Try signing in instead.')
      ).toBeInTheDocument()
    })
  })

  it('shows field errors under inputs on 400 validation failure', async () => {
    server.use(
      http.post('http://localhost/api/auth/register', () =>
        HttpResponse.json(
          {
            title: 'One or more validation errors occurred.',
            status: 400,
            errors: {
              Email: ['Email is not a valid email address.'],
              Password: ['Password must be at least 8 characters.'],
            },
          },
          { status: 400 }
        )
      )
    )

    renderRegisterPage()

    await userEvent.type(screen.getByLabelText(/email/i), 'bad@example.com')
    await userEvent.type(screen.getByLabelText(/password/i), 'short123')
    await userEvent.click(screen.getByRole('button', { name: /create account/i }))

    await waitFor(() => {
      expect(screen.getByText('Email is not a valid email address.')).toBeInTheDocument()
      expect(screen.getByText('Password must be at least 8 characters.')).toBeInTheDocument()
    })
  })

  it('shows banner on network failure', async () => {
    server.use(
      http.post('http://localhost/api/auth/register', () =>
        HttpResponse.error()
      )
    )

    renderRegisterPage()

    await userEvent.type(screen.getByLabelText(/email/i), 'runner@example.com')
    await userEvent.type(screen.getByLabelText(/password/i), 'SecurePass1!')
    await userEvent.click(screen.getByRole('button', { name: /create account/i }))

    await waitFor(() => {
      expect(
        screen.getByText('Unable to reach the server. Check your connection and try again.')
      ).toBeInTheDocument()
    })
  })
})
