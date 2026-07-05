import { Link, useSearchParams } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { isAxiosError } from 'axios'
import { apiClient } from '@/lib/api'
import { ThemeToggle } from '@/components/ThemeToggle'
import { ArrowLeft, Link2 } from 'lucide-react'
import { formatTime, type ConnectStravaResponse, type StravaActivityPreviewResponse } from '@/lib/types'

export function SyncPage() {
  const queryClient = useQueryClient()
  const [searchParams] = useSearchParams()
  const connected = searchParams.get('connected')

  const activitiesQuery = useQuery({
    queryKey: ['strava-activities'],
    queryFn: async () => {
      const { data } = await apiClient.get<StravaActivityPreviewResponse[]>('/sync/strava/activities')
      return data
    },
    retry: false,
  })

  const isNotConnected = isAxiosError(activitiesQuery.error) && activitiesQuery.error.response?.status === 409

  const connectMutation = useMutation({
    mutationFn: async () => {
      const { data } = await apiClient.get<ConnectStravaResponse>('/sync/strava/connect')
      return data
    },
    onSuccess: (data) => {
      window.location.href = data.authorizeUrl
    },
  })

  const confirmMutation = useMutation({
    mutationFn: async (activity: StravaActivityPreviewResponse) => {
      await apiClient.post('/sync/strava/activities/confirm', {
        externalActivityId: activity.externalActivityId,
        name: activity.name,
        eventDate: activity.eventDate,
        elapsedSecs: activity.elapsedSecs,
      })
      return activity
    },
    onSuccess: (activity) => {
      queryClient.setQueryData<StravaActivityPreviewResponse[]>(
        ['strava-activities'],
        old => (old ?? []).filter(a => a.externalActivityId !== activity.externalActivityId)
      )
      void queryClient.invalidateQueries({ queryKey: ['events'] })
      void queryClient.invalidateQueries({ queryKey: ['personal-bests'] })
      void queryClient.invalidateQueries({ queryKey: ['timeline'] })
    },
  })

  return (
    <div className="min-h-screen bg-bg">
      <header className="bg-surface border-b border-border px-6 py-4 flex items-center gap-4">
        <Link to="/dashboard" className="text-secondary hover:text-primary">
          <ArrowLeft size={16} />
        </Link>
        <h1 className="text-lg font-semibold text-primary flex-1">Sync</h1>
        <ThemeToggle />
      </header>

      <main className="max-w-2xl mx-auto px-6 py-12 space-y-6">
        {connected === 'true' && (
          <p className="text-sm bg-green-100 text-green-800 rounded-md px-4 py-2">
            Strava connected! Confirm the races below to add them to your events.
          </p>
        )}
        {connected === 'false' && (
          <p className="text-sm bg-red-100 text-red-700 rounded-md px-4 py-2">
            Couldn't connect to Strava. Please try again.
          </p>
        )}

        <div className="bg-surface rounded-lg border border-border p-8 space-y-6">
          {isNotConnected && (
            <div className="text-center space-y-4">
              <p className="text-sm text-secondary">
                Connect your Strava account to import races as Pacevite events.
              </p>
              <button
                onClick={() => connectMutation.mutate()}
                disabled={connectMutation.isPending}
                className="inline-flex items-center gap-2 bg-action text-action-fg rounded-md px-4 py-2 text-sm font-medium hover:bg-action-hover disabled:opacity-50"
              >
                <Link2 size={14} /> {connectMutation.isPending ? 'Connecting…' : 'Connect Strava'}
              </button>
            </div>
          )}

          {activitiesQuery.isSuccess && (
            <div className="space-y-3">
              <h2 className="text-sm font-semibold text-secondary uppercase tracking-wide">
                Recent Strava Activities
              </h2>

              {activitiesQuery.data.length === 0 && (
                <p className="text-sm text-secondary">No new activities to import.</p>
              )}

              {activitiesQuery.data.map(activity => (
                <div
                  key={activity.externalActivityId}
                  data-testid="strava-activity-row"
                  className="flex items-center justify-between border border-border rounded-md px-4 py-3"
                >
                  <div>
                    <p className="text-sm font-medium text-primary">{activity.name}</p>
                    <p className="text-xs text-secondary">
                      {activity.eventDate} · {formatTime(activity.elapsedSecs)}
                    </p>
                    {activity.possibleDuplicate && (
                      <p className="text-xs text-amber-700 mt-1">Possible duplicate of an existing event</p>
                    )}
                  </div>
                  <button
                    onClick={() => confirmMutation.mutate(activity)}
                    disabled={confirmMutation.isPending}
                    className="text-xs font-medium bg-action text-action-fg rounded-md px-3 py-1.5 hover:bg-action-hover disabled:opacity-50"
                  >
                    Confirm
                  </button>
                </div>
              ))}
            </div>
          )}
        </div>
      </main>
    </div>
  )
}
