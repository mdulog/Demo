# Frontend Testing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add Vitest + RTL + MSW unit/component tests and Playwright E2E tests to `src/Pacevite.Web`.

**Architecture:** Unit tests run in jsdom via Vitest with MSW intercepting axios at the network layer. E2E tests run Playwright against the real Vite dev server + .NET API + PostgreSQL stack. Vite proxies `/api` to `http://localhost:5291`, so E2E setup calls the API directly on port 5291.

**Tech Stack:** Vitest, React Testing Library, MSW v2, @testing-library/user-event, @testing-library/jest-dom, Playwright

---

### Task 1: Install dependencies and configure Vitest

**Files:**
- Modify: `src/Pacevite.Web/package.json`
- Create: `src/Pacevite.Web/vitest.config.ts`

- [ ] **Step 1: Install unit test dependencies**

```bash
cd src/Pacevite.Web
npm install --save-dev vitest @vitest/coverage-v8 jsdom @testing-library/react @testing-library/jest-dom @testing-library/user-event msw
```

- [ ] **Step 2: Install Playwright**

```bash
cd src/Pacevite.Web
npm install --save-dev @playwright/test
npx playwright install chromium
```

- [ ] **Step 3: Add test scripts to package.json**

Add to the `"scripts"` block in `src/Pacevite.Web/package.json`:

```json
"test": "vitest",
"test:run": "vitest run",
"test:e2e": "playwright test"
```

- [ ] **Step 4: Create vitest.config.ts**

```ts
import { defineConfig, mergeConfig } from 'vitest/config'
import viteConfig from './vite.config'

export default mergeConfig(viteConfig, defineConfig({
  test: {
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
    globals: true,
    exclude: ['e2e/**', 'node_modules/**'],
  },
}))
```

- [ ] **Step 5: Verify Vitest starts**

```bash
cd src/Pacevite.Web
npm run test:run
```

Expected: exits 0 with "No test files found" (no tests yet).

- [ ] **Step 6: Commit**

```bash
git add src/Pacevite.Web/package.json src/Pacevite.Web/package-lock.json src/Pacevite.Web/vitest.config.ts
git commit -m "chore(web): add Vitest and Playwright test dependencies"
```

---

### Task 2: MSW handlers, test setup, and render helper

**Files:**
- Create: `src/Pacevite.Web/src/test/setup.ts`
- Create: `src/Pacevite.Web/src/test/handlers.ts`
- Create: `src/Pacevite.Web/src/test/render.tsx`

- [ ] **Step 1: Create MSW handlers**

```ts
// src/Pacevite.Web/src/test/handlers.ts
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'

export const handlers = [
  http.post('http://localhost/api/auth/register', () =>
    HttpResponse.json(
      { userId: 'user-1', email: 'test@example.com', token: 'token-abc' },
      { status: 201 }
    )
  ),
  http.post('http://localhost/api/auth/login', () =>
    HttpResponse.json({ userId: 'user-1', email: 'test@example.com', token: 'token-abc' })
  ),
  http.get('http://localhost/api/events', () =>
    HttpResponse.json([
      {
        id: 'event-1',
        eventType: 'MARATHON',
        eventName: 'Berlin Marathon',
        eventDate: '2024-09-29',
        completion: 'FINISHED',
        elapsedSecs: 12600,
        overallRank: 1500,
        ageGroupRank: null,
        fieldSize: 45000,
        ageGroupFieldSize: null,
        source: 'manual',
        createdAt: '2024-10-01T00:00:00Z',
        splits: [],
      },
    ])
  ),
  http.get('http://localhost/api/events/personal-bests', () =>
    HttpResponse.json([
      {
        eventType: 'MARATHON',
        eventId: 'event-1',
        eventName: 'Berlin Marathon',
        eventDate: '2024-09-29',
        elapsedSecs: 12600,
      },
    ])
  ),
  http.delete('http://localhost/api/events/:id', () =>
    new HttpResponse(null, { status: 204 })
  ),
  http.post('http://localhost/api/events/upload', () =>
    HttpResponse.json(
      [
        {
          id: 'event-2',
          eventType: 'HALF_MARATHON',
          eventName: 'Test Half',
          eventDate: '2024-06-01',
          completion: 'FINISHED',
          elapsedSecs: 5400,
          overallRank: null,
          ageGroupRank: null,
          fieldSize: null,
          ageGroupFieldSize: null,
          source: 'csv',
          createdAt: '2024-10-01T00:00:00Z',
          splits: [],
        },
      ],
      { status: 201 }
    )
  ),
]

export const server = setupServer(...handlers)
```

- [ ] **Step 2: Create test setup file**

```ts
// src/Pacevite.Web/src/test/setup.ts
import '@testing-library/jest-dom'
import { beforeAll, afterEach, afterAll } from 'vitest'
import { server } from './handlers'

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())
```

- [ ] **Step 3: Create shared render helper**

```tsx
// src/Pacevite.Web/src/test/render.tsx
import { render, type RenderOptions } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter } from 'react-router-dom'
import { AuthContext } from '@/context/AuthContext'
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
    logout: vi.fn(),
  }

  return render(
    <QueryClientProvider client={makeQueryClient()}>
      <AuthContext.Provider value={authValue}>
        <MemoryRouter initialEntries={initialEntries}>
          {ui}
        </MemoryRouter>
      </AuthContext.Provider>
    </QueryClientProvider>,
    renderOptions
  )
}
```

- [ ] **Step 4: Verify setup compiles**

```bash
cd src/Pacevite.Web
npm run test:run
```

Expected: exits 0 with "No test files found".

- [ ] **Step 5: Commit**

```bash
git add src/Pacevite.Web/src/test/
git commit -m "test(web): add MSW handlers, vitest setup, and render helper"
```

---

### Task 3: AuthGuard unit tests

**Files:**
- Create: `src/Pacevite.Web/src/components/AuthGuard.test.tsx`

- [ ] **Step 1: Write the tests**

```tsx
// src/Pacevite.Web/src/components/AuthGuard.test.tsx
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
```

- [ ] **Step 2: Run and verify tests pass**

```bash
cd src/Pacevite.Web
npm run test:run -- src/components/AuthGuard.test.tsx
```

Expected: 2 passed.

- [ ] **Step 3: Commit**

```bash
git add src/Pacevite.Web/src/components/AuthGuard.test.tsx
git commit -m "test(web): add AuthGuard unit tests"
```

---

### Task 4: AuthContext unit tests

**Files:**
- Create: `src/Pacevite.Web/src/context/AuthContext.test.tsx`

- [ ] **Step 1: Write the tests**

```tsx
// src/Pacevite.Web/src/context/AuthContext.test.tsx
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

  it('logout clears user state and token', () => {
    const { result } = renderHook(() => useAuth(), { wrapper: AuthProvider })

    act(() => {
      result.current.login('user-42', 'runner@example.com', 'jwt-token-xyz')
    })
    act(() => {
      result.current.logout()
    })

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
```

- [ ] **Step 2: Run and verify tests pass**

```bash
cd src/Pacevite.Web
npm run test:run -- src/context/AuthContext.test.tsx
```

Expected: 3 passed.

- [ ] **Step 3: Commit**

```bash
git add src/Pacevite.Web/src/context/AuthContext.test.tsx
git commit -m "test(web): add AuthContext unit tests"
```

---

### Task 5: RegisterPage unit tests

**Files:**
- Modify: `src/Pacevite.Web/src/pages/RegisterPage.tsx` (add `id`/`htmlFor` for accessibility + testability)
- Create: `src/Pacevite.Web/src/pages/RegisterPage.test.tsx`

- [ ] **Step 1: Add `id` and `htmlFor` to RegisterPage form inputs**

Labels and inputs in `RegisterPage` are not associated — `getByLabelText` requires `htmlFor`/`id` pairs. Update `src/Pacevite.Web/src/pages/RegisterPage.tsx`:

```tsx
// Email field — add htmlFor on label, id on input
<label htmlFor="email" className="block text-sm font-medium text-gray-700 mb-1">Email</label>
<input
  id="email"
  type="email"
  required
  value={email}
  onChange={e => { setEmail(e.target.value); setFieldErrors(f => ({ ...f, email: undefined })) }}
  className={`w-full border rounded-md px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-gray-900 ${fieldErrors.email ? 'border-red-500' : 'border-gray-300'}`}
/>

// Password field — add htmlFor on label, id on input
<label htmlFor="password" className="block text-sm font-medium text-gray-700 mb-1">
  Password <span className="text-gray-500 font-normal">(min. 8 chars, requires special char)</span>
</label>
<input
  id="password"
  type="password"
  required
  minLength={8}
  value={password}
  onChange={e => { setPassword(e.target.value); setFieldErrors(f => ({ ...f, password: undefined })) }}
  className={`w-full border rounded-md px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-gray-900 ${fieldErrors.password ? 'border-red-500' : 'border-gray-300'}`}
/>
```

- [ ] **Step 2: Write the tests**

```tsx
// src/Pacevite.Web/src/pages/RegisterPage.test.tsx
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

    await userEvent.type(screen.getByLabelText(/email/i), 'notanemail')
    await userEvent.type(screen.getByLabelText(/password/i), 'short')
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
```

- [ ] **Step 2: Run and verify tests pass**

```bash
cd src/Pacevite.Web
npm run test:run -- src/pages/RegisterPage.test.tsx
```

Expected: 4 passed.

- [ ] **Step 3: Commit**

```bash
git add src/Pacevite.Web/src/pages/RegisterPage.test.tsx
git commit -m "test(web): add RegisterPage unit tests"
```

---

### Task 6: LoginPage unit tests

**Files:**
- Modify: `src/Pacevite.Web/src/pages/LoginPage.tsx` (add `id`/`htmlFor` for accessibility + testability)
- Create: `src/Pacevite.Web/src/pages/LoginPage.test.tsx`

- [ ] **Step 1: Add `id` and `htmlFor` to LoginPage form inputs**

Update `src/Pacevite.Web/src/pages/LoginPage.tsx`:

```tsx
// Email field
<label htmlFor="email" className="block text-sm font-medium text-gray-700 mb-1">Email</label>
<input
  id="email"
  type="email"
  required
  value={email}
  onChange={e => setEmail(e.target.value)}
  className="w-full border border-gray-300 rounded-md px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-gray-900"
/>

// Password field
<label htmlFor="password" className="block text-sm font-medium text-gray-700 mb-1">Password</label>
<input
  id="password"
  type="password"
  required
  value={password}
  onChange={e => setPassword(e.target.value)}
  className="w-full border border-gray-300 rounded-md px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-gray-900"
/>
```

- [ ] **Step 2: Write the tests**

```tsx
// src/Pacevite.Web/src/pages/LoginPage.test.tsx
import { describe, it, expect } from 'vitest'
import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { Routes, Route } from 'react-router-dom'
import { http, HttpResponse } from 'msw'
import { server } from '@/test/handlers'
import { LoginPage } from '@/pages/LoginPage'
import { renderWithProviders } from '@/test/render'

function renderLoginPage() {
  return renderWithProviders(
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route path="/dashboard" element={<div>dashboard</div>} />
    </Routes>,
    { initialEntries: ['/login'] }
  )
}

describe('LoginPage', () => {
  it('navigates to /dashboard on successful login', async () => {
    renderLoginPage()

    await userEvent.type(screen.getByLabelText(/email/i), 'runner@example.com')
    await userEvent.type(screen.getByLabelText(/password/i), 'SecurePass1!')
    await userEvent.click(screen.getByRole('button', { name: /sign in/i }))

    await waitFor(() => {
      expect(screen.getByText('dashboard')).toBeInTheDocument()
    })
  })

  it('shows error message on invalid credentials', async () => {
    server.use(
      http.post('http://localhost/api/auth/login', () =>
        new HttpResponse(null, { status: 401 })
      )
    )

    renderLoginPage()

    await userEvent.type(screen.getByLabelText(/email/i), 'runner@example.com')
    await userEvent.type(screen.getByLabelText(/password/i), 'WrongPassword!')
    await userEvent.click(screen.getByRole('button', { name: /sign in/i }))

    await waitFor(() => {
      expect(screen.getByText('Invalid email or password.')).toBeInTheDocument()
    })
  })
})
```

- [ ] **Step 2: Run and verify tests pass**

```bash
cd src/Pacevite.Web
npm run test:run -- src/pages/LoginPage.test.tsx
```

Expected: 2 passed.

- [ ] **Step 3: Commit**

```bash
git add src/Pacevite.Web/src/pages/LoginPage.test.tsx
git commit -m "test(web): add LoginPage unit tests"
```

---

### Task 7: DashboardPage unit tests

**Files:**
- Create: `src/Pacevite.Web/src/pages/DashboardPage.test.tsx`

- [ ] **Step 1: Write the tests**

```tsx
// src/Pacevite.Web/src/pages/DashboardPage.test.tsx
import { describe, it, expect } from 'vitest'
import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { server } from '@/test/handlers'
import { DashboardPage } from '@/pages/DashboardPage'
import { renderWithProviders } from '@/test/render'

function renderDashboard() {
  return renderWithProviders(<DashboardPage />, { authenticated: true })
}

describe('DashboardPage', () => {
  it('renders the event list from the API', async () => {
    renderDashboard()

    await waitFor(() => {
      expect(screen.getByText('Berlin Marathon')).toBeInTheDocument()
    })
    expect(screen.getByText('MARATHON')).toBeInTheDocument()
    expect(screen.getByText('2024-09-29')).toBeInTheDocument()
  })

  it('renders personal bests section when data is present', async () => {
    renderDashboard()

    await waitFor(() => {
      expect(screen.getByText(/personal bests/i)).toBeInTheDocument()
    })
    expect(screen.getAllByText('MARATHON').length).toBeGreaterThan(0)
  })

  it('shows empty state when no events exist', async () => {
    server.use(
      http.get('http://localhost/api/events', () => HttpResponse.json([])),
      http.get('http://localhost/api/events/personal-bests', () => HttpResponse.json([]))
    )

    renderDashboard()

    await waitFor(() => {
      expect(screen.getByText('No events yet.')).toBeInTheDocument()
    })
  })

  it('calls delete endpoint when delete button is clicked', async () => {
    let deletedId: string | undefined

    server.use(
      http.delete('http://localhost/api/events/:id', ({ params }) => {
        deletedId = params.id as string
        return new HttpResponse(null, { status: 204 })
      })
    )

    renderDashboard()

    await waitFor(() => {
      expect(screen.getByLabelText('Delete event')).toBeInTheDocument()
    })

    await userEvent.click(screen.getByLabelText('Delete event'))

    await waitFor(() => {
      expect(deletedId).toBe('event-1')
    })
  })
})
```

- [ ] **Step 2: Run and verify tests pass**

```bash
cd src/Pacevite.Web
npm run test:run -- src/pages/DashboardPage.test.tsx
```

Expected: 4 passed.

- [ ] **Step 3: Commit**

```bash
git add src/Pacevite.Web/src/pages/DashboardPage.test.tsx
git commit -m "test(web): add DashboardPage unit tests"
```

---

### Task 8: Playwright config and E2E fixtures

**Files:**
- Create: `src/Pacevite.Web/playwright.config.ts`
- Create: `src/Pacevite.Web/e2e/helpers.ts`
- Create: `src/Pacevite.Web/e2e/fixtures/events.csv`

- [ ] **Step 1: Create Playwright config**

```ts
// src/Pacevite.Web/playwright.config.ts
import { defineConfig, devices } from '@playwright/test'

export default defineConfig({
  testDir: './e2e',
  fullyParallel: false,
  retries: 0,
  reporter: 'html',
  use: {
    baseURL: 'http://localhost:5173',
    screenshot: 'only-on-failure',
    trace: 'on-first-retry',
  },
  projects: [
    { name: 'chromium', use: { ...devices['Desktop Chrome'] } },
  ],
  webServer: {
    command: 'npm run dev',
    url: 'http://localhost:5173',
    reuseExistingServer: true,
  },
})
```

- [ ] **Step 2: Create E2E helpers**

```ts
// src/Pacevite.Web/e2e/helpers.ts
import { type Page, request } from '@playwright/test'

const API_BASE = 'http://localhost:5291'
export const TEST_PASSWORD = 'TestPass123!'

export function uniqueEmail(): string {
  return `test-${Date.now()}@example.com`
}

export async function registerViaApi(email: string): Promise<void> {
  const ctx = await request.newContext({ baseURL: API_BASE })
  const res = await ctx.post('/api/auth/register', {
    data: { email, password: TEST_PASSWORD },
  })
  if (!res.ok()) throw new Error(`Registration failed: ${res.status()}`)
  await ctx.dispose()
}

export async function loginViaUi(page: Page, email: string): Promise<void> {
  await page.goto('/login')
  await page.fill('input[type="email"]', email)
  await page.fill('input[type="password"]', TEST_PASSWORD)
  await page.click('button[type="submit"]')
  await page.waitForURL('/dashboard')
}
```

- [ ] **Step 3: Create CSV fixture**

```csv
// src/Pacevite.Web/e2e/fixtures/events.csv
event_type,event_name,event_date,completion,elapsed_secs
HALF_MARATHON,Test Half Marathon,2024-06-01,FINISHED,5400
5K,Test 5K,2024-05-01,FINISHED,1200
```

- [ ] **Step 4: Commit**

```bash
git add src/Pacevite.Web/playwright.config.ts src/Pacevite.Web/e2e/
git commit -m "test(web): add Playwright config and E2E helpers"
```

---

### Task 9: E2E register test

**Files:**
- Create: `src/Pacevite.Web/e2e/register.spec.ts`

Prerequisite: Vite dev server (`npm run dev`) and .NET API (`dotnet run` in `src/Pacevite.Api`) must be running.

- [ ] **Step 1: Write the test**

```ts
// src/Pacevite.Web/e2e/register.spec.ts
import { test, expect } from '@playwright/test'
import { uniqueEmail, TEST_PASSWORD } from './helpers'

test('register with valid credentials lands on empty dashboard', async ({ page }) => {
  const email = uniqueEmail()

  await page.goto('/register')
  await page.fill('input[type="email"]', email)
  await page.fill('input[type="password"]', TEST_PASSWORD)
  await page.click('button[type="submit"]')

  await page.waitForURL('/dashboard')
  await expect(page.getByText('No events yet.')).toBeVisible()
})
```

- [ ] **Step 2: Run and verify test passes**

```bash
cd src/Pacevite.Web
npm run test:e2e -- e2e/register.spec.ts
```

Expected: 1 passed.

- [ ] **Step 3: Commit**

```bash
git add src/Pacevite.Web/e2e/register.spec.ts
git commit -m "test(web): add E2E register flow test"
```

---

### Task 10: E2E login test

**Files:**
- Create: `src/Pacevite.Web/e2e/login.spec.ts`

- [ ] **Step 1: Write the test**

```ts
// src/Pacevite.Web/e2e/login.spec.ts
import { test, expect } from '@playwright/test'
import { uniqueEmail, TEST_PASSWORD, registerViaApi, loginViaUi } from './helpers'

test('login with valid credentials lands on dashboard', async ({ page }) => {
  const email = uniqueEmail()
  await registerViaApi(email)

  await loginViaUi(page, email)

  await expect(page).toHaveURL('/dashboard')
  await expect(page.getByText('No events yet.')).toBeVisible()
})

test('login with invalid credentials shows error message', async ({ page }) => {
  await page.goto('/login')
  await page.fill('input[type="email"]', 'nonexistent@example.com')
  await page.fill('input[type="password"]', 'WrongPassword!')
  await page.click('button[type="submit"]')

  await expect(page.getByText('Invalid email or password.')).toBeVisible()
})
```

- [ ] **Step 2: Run and verify tests pass**

```bash
cd src/Pacevite.Web
npm run test:e2e -- e2e/login.spec.ts
```

Expected: 2 passed.

- [ ] **Step 3: Commit**

```bash
git add src/Pacevite.Web/e2e/login.spec.ts
git commit -m "test(web): add E2E login flow tests"
```

---

### Task 11: E2E upload test

**Files:**
- Create: `src/Pacevite.Web/e2e/upload.spec.ts`

- [ ] **Step 1: Write the test**

```ts
// src/Pacevite.Web/e2e/upload.spec.ts
import { test, expect } from '@playwright/test'
import path from 'path'
import { uniqueEmail, registerViaApi, loginViaUi } from './helpers'

test('upload CSV file adds events to dashboard', async ({ page }) => {
  const email = uniqueEmail()
  await registerViaApi(email)
  await loginViaUi(page, email)

  await page.click('a[href="/upload"]')
  await page.waitForURL('/upload')

  const csvPath = path.join(__dirname, 'fixtures/events.csv')
  await page.setInputFiles('input[type="file"]', csvPath)

  await page.click('button[type="submit"]')
  await page.waitForURL('/dashboard')

  await expect(page.getByText('Test Half Marathon')).toBeVisible()
  await expect(page.getByText('Test 5K')).toBeVisible()
})
```

- [ ] **Step 2: Run and verify test passes**

```bash
cd src/Pacevite.Web
npm run test:e2e -- e2e/upload.spec.ts
```

Expected: 1 passed.

- [ ] **Step 3: Commit**

```bash
git add src/Pacevite.Web/e2e/upload.spec.ts
git commit -m "test(web): add E2E upload flow test"
```

---

### Task 12: E2E delete test

**Files:**
- Create: `src/Pacevite.Web/e2e/dashboard.spec.ts`

- [ ] **Step 1: Write the test**

```ts
// src/Pacevite.Web/e2e/dashboard.spec.ts
import { test, expect } from '@playwright/test'
import path from 'path'
import { uniqueEmail, registerViaApi, loginViaUi } from './helpers'

test('delete event removes it from the dashboard', async ({ page }) => {
  const email = uniqueEmail()
  await registerViaApi(email)
  await loginViaUi(page, email)

  // Seed an event via the upload UI
  await page.click('a[href="/upload"]')
  await page.waitForURL('/upload')
  const csvPath = path.join(__dirname, 'fixtures/events.csv')
  await page.setInputFiles('input[type="file"]', csvPath)
  await page.click('button[type="submit"]')
  await page.waitForURL('/dashboard')

  await expect(page.getByText('Test Half Marathon')).toBeVisible()

  // Delete the first event
  await page.getByLabel('Delete event').first().click()

  await expect(page.getByText('Test Half Marathon')).not.toBeVisible({ timeout: 5000 })
})
```

- [ ] **Step 2: Run and verify test passes**

```bash
cd src/Pacevite.Web
npm run test:e2e -- e2e/dashboard.spec.ts
```

Expected: 1 passed.

- [ ] **Step 3: Run the full test suites**

```bash
cd src/Pacevite.Web
npm run test:run
npm run test:e2e
```

Expected: all unit tests pass, all E2E tests pass.

- [ ] **Step 4: Commit**

```bash
git add src/Pacevite.Web/e2e/dashboard.spec.ts
git commit -m "test(web): add E2E delete event test"
```
