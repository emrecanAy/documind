import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '@/lib/api'
import type { Member, RagSettings, SystemInfo, UsageSummary, WorkspaceDetails, WorkspaceRole, WorkspaceSummary } from '@/lib/types'

export const workspaceKeys = {
  all: ['workspaces'] as const,
  detail: (id: string) => ['workspaces', id] as const,
  usage: (id: string) => ['workspaces', id, 'usage'] as const,
}

export function useWorkspaces() {
  return useQuery({ queryKey: workspaceKeys.all, queryFn: () => api.get<WorkspaceSummary[]>('/api/workspaces') })
}

export function useWorkspace(id: string | undefined) {
  return useQuery({
    queryKey: workspaceKeys.detail(id ?? ''),
    queryFn: () => api.get<WorkspaceDetails>(`/api/workspaces/${id}`),
    enabled: !!id,
  })
}

export function useUsage(id: string) {
  return useQuery({ queryKey: workspaceKeys.usage(id), queryFn: () => api.get<UsageSummary>(`/api/workspaces/${id}/usage`) })
}

export function useSystemInfo() {
  return useQuery({ queryKey: ['system-info'], queryFn: () => api.get<SystemInfo>('/api/system/info'), staleTime: Infinity })
}

export function useCreateWorkspace() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (body: { name: string; description?: string }) => api.post<WorkspaceDetails>('/api/workspaces', body),
    onSuccess: () => client.invalidateQueries({ queryKey: workspaceKeys.all }),
  })
}

export function useUpdateWorkspace(id: string) {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (body: { name: string; description?: string }) => api.put<WorkspaceDetails>(`/api/workspaces/${id}`, body),
    onSuccess: (data) => {
      client.setQueryData(workspaceKeys.detail(id), data)
      void client.invalidateQueries({ queryKey: workspaceKeys.all })
    },
  })
}

export function useUpdateSettings(id: string) {
  const client = useQueryClient()
  return useMutation({
    mutationFn: ({ settings, reindex }: { settings: RagSettings; reindex: boolean }) =>
      api.put<WorkspaceDetails>(`/api/workspaces/${id}/settings?reindex=${reindex}`, settings),
    onSuccess: (data) => {
      client.setQueryData(workspaceKeys.detail(id), data)
      void client.invalidateQueries({ queryKey: ['documents', id] })
    },
  })
}

export function useDeleteWorkspace() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => api.delete(`/api/workspaces/${id}`),
    onSuccess: () => client.invalidateQueries({ queryKey: workspaceKeys.all }),
  })
}

export function useAddMember(id: string) {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (body: { email: string; role: WorkspaceRole }) => api.post<Member>(`/api/workspaces/${id}/members`, body),
    onSuccess: () => client.invalidateQueries({ queryKey: workspaceKeys.detail(id) }),
  })
}

export function useRemoveMember(id: string) {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (userId: string) => api.delete(`/api/workspaces/${id}/members/${userId}`),
    onSuccess: () => client.invalidateQueries({ queryKey: workspaceKeys.detail(id) }),
  })
}

export const canEdit = (role?: WorkspaceRole) => role === 'Editor' || role === 'Owner'
export const isOwner = (role?: WorkspaceRole) => role === 'Owner'
