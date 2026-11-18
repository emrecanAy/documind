import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useNavigate, useParams } from 'react-router'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  Bot, BookOpen, ChevronDown, Coins, FileText, Gauge, MessageSquarePlus, Search, Send, Sparkles, Square, ThumbsDown,
  ThumbsUp, Trash2, Wrench,
} from 'lucide-react'
import { Badge, Button, Spinner, Textarea } from '@/components/ui'
import { useSystemInfo, useWorkspace } from '@/features/workspaces/api'
import { api } from '@/lib/api'
import type { ChatMode, Citation, ConversationSummary } from '@/lib/types'
import { cn } from '@/lib/utils'
import { MessageContent } from './MessageContent'
import { useChat, type ChatMessageView } from './useChat'

const suggestions = [
  'How many days of annual leave do I get?',
  'What are the password requirements?',
  'What is the hotel limit for a trip to London?',
  'Yurt dışından yılda kaç gün çalışabilirim?',
]

export function ChatPage() {
  const { workspaceId = '', conversationId } = useParams()
  const navigate = useNavigate()
  const client = useQueryClient()
  const { data: workspace } = useWorkspace(workspaceId)
  const { data: info } = useSystemInfo()
  const [mode, setMode] = useState<ChatMode>('Rag')
  const [input, setInput] = useState('')
  const [selected, setSelected] = useState<{ messageKey: string; citation: number | null } | null>(null)
  const scroller = useRef<HTMLDivElement>(null)

  const conversations = useQuery({
    queryKey: ['conversations', workspaceId],
    queryFn: () => api.get<ConversationSummary[]>(`/api/workspaces/${workspaceId}/conversations`),
  })
  const deleteConversation = useMutation({
    mutationFn: (id: string) => api.delete(`/api/workspaces/${workspaceId}/conversations/${id}`),
    onSuccess: (_, id) => {
      void client.invalidateQueries({ queryKey: ['conversations', workspaceId] })
      if (id === conversationId) navigate(`/w/${workspaceId}/chat`)
    },
  })

  const onCreated = useCallback(
    (id: string) => navigate(`/w/${workspaceId}/chat/${id}`, { replace: true }),
    [navigate, workspaceId],
  )
  const chat = useChat(workspaceId, conversationId, onCreated)

  useEffect(() => {
    scroller.current?.scrollTo({ top: scroller.current.scrollHeight, behavior: 'smooth' })
  }, [chat.messages])

  // The source panel shows the selected answer's citations, defaulting to the latest answer.
  const lastAssistant = [...chat.messages].reverse().find((m) => m.role === 'Assistant')
  const focused = chat.messages.find((m) => m.key === selected?.messageKey) ?? lastAssistant
  const activeCitation = focused && selected?.messageKey === focused.key ? selected.citation : null

  const submit = (question = input) => {
    const text = question.trim()
    if (!text || chat.streaming) return
    setInput('')
    setSelected(null)
    void chat.send(text, mode)
  }

  return (
    <div className="flex h-full">
      {/* History */}
      <div className="hidden w-64 shrink-0 flex-col border-r border-slate-200 bg-slate-50/60 xl:flex">
        <div className="p-3">
          <Button
            variant="secondary"
            className="w-full"
            onClick={() => {
              chat.reset()
              navigate(`/w/${workspaceId}/chat`)
            }}
          >
            <MessageSquarePlus className="h-4 w-4" /> New conversation
          </Button>
        </div>
        <div className="flex-1 space-y-0.5 overflow-y-auto px-2 pb-3">
          {conversations.data?.map((c) => (
            <div
              key={c.id}
              className={cn(
                'group flex items-center rounded-lg text-sm',
                c.id === conversationId ? 'bg-white shadow-sm ring-1 ring-slate-200' : 'hover:bg-white',
              )}
            >
              <button className="min-w-0 flex-1 truncate px-3 py-2 text-left text-slate-700" onClick={() => navigate(`/w/${workspaceId}/chat/${c.id}`)}>
                {c.title}
              </button>
              <button
                className="mr-1 hidden rounded p-1 text-slate-400 hover:text-red-600 group-hover:block"
                title="Delete conversation"
                onClick={() => deleteConversation.mutate(c.id)}
              >
                <Trash2 className="h-3.5 w-3.5" />
              </button>
            </div>
          ))}
          {conversations.data?.length === 0 && <p className="px-3 py-2 text-xs text-slate-400">No conversations yet.</p>}
        </div>
      </div>

      {/* Conversation */}
      <div className="flex min-w-0 flex-1 flex-col">
        <header className="flex items-center justify-between border-b border-slate-200 bg-white px-6 py-3">
          <div className="min-w-0">
            <h1 className="truncate font-semibold text-slate-900">{workspace?.name ?? 'Ask'}</h1>
            <p className="text-xs text-slate-500">{info ? `${info.chatProvider} · ${info.chatModel}` : ' '}</p>
          </div>
          <ModeToggle mode={mode} onChange={setMode} />
        </header>

        <div ref={scroller} className="flex-1 overflow-y-auto">
          <div className="mx-auto max-w-3xl px-6 py-6">
            {chat.loading ? (
              <Spinner />
            ) : chat.messages.length === 0 ? (
              <Welcome onPick={submit} />
            ) : (
              <div className="space-y-6">
                {chat.messages.map((message) =>
                  message.role === 'User' ? (
                    <div key={message.key} className="flex justify-end">
                      <div className="max-w-[85%] rounded-2xl rounded-br-md bg-brand-600 px-4 py-2.5 text-[15px] text-white shadow-sm">{message.content}</div>
                    </div>
                  ) : (
                    <AssistantMessage
                      key={message.key}
                      message={message}
                      focused={focused?.key === message.key}
                      activeCitation={focused?.key === message.key ? activeCitation : null}
                      onCite={(n) => setSelected({ messageKey: message.key, citation: n })}
                      onFocus={() => setSelected({ messageKey: message.key, citation: null })}
                      onFeedback={(score) => chat.giveFeedback(message, score)}
                    />
                  ),
                )}
              </div>
            )}
          </div>
        </div>

        <div className="border-t border-slate-200 bg-white px-6 py-4">
          <form
            className="mx-auto flex max-w-3xl items-end gap-2"
            onSubmit={(e) => {
              e.preventDefault()
              submit()
            }}
          >
            <Textarea
              rows={1}
              value={input}
              placeholder={mode === 'Agent' ? 'Ask a multi-part question — the agent will plan its searches…' : 'Ask anything about your documents…'}
              className="max-h-40 min-h-11 resize-none py-2.5"
              onChange={(e) => setInput(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === 'Enter' && !e.shiftKey) {
                  e.preventDefault()
                  submit()
                }
              }}
            />
            {chat.streaming ? (
              <Button type="button" variant="secondary" size="lg" onClick={chat.stop} title="Stop"><Square className="h-4 w-4" /></Button>
            ) : (
              <Button type="submit" size="lg" disabled={!input.trim()} title="Send"><Send className="h-4 w-4" /></Button>
            )}
          </form>
          <p className="mx-auto mt-2 max-w-3xl text-center text-xs text-slate-400">
            Answers are generated only from documents in this workspace and cite their sources.
          </p>
        </div>
      </div>

      {/* Sources */}
      <SourcesPanel citations={focused?.citations ?? []} active={activeCitation} onSelect={(n) => focused && setSelected({ messageKey: focused.key, citation: n })} />
    </div>
  )
}

function ModeToggle({ mode, onChange }: { mode: ChatMode; onChange: (mode: ChatMode) => void }) {
  const options: { value: ChatMode; label: string; icon: typeof Search; hint: string }[] = [
    { value: 'Rag', label: 'RAG', icon: Search, hint: 'Single retrieval pass: rewrite → hybrid search → rerank → answer' },
    { value: 'Agent', label: 'Agent', icon: Bot, hint: 'LLM plans and calls search tools multiple times' },
  ]
  return (
    <div className="flex rounded-lg bg-slate-100 p-0.5">
      {options.map(({ value, label, icon: Icon, hint }) => (
        <button
          key={value}
          title={hint}
          onClick={() => onChange(value)}
          className={cn(
            'flex items-center gap-1.5 rounded-md px-3 py-1.5 text-sm font-medium transition',
            mode === value ? 'bg-white text-slate-900 shadow-sm' : 'text-slate-500 hover:text-slate-800',
          )}
        >
          <Icon className="h-3.5 w-3.5" /> {label}
        </button>
      ))}
    </div>
  )
}

function Welcome({ onPick }: { onPick: (q: string) => void }) {
  return (
    <div className="flex flex-col items-center pt-12 text-center">
      <div className="rounded-2xl bg-brand-50 p-4 text-brand-600"><Sparkles className="h-8 w-8" /></div>
      <h2 className="mt-4 text-xl font-semibold text-slate-900">Ask your knowledge base</h2>
      <p className="mt-1 max-w-md text-sm text-slate-500">
        DocuMind searches your documents with hybrid semantic + keyword retrieval and answers with citations you can verify.
      </p>
      <div className="mt-8 grid w-full max-w-xl gap-2 sm:grid-cols-2">
        {suggestions.map((q) => (
          <button key={q} onClick={() => onPick(q)} className="rounded-xl border border-slate-200 bg-white px-4 py-3 text-left text-sm text-slate-700 shadow-sm transition hover:border-brand-300 hover:text-brand-700">
            {q}
          </button>
        ))}
      </div>
    </div>
  )
}

function AssistantMessage({
  message,
  focused,
  activeCitation,
  onCite,
  onFocus,
  onFeedback,
}: {
  message: ChatMessageView
  focused: boolean
  activeCitation: number | null
  onCite: (n: number) => void
  onFocus: () => void
  onFeedback: (score: 1 | -1) => void
}) {
  const [showSteps, setShowSteps] = useState(false)

  return (
    <div className="flex gap-3" onClick={onFocus}>
      <div className="mt-1 flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-brand-600 text-white">
        <Sparkles className="h-4 w-4" />
      </div>
      <div className={cn('min-w-0 flex-1 rounded-2xl rounded-tl-md border bg-white px-4 py-3 shadow-sm', focused ? 'border-brand-200' : 'border-slate-200')}>
        {message.steps.length > 0 && (
          <div className="mb-2">
            <button onClick={() => setShowSteps((v) => !v)} className="flex items-center gap-1 text-xs font-medium text-slate-500 hover:text-slate-800">
              <Wrench className="h-3.5 w-3.5" /> {message.steps.length} pipeline step{message.steps.length > 1 ? 's' : ''}
              <ChevronDown className={cn('h-3.5 w-3.5 transition', showSteps && 'rotate-180')} />
            </button>
            {showSteps && (
              <ol className="mt-2 space-y-1.5 border-l-2 border-slate-100 pl-3">
                {message.steps.map((step, i) => (
                  <li key={i} className="text-xs text-slate-600">
                    <Badge tone={step.kind === 'tool_result' ? 'green' : 'blue'} className="mr-1.5">{step.kind}</Badge>
                    <span className="font-mono font-medium">{step.name}</span>
                    {step.input && <span className="block truncate font-mono text-slate-400" title={step.input}>→ {step.input}</span>}
                    {step.output && <span className="block line-clamp-2 whitespace-pre-wrap text-slate-500">{step.output}</span>}
                  </li>
                ))}
              </ol>
            )}
          </div>
        )}

        {message.content ? (
          <MessageContent content={message.content} activeCitation={activeCitation} onCite={onCite} />
        ) : message.status ? (
          <p className="flex items-center gap-2 text-sm text-slate-500"><Spinner className="h-4 w-4" /> {message.status}</p>
        ) : null}
        {message.streaming && message.content && <span className="inline-block h-4 w-1.5 animate-pulse bg-brand-500 align-middle" />}
        {message.error && <p className="mt-2 rounded-lg bg-red-50 px-3 py-2 text-sm text-red-700">{message.error}</p>}

        {!message.streaming && message.usage && (
          <div className="mt-3 flex flex-wrap items-center gap-3 border-t border-slate-100 pt-2 text-xs text-slate-400">
            <span className="flex items-center gap-1" title="Latency"><Gauge className="h-3.5 w-3.5" /> {(message.usage.latencyMs / 1000).toFixed(1)}s</span>
            <span className="flex items-center gap-1" title="Tokens in / out"><BookOpen className="h-3.5 w-3.5" /> {message.usage.inputTokens} / {message.usage.outputTokens} tok</span>
            <span className="flex items-center gap-1" title="Estimated cost"><Coins className="h-3.5 w-3.5" /> ${message.usage.estimatedCostUsd.toFixed(4)}</span>
            <span className="ml-auto flex gap-1">
              <button title="Helpful" onClick={() => onFeedback(1)} className={cn('rounded p-1 hover:bg-slate-100', message.feedback === 1 && 'text-emerald-600')}>
                <ThumbsUp className="h-3.5 w-3.5" />
              </button>
              <button title="Not helpful" onClick={() => onFeedback(-1)} className={cn('rounded p-1 hover:bg-slate-100', message.feedback === -1 && 'text-red-600')}>
                <ThumbsDown className="h-3.5 w-3.5" />
              </button>
            </span>
          </div>
        )}
      </div>
    </div>
  )
}

function SourcesPanel({ citations, active, onSelect }: { citations: Citation[]; active: number | null; onSelect: (n: number) => void }) {
  const refs = useRef<Record<number, HTMLDivElement | null>>({})
  useEffect(() => {
    if (active) refs.current[active]?.scrollIntoView({ behavior: 'smooth', block: 'nearest' })
  }, [active])

  const sorted = useMemo(() => [...citations].sort((a, b) => a.number - b.number), [citations])

  return (
    <aside className="hidden w-80 shrink-0 flex-col border-l border-slate-200 bg-white lg:flex">
      <div className="border-b border-slate-200 px-4 py-3">
        <h2 className="text-sm font-semibold text-slate-900">Sources</h2>
        <p className="text-xs text-slate-500">Passages the answer was grounded on</p>
      </div>
      <div className="flex-1 space-y-2 overflow-y-auto p-3">
        {sorted.length === 0 && <p className="px-1 py-4 text-center text-xs text-slate-400">Sources appear here once you ask a question.</p>}
        {sorted.map((c) => (
          <div
            key={c.chunkId}
            ref={(el) => {
              refs.current[c.number] = el
            }}
            onClick={() => onSelect(c.number)}
            className={cn(
              'cursor-pointer rounded-lg border p-3 transition',
              active === c.number ? 'border-brand-400 bg-brand-50/60 ring-2 ring-brand-100' : 'border-slate-200 hover:border-slate-300',
            )}
          >
            <div className="mb-1.5 flex items-center gap-2">
              <span className="flex h-5 min-w-5 items-center justify-center rounded-md bg-brand-100 px-1 text-[11px] font-semibold text-brand-700">{c.number}</span>
              <FileText className="h-3.5 w-3.5 shrink-0 text-slate-400" />
              <span className="truncate text-xs font-medium text-slate-700" title={c.fileName}>{c.fileName}</span>
            </div>
            <div className="mb-1.5 flex flex-wrap gap-1">
              {c.page && <Badge>p. {c.page}</Badge>}
              {c.section && <Badge tone="blue" className="max-w-full truncate">{c.section}</Badge>}
              <Badge tone="gray">score {c.score.toFixed(3)}</Badge>
            </div>
            <p className="line-clamp-6 whitespace-pre-wrap text-xs leading-relaxed text-slate-600">{c.snippet}</p>
          </div>
        ))}
      </div>
    </aside>
  )
}
