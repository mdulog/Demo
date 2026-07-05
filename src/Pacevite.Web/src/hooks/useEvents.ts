import { useInfiniteQuery } from '@tanstack/react-query'
import { apiClient } from '@/lib/api'
import type { PagedEventsResponse } from '@/lib/types'

export interface EventsFilters {
  search?: string
  eventType?: string
}

export function useEvents(filters: EventsFilters = {}) {
  return useInfiniteQuery({
    queryKey: ['events', filters],
    queryFn: async ({ pageParam }) => {
      const params = new URLSearchParams()
      if (pageParam) params.set('cursor', pageParam)
      if (filters.search) params.set('search', filters.search)
      if (filters.eventType) params.set('eventType', filters.eventType)
      const qs = params.toString()
      const { data } = await apiClient.get<PagedEventsResponse>(`/events${qs ? `?${qs}` : ''}`)
      return data
    },
    initialPageParam: null as string | null,
    getNextPageParam: last => last.nextCursor,
  })
}
