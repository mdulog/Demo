import { useCallback, useRef, useState } from 'react'
import { streamChatMessage, type ConversationMessage } from '@/lib/chatApi'

export interface ChatMessage {
  id: string
  role: 'user' | 'assistant'
  content: string
}

export interface UseChatStreamResult {
  messages: ChatMessage[]
  streamingText: string
  toolStatus: string
  isLoading: boolean
  error: string | null
  sendMessage: (text: string) => Promise<void>
  clearError: () => void
}

export function useChatStream(): UseChatStreamResult {
  const [messages, setMessages] = useState<ChatMessage[]>([])
  const [streamingText, setStreamingText] = useState('')
  const [toolStatus, setToolStatus] = useState('')
  const [isLoading, setIsLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const abortRef = useRef<AbortController | null>(null)

  const sendMessage = useCallback(async (text: string) => {
    if (isLoading) return

    // Cancel any in-flight stream before starting a new one
    abortRef.current?.abort()
    abortRef.current = new AbortController()

    const userMessage: ChatMessage = { id: crypto.randomUUID(), role: 'user', content: text }
    setMessages(prev => [...prev, userMessage])
    setStreamingText('')
    setToolStatus('')
    setError(null)
    setIsLoading(true)

    // Snapshot committed messages as history (the user turn we just added is the current message)
    const history: ConversationMessage[] = messages.map(m => ({
      role: m.role,
      content: m.content,
    }))

    let accumulated = ''

    try {
      await streamChatMessage(
        text,
        history,
        {
          onDelta: delta => {
            accumulated += delta
            setStreamingText(accumulated)
          },
          onToolStart: (_tool, label) => setToolStatus(label),
          onToolEnd: () => setToolStatus(''),
          onDone: () => {
            const assistantMessage: ChatMessage = {
              id: crypto.randomUUID(),
              role: 'assistant',
              content: accumulated,
            }
            setMessages(prev => [...prev, assistantMessage])
            setStreamingText('')
            setToolStatus('')
            setIsLoading(false)
          },
          onError: message => {
            setError(message)
            setStreamingText('')
            setToolStatus('')
            setIsLoading(false)
          },
        },
        abortRef.current.signal,
      )
    } catch (err) {
      if (err instanceof Error && err.name !== 'AbortError') {
        setError('Connection lost. Please try again.')
      }
      setStreamingText('')
      setToolStatus('')
      setIsLoading(false)
    }
  }, [isLoading, messages])

  return {
    messages,
    streamingText,
    toolStatus,
    isLoading,
    error,
    sendMessage,
    clearError: () => setError(null),
  }
}
