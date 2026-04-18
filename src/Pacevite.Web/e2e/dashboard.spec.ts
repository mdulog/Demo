import { test, expect } from '@playwright/test'
import path from 'path'
import { fileURLToPath } from 'url'
import { uniqueEmail, registerViaApi, loginViaUi } from './helpers'

const __dirname = path.dirname(fileURLToPath(import.meta.url))

test('delete event removes it from the dashboard', async ({ page }) => {
  const email = uniqueEmail()
  await registerViaApi(email)
  await loginViaUi(page, email)

  // Seed an event via the upload UI
  await page.click('a[href="/upload"]')
  await page.waitForURL('/upload')
  const csvPath = path.join(__dirname, 'fixtures/events.csv')
  await page.setInputFiles('input[type="file"]', csvPath)
  await page.waitForFunction(() => {
    const btn = document.querySelector<HTMLButtonElement>('button[type="submit"]')
    return btn !== null && !btn.disabled
  })
  await page.click('button[type="submit"]')
  await page.waitForURL('/dashboard')

  await expect(page.getByText('Test Half Marathon').first()).toBeVisible()

  // Delete the first event
  await page.getByLabel('Delete event').first().click()

  await expect(page.getByText('Test Half Marathon').first()).not.toBeVisible({ timeout: 5000 })
})
