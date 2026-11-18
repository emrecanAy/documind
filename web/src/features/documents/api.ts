import { useEffect } from 'react'
import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '@/lib/api'
import type { ChunkDto, DocumentDto, DocumentStatusChanged } from '@/lib/types'
import { useAuthStore } from '@/stores/auth'

export const documentKeys = {
  list: (workspaceId: string) => ['documents', workspaceId] as const,
  chunks: (workspaceId: string, documentId: string) => ['documents', workspaceId, documentId, 'chunks'] as const,
}

export function useDocuments(workspaceId: string) {
  return useQuery({
    queryKey: documentKeys.list(workspaceId),
    queryFn: () => api.get<DocumentDto[]>(`/api/workspaces/${workspaceId}/documents`),
    // Fallback polling while something is in flight (SignalR pushes updates in real time).
    refetchInterval: (query) =>
      query.state.data?.some((d) => d.status === 'Queued' || d.status === 'Processing') ? 5000 : false,
  })
}

export function useChunks(workspaceId: string, documentId: string | null) {
  return useQuery({
    queryKey: documentKeys.chunks(workspaceId, documentId ?? ''),
    queryFn: () => api.get<ChunkDto[]>(`/api/workspaces/${workspaceId}/documents/${documentId}/chunks`),
    enabled: !!documentId,
  })
}

export function useUploadDocuments(workspaceId: string) {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (files: File[]) => api.upload<DocumentDto[]>(`/api/workspaces/${workspaceId}/documents`, files),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: documentKeys.list(workspaceId) })
      void client.invalidateQueries({ queryKey: ['workspaces'] })
    },
  })
}

export function useDocumentAction(workspaceId: string) {
  const client = useQueryClient()
  const invalidate = () => client.invalidateQueries({ queryKey: documentKeys.list(workspaceId) })
  return {
    remove: useMutation({
      mutationFn: (id: string) => api.delete(`/api/workspaces/${workspaceId}/documents/${id}`),
      onSuccess: invalidate,
    }),
    reindex: useMutation({
      mutationFn: (id: string) => api.post(`/api/workspaces/${workspaceId}/documents/${id}/reindex`),
      onSuccess: invalidate,
    }),
    reindexAll: useMutation({
      mutationFn: () => api.post(`/api/workspaces/${workspaceId}/documents/reindex`),
      onSuccess: invalidate,
    }),
  }
}

/** Live ingestion progress: the worker publishes via Postgres NOTIFY → API → SignalR group per workspace. */
export function useDocumentStatusStream(workspaceId: string) {
  const client = useQueryClient()

  useEffect(() => {
    const connection = new HubConnectionBuilder()
      .withUrl('/hubs/documents', { accessTokenFactory: () => useAuthStore.getState().accessToken ?? '' })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build()

    connection.on('documentStatus', (change: DocumentStatusChanged) => {
      client.setQueryData<DocumentDto[]>(documentKeys.list(workspaceId), (documents) =>
        documents?.map((d) =>
          d.id === change.documentId
            ? { ...d, status: change.status, progress: change.progress, chunkCount: change.chunkCount, error: change.error }
            : d,
        ),
      )
      if (change.status === 'Indexed' || change.status === 'Failed') {
        void client.invalidateQueries({ queryKey: documentKeys.list(workspaceId) })
      }
    })

    connection.onreconnected(() => connection.invoke('JoinWorkspace', workspaceId))
    let disposed = false
    const started = connection
      .start()
      .then(() => (disposed ? undefined : connection.invoke('JoinWorkspace', workspaceId)))
      .catch(() => undefined) // polling fallback keeps the UI correct

    return () => {
      disposed = true
      // stopping mid-negotiation logs an error (StrictMode double-mount), so wait for start to settle
      void started.finally(() => connection.stop())
    }
  }, [workspaceId, client])
}

export async function downloadDocument(workspaceId: string, doc: DocumentDto) {
  const { authFetch } = await import('@/lib/api')
  const response = await authFetch(`/api/workspaces/${workspaceId}/documents/${doc.id}/download`)
  const url = URL.createObjectURL(await response.blob())
  const link = Object.assign(document.createElement('a'), { href: url, download: doc.fileName })
  link.click()
  URL.revokeObjectURL(url)
}
