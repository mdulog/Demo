import { useQuery } from '@tanstack/react-query'
import { apiClient } from '@/lib/api'
import type { PredictionResponse } from '@/lib/types'

export function usePrediction(eventType: string | null) {
  return useQuery<PredictionResponse>({
    queryKey: ['prediction', eventType],
    queryFn: async () => {
      const { data } = await apiClient.get<PredictionResponse>(
        `/events/prediction?eventType=${eventType}`
      )
      return data
    },
    enabled: eventType !== null,
    retry: false,
  })
}
