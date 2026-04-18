import '@testing-library/jest-dom'
import { beforeAll, afterEach, afterAll } from 'vitest'
import { server } from './handlers'
import { apiClient } from '@/lib/api'

// In jsdom/Node there is no window.location, so axios resolves relative baseURLs
// to path-only strings (e.g. "/api/auth/register") that MSW cannot match against
// its absolute-URL handlers. Setting an explicit absolute baseURL here ensures
// MSW intercepts every request correctly in the test environment.
apiClient.defaults.baseURL = 'http://localhost/api'

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())
