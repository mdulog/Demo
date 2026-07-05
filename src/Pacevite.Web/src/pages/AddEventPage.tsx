import { useState } from 'react'
import { useNavigate, Link } from 'react-router-dom'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { isAxiosError } from 'axios'
import { apiClient } from '@/lib/api'
import { ThemeToggle } from '@/components/ThemeToggle'
import { ArrowLeft, ClipboardPlus } from 'lucide-react'
import type { EventResponse } from '@/lib/types'

const EVENT_TYPES = ['Marathon', 'Hyrox', 'Spartan', 'Generic'] as const
const COMPLETIONS = ['Finished', 'Dnf', 'Dns'] as const

interface CreateEventRequest {
  eventType: string
  eventName: string
  eventDate: string
  completion: string
  elapsedSecs: number
  overallRank: number | null
  ageGroupRank: number | null
  fieldSize: number | null
  ageGroupFieldSize: number | null
}

function toOptionalNumber(value: string): number | null {
  return value === '' ? null : Number(value)
}

export function AddEventPage() {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const [error, setError] = useState<string | null>(null)

  const [eventType, setEventType] = useState<string>(EVENT_TYPES[3])
  const [eventName, setEventName] = useState('')
  const [eventDate, setEventDate] = useState('')
  const [completion, setCompletion] = useState<string>(COMPLETIONS[0])
  const [hours, setHours] = useState('')
  const [minutes, setMinutes] = useState('')
  const [seconds, setSeconds] = useState('')
  const [overallRank, setOverallRank] = useState('')
  const [ageGroupRank, setAgeGroupRank] = useState('')
  const [fieldSize, setFieldSize] = useState('')
  const [ageGroupFieldSize, setAgeGroupFieldSize] = useState('')

  const elapsedSecs = (Number(hours) || 0) * 3600 + (Number(minutes) || 0) * 60 + (Number(seconds) || 0)
  const canSave = eventName.trim() !== '' && eventDate !== '' && elapsedSecs > 0

  const mutation = useMutation({
    mutationFn: async () => {
      const request: CreateEventRequest = {
        eventType,
        eventName,
        eventDate,
        completion,
        elapsedSecs,
        overallRank: toOptionalNumber(overallRank),
        ageGroupRank: toOptionalNumber(ageGroupRank),
        fieldSize: toOptionalNumber(fieldSize),
        ageGroupFieldSize: toOptionalNumber(ageGroupFieldSize),
      }
      const { data } = await apiClient.post<EventResponse>('/events', request)
      return data
    },
    onSuccess: (created) => {
      void queryClient.invalidateQueries({ queryKey: ['events'] })
      void queryClient.invalidateQueries({ queryKey: ['personal-bests'] })
      void queryClient.invalidateQueries({ queryKey: ['timeline'] })
      navigate(`/events/${created.id}`)
    },
    onError: (err) => {
      if (isAxiosError(err) && err.response?.status === 409) {
        setError(typeof err.response.data === 'string' ? err.response.data : 'An event like this already exists.')
        return
      }
      setError("Couldn't save event. Check your inputs and try again.")
    },
  })

  function handleSubmit(e: { preventDefault(): void }) {
    e.preventDefault()
    if (!canSave) return
    setError(null)
    mutation.mutate()
  }

  return (
    <div className="min-h-screen bg-bg">
      <header className="bg-surface border-b border-border px-6 py-4 flex items-center gap-4">
        <Link to="/dashboard" className="text-secondary hover:text-primary">
          <ArrowLeft size={16} />
        </Link>
        <h1 className="text-lg font-semibold text-primary flex-1">Add Event</h1>
        <ThemeToggle />
      </header>

      <main className="max-w-xl mx-auto px-6 py-12 space-y-6">
        <div className="bg-surface rounded-lg border border-border p-8 space-y-6">
          <div className="flex items-center gap-2 text-secondary text-sm">
            <ClipboardPlus size={16} />
            <span>Manually enter a race result</span>
          </div>

          <form onSubmit={handleSubmit} className="space-y-4">
            <div>
              <label htmlFor="eventType" className="block text-sm font-medium text-primary mb-1">Event type</label>
              <select
                id="eventType"
                value={eventType}
                onChange={e => setEventType(e.target.value)}
                className="w-full border border-border rounded-md px-3 py-2 text-sm bg-surface text-primary"
              >
                {EVENT_TYPES.map(t => <option key={t} value={t}>{t}</option>)}
              </select>
            </div>

            <div>
              <label htmlFor="eventName" className="block text-sm font-medium text-primary mb-1">Event name</label>
              <input
                id="eventName"
                type="text"
                value={eventName}
                onChange={e => setEventName(e.target.value)}
                className="w-full border border-border rounded-md px-3 py-2 text-sm bg-surface text-primary"
              />
            </div>

            <div>
              <label htmlFor="eventDate" className="block text-sm font-medium text-primary mb-1">Date</label>
              <input
                id="eventDate"
                type="date"
                value={eventDate}
                onChange={e => setEventDate(e.target.value)}
                className="w-full border border-border rounded-md px-3 py-2 text-sm bg-surface text-primary"
              />
            </div>

            <div>
              <label htmlFor="completion" className="block text-sm font-medium text-primary mb-1">Completion</label>
              <select
                id="completion"
                value={completion}
                onChange={e => setCompletion(e.target.value)}
                className="w-full border border-border rounded-md px-3 py-2 text-sm bg-surface text-primary"
              >
                {COMPLETIONS.map(c => <option key={c} value={c}>{c}</option>)}
              </select>
            </div>

            <div>
              <span className="block text-sm font-medium text-primary mb-1">Elapsed time</span>
              <div className="flex items-center gap-2">
                <div className="flex-1">
                  <label htmlFor="hours" className="block text-xs text-secondary mb-1">Hours</label>
                  <input
                    id="hours"
                    type="number"
                    min={0}
                    value={hours}
                    onChange={e => setHours(e.target.value)}
                    className="w-full border border-border rounded-md px-3 py-2 text-sm bg-surface text-primary"
                  />
                </div>
                <div className="flex-1">
                  <label htmlFor="minutes" className="block text-xs text-secondary mb-1">Minutes</label>
                  <input
                    id="minutes"
                    type="number"
                    min={0}
                    max={59}
                    value={minutes}
                    onChange={e => setMinutes(e.target.value)}
                    className="w-full border border-border rounded-md px-3 py-2 text-sm bg-surface text-primary"
                  />
                </div>
                <div className="flex-1">
                  <label htmlFor="seconds" className="block text-xs text-secondary mb-1">Seconds</label>
                  <input
                    id="seconds"
                    type="number"
                    min={0}
                    max={59}
                    value={seconds}
                    onChange={e => setSeconds(e.target.value)}
                    className="w-full border border-border rounded-md px-3 py-2 text-sm bg-surface text-primary"
                  />
                </div>
              </div>
            </div>

            <div className="grid grid-cols-2 gap-4">
              <div>
                <label htmlFor="overallRank" className="block text-sm font-medium text-primary mb-1">Overall rank (optional)</label>
                <input
                  id="overallRank"
                  type="number"
                  min={1}
                  value={overallRank}
                  onChange={e => setOverallRank(e.target.value)}
                  className="w-full border border-border rounded-md px-3 py-2 text-sm bg-surface text-primary"
                />
              </div>
              <div>
                <label htmlFor="fieldSize" className="block text-sm font-medium text-primary mb-1">Field size (optional)</label>
                <input
                  id="fieldSize"
                  type="number"
                  min={1}
                  value={fieldSize}
                  onChange={e => setFieldSize(e.target.value)}
                  className="w-full border border-border rounded-md px-3 py-2 text-sm bg-surface text-primary"
                />
              </div>
              <div>
                <label htmlFor="ageGroupRank" className="block text-sm font-medium text-primary mb-1">Age group rank (optional)</label>
                <input
                  id="ageGroupRank"
                  type="number"
                  min={1}
                  value={ageGroupRank}
                  onChange={e => setAgeGroupRank(e.target.value)}
                  className="w-full border border-border rounded-md px-3 py-2 text-sm bg-surface text-primary"
                />
              </div>
              <div>
                <label htmlFor="ageGroupFieldSize" className="block text-sm font-medium text-primary mb-1">Age group field size (optional)</label>
                <input
                  id="ageGroupFieldSize"
                  type="number"
                  min={1}
                  value={ageGroupFieldSize}
                  onChange={e => setAgeGroupFieldSize(e.target.value)}
                  className="w-full border border-border rounded-md px-3 py-2 text-sm bg-surface text-primary"
                />
              </div>
            </div>

            {error && <p className="text-sm text-red-600">{error}</p>}

            <button
              type="submit"
              disabled={!canSave || mutation.isPending}
              className="w-full bg-action text-action-fg rounded-md px-4 py-2 text-sm font-medium hover:bg-action-hover disabled:opacity-50"
            >
              {mutation.isPending ? 'Saving…' : 'Save Event'}
            </button>
          </form>
        </div>
      </main>
    </div>
  )
}
