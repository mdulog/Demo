export interface AuthResponse {
  userId: string
  email: string
  token: string
}

export interface EventResponse {
  id: string
  eventType: string
  eventName: string
  eventDate: string
  completion: string
  elapsedSecs: number
  overallRank: number | null
  ageGroupRank: number | null
  fieldSize: number | null
  ageGroupFieldSize: number | null
  source: string
  createdAt: string
  splits: EventSplitResponse[]
}

export interface EventSplitResponse {
  id: string
  splitType: string
  splitLabel: string
  splitSecs: number
  cumulativeSecs: number
}

export interface PersonalBestResponse {
  eventType: string
  eventId: string
  eventName: string
  eventDate: string
  elapsedSecs: number
}

export interface ValidationProblemDetails {
  title: string
  status: number
  errors: Record<string, string[]>
}

// Formats elapsed seconds as h:mm:ss
export function formatTime(secs: number): string {
  const h = Math.floor(secs / 3600)
  const m = Math.floor((secs % 3600) / 60)
  const s = secs % 60
  return `${h}:${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}`
}
