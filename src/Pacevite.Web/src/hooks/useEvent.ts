import { useQuery } from '@tanstack/react-query'
import { apiClient } from '@/lib/api'
import type { EventResponse } from '@/lib/types'

export function useEvent(id: string | undefined) {
  return useQuery({
    queryKey: ['event', id],
    queryFn: async () => {
      const { data } = await apiClient.get<EventResponse>(`/events/${id}`)
      return data
    },
    enabled: !!id,
  })
}
