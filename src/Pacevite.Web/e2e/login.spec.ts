import { test, expect } from '@playwright/test'
import { uniqueEmail, registerViaApi, loginViaUi } from './helpers'

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
