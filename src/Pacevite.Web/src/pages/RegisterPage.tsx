import { useState, type FormEvent } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { useMutation } from '@tanstack/react-query'
import { apiClient } from '@/lib/api'
import { useAuth } from '@/hooks/useAuth'
import type { AuthResponse } from '@/lib/types'

export function RegisterPage() {
  const navigate = useNavigate()
  const { login } = useAuth()
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)

  const mutation = useMutation({
    mutationFn: async () => {
      const { data } = await apiClient.post<AuthResponse>('/auth/register', { email, password })
      return data
    },
    onSuccess: (data) => {
      login(data.userId, data.email, data.token)
      navigate('/dashboard')
    },
    onError: (err: unknown) => {
      const res = (err as { response?: { status?: number; data?: { detail?: string; title?: string } } }).response
      if (res?.status === 409) {
        setError('Email is already registered.')
      } else {
        setError(res?.data?.detail ?? res?.data?.title ?? 'Registration failed. Please try again.')
      }
    },
  })

  function handleSubmit(e: FormEvent) {
    e.preventDefault()
    setError(null)
    mutation.mutate()
  }

  return (
    <div className="min-h-screen flex items-center justify-center bg-gray-50">
      <div className="w-full max-w-sm bg-white rounded-lg shadow p-8 space-y-6">
        <h1 className="text-2xl font-semibold text-gray-900">Create your account</h1>

        <form onSubmit={handleSubmit} className="space-y-4">
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">Email</label>
            <input
              type="email"
              required
              value={email}
              onChange={e => setEmail(e.target.value)}
              className="w-full border border-gray-300 rounded-md px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-gray-900"
            />
          </div>

          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">
              Password <span className="text-gray-500 font-normal">(min. 8 chars, requires special char)</span>
            </label>
            <input
              type="password"
              required
              minLength={8}
              value={password}
              onChange={e => setPassword(e.target.value)}
              className="w-full border border-gray-300 rounded-md px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-gray-900"
            />
          </div>

          {error && <p className="text-sm text-red-600">{error}</p>}

          <button
            type="submit"
            disabled={mutation.isPending}
            className="w-full bg-gray-900 text-white rounded-md px-4 py-2 text-sm font-medium hover:bg-gray-800 disabled:opacity-50"
          >
            {mutation.isPending ? 'Creating account…' : 'Create account'}
          </button>
        </form>

        <p className="text-sm text-gray-600 text-center">
          Already have an account?{' '}
          <Link to="/login" className="font-medium text-gray-900 underline">
            Sign in
          </Link>
        </p>
      </div>
    </div>
  )
}
