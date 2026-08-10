import type { QueryClient } from '@tanstack/react-query'

export const residentQueryKeys = {
  residents: ['residents'] as const,
  rooms: ['resident-rooms'] as const,
  dormCheckSuites: ['dorm-check-suites'] as const,
}

export const sharedResidentQueryOptions = {
  staleTime: 0,
  refetchOnWindowFocus: true,
  refetchOnReconnect: true,
  refetchInterval: 15_000,
} as const

export async function refreshResidentData(queryClient: QueryClient) {
  await Promise.all([
    queryClient.invalidateQueries({ queryKey: residentQueryKeys.residents }),
    queryClient.invalidateQueries({ queryKey: residentQueryKeys.rooms }),
    queryClient.invalidateQueries({ queryKey: residentQueryKeys.dormCheckSuites }),
  ])
}
