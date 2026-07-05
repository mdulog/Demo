import { useQuery } from '@tanstack/react-query'
import { apiClient } from '@/lib/api'
import type { TimelineEntry } from '@/lib/types'

export function useTimeline() {
  return useQuery({
    queryKey: ['timeline'],
    queryFn: async () => {
      const { data } = await apiClient.get<TimelineEntry[]>('/events/timeline')
      return data
    },
  })
}
