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
  needsEnrichment: boolean
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

export type EventSummaryResponse = Omit<EventResponse, 'splits'>

export interface PagedEventsResponse {
  items: EventSummaryResponse[]
  nextCursor: string | null
}

export interface TimelineEntry {
  id: string
  eventDate: string
  eventType: string
  elapsedSecs: number
  completion: string
}

export interface AverageSplitResponse {
  label: string
  avgSecs: number
}

export interface EventDetailResponse extends EventResponse {
  averageSplits: AverageSplitResponse[]
}

export interface PersonalBestResponse {
  eventType: string
  eventId: string
  eventName: string
  eventDate: string
  elapsedSecs: number
}

export interface PredictionDataPoint {
  eventId: string | null
  eventDate: string
  elapsedSecs: number | null
  fittedSecs: number
}

export interface PredictionResponse {
  eventType: string
  predictedSecs: number
  confidenceLabel: string
  avgImprovementSecs: number
  dataPoints: PredictionDataPoint[]
}

export interface ValidationProblemDetails {
  title: string
  status: number
  errors: Record<string, string[]>
}

export interface ConnectStravaResponse {
  authorizeUrl: string
}

export interface StravaActivityPreviewResponse {
  externalActivityId: string
  name: string
  eventDate: string
  elapsedSecs: number
  possibleDuplicate: boolean
}

// Formats elapsed seconds as h:mm:ss
export function formatTime(secs: number): string {
  const h = Math.floor(secs / 3600)
  const m = Math.floor((secs % 3600) / 60)
  const s = secs % 60
  return `${h}:${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}`
}
