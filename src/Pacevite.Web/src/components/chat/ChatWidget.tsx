import { useState } from 'react'
import { ChatPanel } from './ChatPanel'
import { useChatStream } from '@/hooks/useChatStream'
import { useAuth } from '@/hooks/useAuth'

export function ChatWidget() {
  const { isAuthenticated } = useAuth()
  const [isOpen, setIsOpen] = useState(false)
  const chat = useChatStream()

  if (!isAuthenticated) return null

  return (
    <div className="fixed bottom-6 right-6 z-50 flex flex-col items-end gap-3">
      {isOpen && (
        <div className="w-80 h-[480px] bg-white rounded-2xl shadow-2xl border border-gray-200 flex flex-col overflow-hidden">
          <ChatPanel chat={chat} />
        </div>
      )}

      <button
        onClick={() => setIsOpen(prev => !prev)}
        aria-label={isOpen ? 'Close chat' : 'Open chat assistant'}
        className="w-14 h-14 rounded-full bg-blue-600 text-white shadow-lg hover:bg-blue-700 active:scale-95 transition-all flex items-center justify-center text-2xl"
      >
        {isOpen ? '✕' : '💬'}
      </button>
    </div>
  )
}
