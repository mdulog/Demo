import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { renderHook, act } from '@testing-library/react'
import { ThemeProvider, useTheme } from '@/context/ThemeContext'

// matchMedia is not implemented in jsdom — we stub it per test.
function stubMatchMedia(matches: boolean) {
  Object.defineProperty(window, 'matchMedia', {
    writable: true,
    value: vi.fn((query: string) => ({
      matches,
      media: query,
      onchange: null,
      addEventListener: vi.fn(),
      removeEventListener: vi.fn(),
      dispatchEvent: vi.fn(),
    })),
  })
}

beforeEach(() => {
  localStorage.clear()
  document.documentElement.classList.remove('dark')
  stubMatchMedia(false) // default: system is light
})

afterEach(() => {
  vi.restoreAllMocks()
})

describe('ThemeContext', () => {
  it('defaults to light when system preference is light and no localStorage override', () => {
    stubMatchMedia(false)
    const { result } = renderHook(() => useTheme(), { wrapper: ThemeProvider })

    expect(result.current.theme).toBe('light')
    expect(document.documentElement.classList.contains('dark')).toBe(false)
  })

  it('defaults to dark when system preference is dark and no localStorage override', () => {
    stubMatchMedia(true)
    const { result } = renderHook(() => useTheme(), { wrapper: ThemeProvider })

    expect(result.current.theme).toBe('dark')
    expect(document.documentElement.classList.contains('dark')).toBe(true)
  })

  it('reads stored light preference from localStorage over system pref', () => {
    localStorage.setItem('theme', 'light')
    stubMatchMedia(true) // system is dark, but stored pref wins
    const { result } = renderHook(() => useTheme(), { wrapper: ThemeProvider })

    expect(result.current.theme).toBe('light')
    expect(document.documentElement.classList.contains('dark')).toBe(false)
  })

  it('reads stored dark preference from localStorage over system pref', () => {
    localStorage.setItem('theme', 'dark')
    stubMatchMedia(false) // system is light, but stored pref wins
    const { result } = renderHook(() => useTheme(), { wrapper: ThemeProvider })

    expect(result.current.theme).toBe('dark')
    expect(document.documentElement.classList.contains('dark')).toBe(true)
  })

  it('toggleTheme switches from light to dark and persists to localStorage', () => {
    const { result } = renderHook(() => useTheme(), { wrapper: ThemeProvider })

    act(() => { result.current.toggleTheme() })

    expect(result.current.theme).toBe('dark')
    expect(localStorage.getItem('theme')).toBe('dark')
    expect(document.documentElement.classList.contains('dark')).toBe(true)
  })

  it('toggleTheme switches from dark to light and persists to localStorage', () => {
    localStorage.setItem('theme', 'dark')
    stubMatchMedia(true)
    const { result } = renderHook(() => useTheme(), { wrapper: ThemeProvider })

    act(() => { result.current.toggleTheme() })

    expect(result.current.theme).toBe('light')
    expect(localStorage.getItem('theme')).toBe('light')
    expect(document.documentElement.classList.contains('dark')).toBe(false)
  })

  it('reacts to OS change when no manual override is stored', () => {
    let changeHandler: ((e: Partial<MediaQueryListEvent>) => void) | null = null
    Object.defineProperty(window, 'matchMedia', {
      writable: true,
      value: vi.fn((query: string) => ({
        matches: false,
        media: query,
        onchange: null,
        addEventListener: vi.fn((_: string, handler: (e: Partial<MediaQueryListEvent>) => void) => {
          changeHandler = handler
        }),
        removeEventListener: vi.fn(),
        dispatchEvent: vi.fn(),
      })),
    })

    const { result } = renderHook(() => useTheme(), { wrapper: ThemeProvider })
    expect(result.current.theme).toBe('light')

    act(() => { changeHandler?.({ matches: true } as MediaQueryListEvent) })

    expect(result.current.theme).toBe('dark')
  })

  it('does NOT react to OS change when a manual override is stored', () => {
    let changeHandler: ((e: Partial<MediaQueryListEvent>) => void) | null = null
    Object.defineProperty(window, 'matchMedia', {
      writable: true,
      value: vi.fn((query: string) => ({
        matches: false,
        media: query,
        onchange: null,
        addEventListener: vi.fn((_: string, handler: (e: Partial<MediaQueryListEvent>) => void) => {
          changeHandler = handler
        }),
        removeEventListener: vi.fn(),
        dispatchEvent: vi.fn(),
      })),
    })
    localStorage.setItem('theme', 'light')

    const { result } = renderHook(() => useTheme(), { wrapper: ThemeProvider })
    expect(result.current.theme).toBe('light')

    act(() => { changeHandler?.({ matches: true } as MediaQueryListEvent) })

    // Manual override in localStorage silences the listener
    expect(result.current.theme).toBe('light')
  })

  it('throws when useTheme is called outside ThemeProvider', () => {
    expect(() => renderHook(() => useTheme())).toThrow('useTheme must be used within ThemeProvider')
  })
})
