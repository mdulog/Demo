import { test, expect } from '@playwright/test'
import path from 'path'
import { fileURLToPath } from 'url'
import { uniqueEmail, registerViaApi, loginViaUi } from './helpers'

const __dirname = path.dirname(fileURLToPath(import.meta.url))

test('clicking View on an event navigates to the detail page', async ({ page }) => {
  const email = uniqueEmail()
  await registerViaApi(email)
  await loginViaUi(page, email)

  // Upload an event with splits
  await page.click('a[href="/upload"]')
  await page.waitForURL('/upload')
  const jsonPath = path.join(__dirname, 'fixtures/events-with-splits.json')
  await page.setInputFiles('input[type="file"]', jsonPath)
  await page.waitForFunction(() => {
    const btn = document.querySelector<HTMLButtonElement>('button[type="submit"]')
    return btn !== null && !btn.disabled
  })
  await page.click('button[type="submit"]')
  await page.waitForURL('/dashboard')

  // Click View on the uploaded event
  await page.getByText('View').first().click()
  await page.waitForURL(/\/events\/.+/)

  // Assert detail page content — wait for split-chart first to confirm render is complete
  await expect(page.getByTestId('split-chart')).toBeVisible()
  await expect(page.getByRole('heading', { name: 'Brighton Half Marathon' })).toBeVisible()
  await expect(page.getByText(/2024-03-17/)).toBeVisible()
})

test('back link on detail page returns to dashboard', async ({ page }) => {
  const email = uniqueEmail()
  await registerViaApi(email)
  await loginViaUi(page, email)

  // Upload and navigate to detail
  await page.click('a[href="/upload"]')
  await page.waitForURL('/upload')
  const jsonPath = path.join(__dirname, 'fixtures/events-with-splits.json')
  await page.setInputFiles('input[type="file"]', jsonPath)
  await page.waitForFunction(() => {
    const btn = document.querySelector<HTMLButtonElement>('button[type="submit"]')
    return btn !== null && !btn.disabled
  })
  await page.click('button[type="submit"]')
  await page.waitForURL('/dashboard')
  await page.getByText('View').first().click()
  await page.waitForURL(/\/events\/.+/)

  // Click back
  await page.getByText('← Dashboard').click()
  await page.waitForURL('/dashboard')
  await expect(page.getByText('Brighton Half Marathon')).toBeVisible()
})
