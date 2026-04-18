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
