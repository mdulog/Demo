import { useQuery } from '@tanstack/react-query'
import { apiClient } from '@/lib/api'
import type { EventResponse } from '@/lib/types'

export function useEvents() {
  return useQuery({
    queryKey: ['events'],
    queryFn: async () => {
      const { data } = await apiClient.get<EventResponse[]>('/events')
      return data
    },
  })
}
