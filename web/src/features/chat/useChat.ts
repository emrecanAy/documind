import { useCallback, useEffect, useRef, useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { api, ApiError, authFetch } from '@/lib/api'
import { readSse } from '@/lib/sse'
import type { AgentStep, ChatMode, Citation, ConversationDetails, MessageUsage, ProblemDetails } from '@/lib/types'

export interface ChatMessageView {
  key: string
  id?: string
  role: 'User' | 'Assistant'
  content: string
  citations: Citation[]
  steps: AgentStep[]
  usage?: MessageUsage
  feedback?: number
  status?: string
  streaming?: boolean
  error?: string
}

let keySeed = 0
const nextKey = () => `m${++keySeed}`

/**
 * Streams answers from POST /chat (Server-Sent Events) and keeps the transcript in local state.
 * Event order: conversation? → status* → (step*) → sources → delta* → done | error
 */
export function useChat(workspaceId: string, conversationId: string | undefined, onConversationCreated: (id: string) => void) {
  const client = useQueryClient()
  const [messages, setMessages] = useState<ChatMessageView[]>([])
  const [streaming, setStreaming] = useState(false)
  // Conversation the transcript currently belongs to (created by our own stream or loaded from history).
  const [owner, setOwner] = useState<string | null>(null)
  const abort = useRef<AbortController | null>(null)

  // Load history when the user opens an existing conversation (but not the one we are streaming into).
  useEffect(() => {
    if (!conversationId || owner === conversationId) return

    let cancelled = false
    api
      .get<ConversationDetails>(`/api/workspaces/${workspaceId}/conversations/${conversationId}`)
      .then((details) =>
        details.messages.map<ChatMessageView>((m) => ({
          key: m.id,
          id: m.id,
          role: m.role,
          content: m.content,
          citations: m.citations,
          steps: m.agentSteps,
          usage: m.usage,
          feedback: m.feedback,
        })),
      )
      .catch(() => [] as ChatMessageView[])
      .then((loaded) => {
        if (cancelled) return
        setMessages(loaded)
        setOwner(conversationId)
      })
    return () => {
      cancelled = true
    }
  }, [workspaceId, conversationId, owner])

  const loading = !!conversationId && owner !== conversationId

  useEffect(() => () => abort.current?.abort(), [])

  const updateLast = (patch: (m: ChatMessageView) => ChatMessageView) =>
    setMessages((list) => list.map((m, i) => (i === list.length - 1 ? patch(m) : m)))

  const send = useCallback(
    async (question: string, mode: ChatMode) => {
      if (streaming) return
      setStreaming(true)
      setMessages((list) => [
        ...list,
        { key: nextKey(), role: 'User', content: question, citations: [], steps: [] },
        { key: nextKey(), role: 'Assistant', content: '', citations: [], steps: [], streaming: true, status: 'Thinking…' },
      ])

      const controller = new AbortController()
      abort.current = controller
      try {
        const response = await authFetch(`/api/workspaces/${workspaceId}/chat`, {
          method: 'POST',
          body: JSON.stringify({ question, conversationId, mode }),
          signal: controller.signal,
        })
        if (!response.ok || !response.body) {
          const problem = (await response.json().catch(() => undefined)) as ProblemDetails | undefined
          throw new ApiError(response.status, problem)
        }

        for await (const { event, data } of readSse(response.body, controller.signal)) {
          const payload = JSON.parse(data)
          switch (event) {
            case 'conversation':
              setOwner(payload.conversationId)
              onConversationCreated(payload.conversationId)
              break
            case 'status':
              updateLast((m) => ({ ...m, status: payload.message }))
              break
            case 'step':
              updateLast((m) => ({ ...m, steps: [...m.steps, payload.step] }))
              break
            case 'sources':
              updateLast((m) => ({ ...m, citations: payload.citations }))
              break
            case 'delta':
              updateLast((m) => ({ ...m, content: m.content + payload.text, status: undefined }))
              break
            case 'done':
              updateLast((m) => ({ ...m, id: payload.messageId, usage: payload.usage, streaming: false, status: undefined }))
              break
            case 'error':
              updateLast((m) => ({ ...m, error: payload.message, streaming: false, status: undefined }))
              break
          }
        }
      } catch (error) {
        if (!controller.signal.aborted) {
          const message = error instanceof Error ? error.message : 'Something went wrong'
          updateLast((m) => ({ ...m, error: message, streaming: false, status: undefined }))
        }
      } finally {
        updateLast((m) => ({ ...m, streaming: false, status: undefined }))
        setStreaming(false)
        void client.invalidateQueries({ queryKey: ['conversations', workspaceId] })
      }
    },
    [client, conversationId, onConversationCreated, streaming, workspaceId],
  )

  const stop = () => abort.current?.abort()

  const giveFeedback = async (message: ChatMessageView, score: 1 | -1) => {
    const cid = conversationId ?? owner
    if (!message.id || !cid) return
    await api.post(`/api/workspaces/${workspaceId}/conversations/${cid}/messages/${message.id}/feedback`, { score })
    setMessages((list) => list.map((m) => (m.key === message.key ? { ...m, feedback: score } : m)))
  }

  const reset = () => {
    abort.current?.abort()
    setOwner(null)
    setMessages([])
  }

  return { messages, loading, streaming, send, stop, giveFeedback, reset }
}
