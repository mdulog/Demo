# Dark Mode Design

**Date:** 2026-04-25  
**Status:** Approved

## Overview

Add light and dark mode support to Pacevite. The theme defaults to the OS `prefers-color-scheme` setting but can be overridden by the user via a toggle in the nav bar. The preference persists to `localStorage` across sessions.

## Decisions Made

| Decision | Choice | Rationale |
|---|---|---|
| Theme control | System pref + manual toggle | Best UX — respects OS preference but gives user override |
| Toggle placement | Nav bar, right side (inline with Sign out) | Always visible, consistent with account controls |
| Toggle style | Icon (Sun/Moon) + label | Clear affordance without being intrusive |
| Implementation | CSS custom properties via Tailwind v4 `@theme inline` | One source of truth in CSS; components stay stable; no per-class `dark:` churn |
| Palette | Zinc + Indigo accent | Zinc-950 deep dark; indigo accent already used for links in codebase |

## Architecture

### ThemeContext (`src/context/ThemeContext.tsx`)

New context that:
1. On mount: reads `localStorage.getItem('theme')` — if set, uses it; otherwise reads `window.matchMedia('(prefers-color-scheme: dark)').matches`
2. Stores `theme: 'light' | 'dark'` in React state and applies/removes the `dark` class on `<html>`
3. Registers a `matchMedia` change listener — fires only when no manual override is stored in `localStorage`. When the OS theme changes and the user has not overridden it, the app theme updates in real time
4. When the user manually toggles: writes to `localStorage('theme')`, which makes the `matchMedia` listener dormant for the rest of the session
5. Exposes `{ theme, toggleTheme }` via a `useTheme` hook. The listener is cleaned up in the effect's teardown

Wrapped in `App.tsx` alongside the existing `<AuthProvider>`.

### ThemeToggle (`src/components/ThemeToggle.tsx`)

Small component using Lucide's `Sun` / `Moon` icons with a text label ("Light" / "Dark"). Placed in the nav bar of all three authenticated pages: `DashboardPage`, `UploadPage`, `EventDetailPage`. Login and Register pages receive color token updates only — no toggle, since the stored preference already applies.

## CSS Token Palette (Zinc + Indigo)

Defined once in `src/index.css` using Tailwind v4's `@theme inline`. All tokens are CSS custom properties overridden in `.dark`.

| Token | Utility class | Light (zinc) | Dark (zinc) |
|---|---|---|---|
| `--color-bg` | `bg-bg` | zinc-50 `#fafafa` | zinc-950 `#09090b` |
| `--color-surface` | `bg-surface` | white `#ffffff` | zinc-900 `#18181b` |
| `--color-border` | `border-border` | zinc-200 `#e4e4e7` | zinc-800 `#27272a` |
| `--color-text-primary` | `text-primary` | zinc-900 `#18181b` | zinc-50 `#fafafa` |
| `--color-text-secondary` | `text-secondary` | zinc-500 `#71717a` | zinc-400 `#a1a1aa` |
| `--color-text-muted` | `text-muted` | zinc-400 `#a1a1aa` | zinc-600 `#52525b` |
| `--color-badge-bg` | `bg-badge` | zinc-100 `#f4f4f5` | zinc-800 `#27272a` |
| `--color-badge-text` | `text-badge` | zinc-700 `#3f3f46` | zinc-300 `#d4d4d8` |
| `--color-action` | `bg-action` | zinc-900 `#18181b` | zinc-50 `#fafafa` |
| `--color-action-hover` | `bg-action-hover` | zinc-800 `#27272a` | zinc-200 `#e4e4e7` |
| `--color-action-text` | `text-action-text` | white `#ffffff` | zinc-900 `#18181b` |

Indigo link/accent colors (`text-indigo-600`, `hover:text-indigo-800`) are already correct in both modes — no token needed, they stay as-is.

## Tailwind v4 Configuration

```css
/* src/index.css */
@import "tailwindcss";

@custom-variant dark (&:where(.dark, .dark *));

@theme inline {
  --color-bg: var(--color-bg);
  --color-surface: var(--color-surface);
  --color-border: var(--color-border);
  --color-text-primary: var(--color-text-primary);
  --color-text-secondary: var(--color-text-secondary);
  --color-text-muted: var(--color-text-muted);
  --color-badge-bg: var(--color-badge-bg);
  --color-badge-text: var(--color-badge-text);
  --color-action: var(--color-action);
  --color-action-hover: var(--color-action-hover);
  --color-action-text: var(--color-action-text);
}

@layer base {
  :root {
    --color-bg: #fafafa;
    --color-surface: #ffffff;
    --color-border: #e4e4e7;
    --color-text-primary: #18181b;
    --color-text-secondary: #71717a;
    --color-text-muted: #a1a1aa;
    --color-badge-bg: #f4f4f5;
    --color-badge-text: #3f3f46;
    --color-action: #18181b;
    --color-action-hover: #27272a;
    --color-action-text: #ffffff;
  }

  .dark {
    --color-bg: #09090b;
    --color-surface: #18181b;
    --color-border: #27272a;
    --color-text-primary: #fafafa;
    --color-text-secondary: #a1a1aa;
    --color-text-muted: #52525b;
    --color-badge-bg: #27272a;
    --color-badge-text: #d4d4d8;
    --color-action: #fafafa;
    --color-action-hover: #e4e4e7;
    --color-action-text: #18181b;
  }
}
```

## Files Changed

| File | Change |
|---|---|
| `src/index.css` | Add `@custom-variant dark`, `@theme inline` block, `:root` + `.dark` token definitions |
| `src/context/ThemeContext.tsx` | **New** — `ThemeProvider` + `useTheme` hook |
| `src/components/ThemeToggle.tsx` | **New** — Sun/Moon toggle button component |
| `src/App.tsx` | Wrap with `<ThemeProvider>` |
| `src/pages/DashboardPage.tsx` | Replace hardcoded color classes with semantic tokens; add `<ThemeToggle />` to nav |
| `src/pages/EventDetailPage.tsx` | Replace hardcoded color classes; add `<ThemeToggle />` to nav |
| `src/pages/UploadPage.tsx` | Replace hardcoded color classes; add `<ThemeToggle />` to nav |
| `src/pages/LoginPage.tsx` | Replace hardcoded color classes only |
| `src/pages/RegisterPage.tsx` | Replace hardcoded color classes only |
| `src/components/ProgressChart.tsx` | Read chart stroke/fill colors from CSS variables via `getComputedStyle` |
| `src/components/SplitChart.tsx` | Same as above |
| `src/components/RaceComparison.tsx` | Same as above |
| `src/test/render.tsx` | Wrap `renderWithProviders` with `ThemeProvider` |

## Chart Color Handling

Recharts uses hex string props (`stroke="#..."`, `fill="#..."`), not Tailwind classes. Chart components will read color values at render time via:

```ts
const style = getComputedStyle(document.documentElement)
const borderColor = style.getPropertyValue('--color-border').trim()
const textColor = style.getPropertyValue('--color-text-secondary').trim()
```

This ensures chart lines and axes respond to theme switches without a page reload.

## Testing

- `ThemeContext` — unit tests covering: initialises from `localStorage`, falls back to system pref, toggles `.dark` on `<html>`, persists choice to `localStorage`, reacts to OS change when no override is stored, does NOT react to OS change when a manual override is stored
- `ThemeToggle` — unit test: renders Sun in light mode, Moon in dark mode, calls `toggleTheme` on click
- Existing page/component tests pass unchanged because `renderWithProviders` is updated to include `ThemeProvider`
- No new E2E tests required — theme is purely visual and not part of any authenticated flow

## Out of Scope

- Per-route theme overrides
- Theme-aware chart color animations
- Custom accent color picker
