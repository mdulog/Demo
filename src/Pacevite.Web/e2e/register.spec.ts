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
