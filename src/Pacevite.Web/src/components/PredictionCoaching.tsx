import { useState } from 'react'
import { tokenStore } from '@/lib/api'
import { Sparkles } from 'lucide-react'

interface Props {
  eventType: string
}

export function PredictionCoaching({ eventType }: Props) {
  const [coachingText, setCoachingText] = useState('')
  const [isStreaming, setIsStreaming]   = useState(false)
  const [error, setError]              = useState<string | null>(null)
  const [generated, setGenerated]      = useState(false)

  async function handleGenerate() {
    setIsStreaming(true)
    setCoachingText('')
    setError(null)
    setGenerated(true)

    const token = tokenStore.get()

    try {
      const response = await fetch(
        `/api/events/prediction/coaching?eventType=${eventType}`,
        { headers: token ? { Authorization: `Bearer ${token}` } : {} }
      )

      if (!response.ok || !response.body) {
        setError('Failed to generate coaching analysis. Please try again.')
        return
      }

      const reader     = response.body.getReader()
      const decoder    = new TextDecoder()
      let buffer       = ''
      let sseEventType = ''

      while (true) {
        const { done, value } = await reader.read()
        if (done) break

        buffer += decoder.decode(value, { stream: true })
        const lines = buffer.split('\n')
        buffer = lines.pop() ?? ''

        for (const line of lines) {
          if (line.startsWith('event: ')) {
            sseEventType = line.slice(7).trim()
          } else if (line.startsWith('data: ')) {
            const raw = line.slice(6)
            if (sseEventType === 'delta') {
              const parsed = JSON.parse(raw) as { text: string }
              setCoachingText(prev => prev + parsed.text)
            } else if (sseEventType === 'done') {
              setIsStreaming(false)
            } else if (sseEventType === 'error') {
              const parsed = JSON.parse(raw) as { message: string }
              setError(parsed.message)
              setIsStreaming(false)
            }
            sseEventType = ''
          }
        }
      }
    } catch {
      setError('Coaching analysis failed. Please try again.')
    } finally {
      setIsStreaming(false)
    }
  }

  return (
    <div className="bg-surface border border-border rounded-xl p-5">
      <div className="flex items-center justify-between mb-2">
        <h3 className="text-sm font-semibold text-primary flex items-center gap-2">
          <Sparkles size={14} className="text-indigo-500" /> AI Coaching Analysis
        </h3>
        {!isStreaming && (
          <button
            onClick={handleGenerate}
            className="inline-flex items-center gap-1.5 bg-action text-action-fg text-xs px-3 py-1.5 rounded-md hover:bg-action-hover"
          >
            {generated ? 'Regenerate' : 'Generate analysis'}
          </button>
        )}
        {isStreaming && (
          <span className="text-xs text-secondary">Generating…</span>
        )}
      </div>

      {!generated && (
        <p className="text-sm text-secondary">
          Claude will analyse your split history and highlight where your next minutes are hiding.
        </p>
      )}

      {error && (
        <p className="text-sm text-red-500 mt-2">{error}</p>
      )}

      {coachingText && (
        <p data-testid="coaching-text" className="text-sm text-primary leading-relaxed whitespace-pre-wrap mt-2">
          {coachingText}
          {isStreaming && (
            <span className="inline-block w-2 h-4 bg-indigo-500 ml-0.5 align-text-bottom rounded-sm animate-pulse" />
          )}
        </p>
      )}
    </div>
  )
}
