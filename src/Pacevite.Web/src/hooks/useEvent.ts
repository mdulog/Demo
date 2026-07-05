import { useQuery } from '@tanstack/react-query'
import { apiClient } from '@/lib/api'
import type { EventDetailResponse } from '@/lib/types'

export function useEvent(id: string | undefined) {
  return useQuery({
    queryKey: ['event', id],
    queryFn: async () => {
      const { data } = await apiClient.get<EventDetailResponse>(`/events/${id}`)
      return data
    },
    enabled: !!id,
  })
}
