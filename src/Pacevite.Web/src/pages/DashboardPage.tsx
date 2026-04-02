import { Link } from 'react-router-dom'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { apiClient } from '@/lib/api'
import { useAuth } from '@/hooks/useAuth'
import { formatTime, type EventResponse, type PersonalBestResponse } from '@/lib/types'
import { Upload, Trash2, Trophy, LogOut } from 'lucide-react'

export function DashboardPage() {
  const { user, logout } = useAuth()
  const queryClient = useQueryClient()

  const { data: events = [], isLoading: eventsLoading } = useQuery({
    queryKey: ['events'],
    queryFn: async () => {
      const { data } = await apiClient.get<EventResponse[]>('/events')
      return data
    },
  })

  const { data: personalBests = [] } = useQuery({
    queryKey: ['personal-bests'],
    queryFn: async () => {
      const { data } = await apiClient.get<PersonalBestResponse[]>('/events/personal-bests')
      return data
    },
  })

  const deleteMutation = useMutation({
    mutationFn: (id: string) => apiClient.delete(`/events/${id}`),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['events'] })
      void queryClient.invalidateQueries({ queryKey: ['personal-bests'] })
    },
  })

  return (
    <div className="min-h-screen bg-gray-50">
      {/* Nav */}
      <header className="bg-white border-b border-gray-200 px-6 py-4 flex items-center justify-between">
        <h1 className="text-lg font-semibold text-gray-900">Pacevite</h1>
        <div className="flex items-center gap-4">
          <span className="text-sm text-gray-500">{user?.email}</span>
          <Link
            to="/upload"
            className="inline-flex items-center gap-2 bg-gray-900 text-white text-sm px-3 py-2 rounded-md hover:bg-gray-800"
          >
            <Upload size={14} /> Upload
          </Link>
          <button
            onClick={logout}
            className="inline-flex items-center gap-2 text-sm text-gray-600 hover:text-gray-900"
          >
            <LogOut size={14} /> Sign out
          </button>
        </div>
      </header>

      <main className="max-w-5xl mx-auto px-6 py-8 space-y-8">
        {/* Personal Bests */}
        {personalBests.length > 0 && (
          <section>
            <h2 className="text-sm font-semibold text-gray-500 uppercase tracking-wide mb-3 flex items-center gap-2">
              <Trophy size={14} /> Personal Bests
            </h2>
            <div className="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-4 gap-3">
              {personalBests.map(pb => (
                <div key={pb.eventId} className="bg-white rounded-lg border border-gray-200 p-4">
                  <p className="text-xs font-medium text-gray-500 uppercase">{pb.eventType}</p>
                  <p className="text-2xl font-bold text-gray-900 mt-1">{formatTime(pb.elapsedSecs)}</p>
                  <p className="text-xs text-gray-500 mt-1 truncate">{pb.eventName}</p>
                  <p className="text-xs text-gray-400">{pb.eventDate}</p>
                </div>
              ))}
            </div>
          </section>
        )}

        {/* Event List */}
        <section>
          <h2 className="text-sm font-semibold text-gray-500 uppercase tracking-wide mb-3">
            All Events
          </h2>

          {eventsLoading && <p className="text-sm text-gray-500">Loading…</p>}

          {!eventsLoading && events.length === 0 && (
            <div className="bg-white rounded-lg border border-dashed border-gray-300 p-12 text-center">
              <p className="text-gray-500 text-sm">No events yet.</p>
              <Link to="/upload" className="text-sm font-medium text-gray-900 underline mt-2 inline-block">
                Upload your first event
              </Link>
            </div>
          )}

          {events.length > 0 && (
            <div className="bg-white rounded-lg border border-gray-200 divide-y divide-gray-100">
              {events.map(ev => (
                <div key={ev.id} className="flex items-center justify-between px-4 py-3">
                  <div className="flex items-center gap-4">
                    <span className="text-xs font-medium bg-gray-100 text-gray-700 px-2 py-0.5 rounded uppercase">
                      {ev.eventType}
                    </span>
                    <div>
                      <p className="text-sm font-medium text-gray-900">{ev.eventName}</p>
                      <p className="text-xs text-gray-500">{ev.eventDate}</p>
                    </div>
                  </div>
                  <div className="flex items-center gap-6">
                    <div className="text-right">
                      <p className="text-sm font-semibold text-gray-900">{formatTime(ev.elapsedSecs)}</p>
                      <p className={`text-xs ${ev.completion === 'FINISHED' ? 'text-green-600' : 'text-red-500'}`}>
                        {ev.completion}
                      </p>
                    </div>
                    {ev.overallRank && (
                      <p className="text-xs text-gray-500 w-20 text-right">
                        #{ev.overallRank}{ev.fieldSize ? ` / ${ev.fieldSize}` : ''}
                      </p>
                    )}
                    <button
                      onClick={() => deleteMutation.mutate(ev.id)}
                      disabled={deleteMutation.isPending}
                      className="text-gray-400 hover:text-red-500 disabled:opacity-40"
                      aria-label="Delete event"
                    >
                      <Trash2 size={14} />
                    </button>
                  </div>
                </div>
              ))}
            </div>
          )}
        </section>
      </main>
    </div>
  )
}
