import { useRef, useState } from 'react'
import { useParams } from 'react-router'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { toast } from 'sonner'
import { CheckCircle2, FlaskConical, Upload, XCircle } from 'lucide-react'
import { Badge, Button, Card, CardHeader, EmptyState, PageHeader, Spinner, Switch } from '@/components/ui'
import { Dialog } from '@/components/ui/dialog'
import { canEdit, useUsage, useWorkspace } from '@/features/workspaces/api'
import { api } from '@/lib/api'
import type { EvalDataset, EvalRunDetails, EvalRunSummary } from '@/lib/types'
import { cn, formatDate, formatNumber, percent } from '@/lib/utils'

export function EvaluationPage() {
  const { workspaceId = '' } = useParams()
  const client = useQueryClient()
  const { data: workspace } = useWorkspace(workspaceId)
  const usage = useUsage(workspaceId)
  const [judge, setJudge] = useState(true)
  const [openRun, setOpenRun] = useState<string | null>(null)
  const fileInput = useRef<HTMLInputElement>(null)

  const runs = useQuery({
    queryKey: ['evals', workspaceId],
    queryFn: () => api.get<EvalRunSummary[]>(`/api/workspaces/${workspaceId}/evals`),
    refetchInterval: (q) => (q.state.data?.some((r) => r.status === 'Pending' || r.status === 'Running') ? 3000 : false),
  })

  const start = useMutation({
    mutationFn: (dataset: EvalDataset) => api.post<EvalRunSummary>(`/api/workspaces/${workspaceId}/evals`, { dataset, useLlmJudge: judge }),
    onSuccess: () => {
      toast.success('Evaluation started — it runs in the background')
      void client.invalidateQueries({ queryKey: ['evals', workspaceId] })
    },
    onError: (e) => toast.error((e as Error).message),
  })

  const runSample = async () => start.mutate(await api.get<EvalDataset>('/api/system/sample-dataset'))
  const runFile = async (file: File) => {
    try {
      start.mutate(JSON.parse(await file.text()) as EvalDataset)
    } catch {
      toast.error('The file is not a valid dataset JSON')
    }
  }

  const u = usage.data
  return (
    <div className="h-full overflow-y-auto">
      <div className="mx-auto max-w-6xl space-y-6 px-8 py-8">
        <PageHeader title="Quality & Usage" description="Measure retrieval and answer quality with golden datasets, and track LLM spend and user feedback." />

        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
          <Stat label="Answers" value={u ? formatNumber(u.answers) : '–'} sub={u ? `${u.conversations} conversations` : undefined} />
          <Stat label="Tokens" value={u ? formatNumber(u.inputTokens + u.outputTokens) : '–'} sub={u ? `${formatNumber(u.inputTokens)} in · ${formatNumber(u.outputTokens)} out` : undefined} />
          <Stat label="Estimated cost" value={u ? `$${u.estimatedCostUsd.toFixed(4)}` : '–'} sub={u ? `avg latency ${(u.avgLatencyMs / 1000).toFixed(1)}s` : undefined} />
          <Stat
            label="Feedback"
            value={u ? `${u.positiveFeedback} 👍 · ${u.negativeFeedback} 👎` : '–'}
            sub={u && u.positiveFeedback + u.negativeFeedback > 0 ? `${percent(u.positiveFeedback / (u.positiveFeedback + u.negativeFeedback))} positive` : 'no ratings yet'}
          />
        </div>

        <Card>
          <CardHeader
            title="Evaluation runs"
            description="Hit rate / Recall / MRR measure retrieval; Relevance / Groundedness / Coherence (1–5) are LLM-as-a-judge scores."
            action={
              canEdit(workspace?.role) ? (
                <div className="flex items-center gap-3">
                  <div className="w-52"><Switch label="LLM judge" checked={judge} onChange={setJudge} /></div>
                  <Button variant="secondary" onClick={() => fileInput.current?.click()}><Upload className="h-4 w-4" /> Upload dataset</Button>
                  <Button loading={start.isPending} onClick={runSample}><FlaskConical className="h-4 w-4" /> Run sample dataset</Button>
                  <input ref={fileInput} type="file" accept=".json" hidden onChange={(e) => {
                    const file = e.target.files?.[0]
                    if (file) void runFile(file)
                    e.target.value = ''
                  }} />
                </div>
              ) : undefined
            }
          />
          {runs.isLoading ? (
            <div className="p-6"><Spinner /></div>
          ) : !runs.data?.length ? (
            <EmptyState icon={<FlaskConical className="h-6 w-6" />} title="No evaluation runs"
              description="Run the sample golden dataset (matches the demo documents) or upload your own JSON: { name, items: [{ question, expectedDocuments, referenceAnswer }] }" />
          ) : (
            <table className="w-full text-sm">
              <thead className="border-b border-slate-100 text-left text-xs uppercase tracking-wide text-slate-400">
                <tr>
                  <th className="px-5 py-3 font-medium">Dataset</th>
                  <th className="px-5 py-3 font-medium">Config</th>
                  <th className="px-5 py-3 font-medium">Hit rate</th>
                  <th className="px-5 py-3 font-medium">Recall</th>
                  <th className="px-5 py-3 font-medium">MRR</th>
                  <th className="px-5 py-3 font-medium">Rel.</th>
                  <th className="px-5 py-3 font-medium">Grd.</th>
                  <th className="px-5 py-3 font-medium">Coh.</th>
                  <th className="px-5 py-3 font-medium">Latency</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {runs.data.map((run) => (
                  <tr key={run.id} className="cursor-pointer hover:bg-slate-50" onClick={() => setOpenRun(run.id)}>
                    <td className="px-5 py-3">
                      <p className="font-medium text-slate-800">{run.datasetName}</p>
                      <p className="text-xs text-slate-400">{formatDate(run.createdAt)}</p>
                    </td>
                    <td className="px-5 py-3"><ConfigBadges snapshot={run.settingsSnapshot} /></td>
                    {run.status === 'Completed' && run.metrics ? (
                      <>
                        <td className="px-5 py-3"><Score value={run.metrics.hitRate} /></td>
                        <td className="px-5 py-3"><Score value={run.metrics.recall} /></td>
                        <td className="px-5 py-3">{run.metrics.mrr.toFixed(2)}</td>
                        <td className="px-5 py-3">{run.metrics.relevance?.toFixed(1) ?? '–'}</td>
                        <td className="px-5 py-3">{run.metrics.groundedness?.toFixed(1) ?? '–'}</td>
                        <td className="px-5 py-3">{run.metrics.coherence?.toFixed(1) ?? '–'}</td>
                        <td className="px-5 py-3 text-slate-500">{(run.metrics.avgLatencyMs / 1000).toFixed(1)}s</td>
                      </>
                    ) : (
                      <td colSpan={7} className="px-5 py-3">
                        <Badge tone={run.status === 'Failed' ? 'red' : 'blue'}>
                          {run.status !== 'Failed' && <Spinner className="h-3 w-3" />}{run.status}
                        </Badge>
                        {run.error && <span className="ml-2 text-xs text-red-600">{run.error}</span>}
                      </td>
                    )}
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </Card>
      </div>

      <RunDetails workspaceId={workspaceId} runId={openRun} onClose={() => setOpenRun(null)} />
    </div>
  )
}

function Stat({ label, value, sub }: { label: string; value: string; sub?: string }) {
  return (
    <Card className="p-5">
      <p className="text-xs font-medium uppercase tracking-wide text-slate-400">{label}</p>
      <p className="mt-2 text-2xl font-semibold text-slate-900">{value}</p>
      {sub && <p className="mt-1 text-xs text-slate-500">{sub}</p>}
    </Card>
  )
}

function Score({ value }: { value: number }) {
  const tone = value >= 0.8 ? 'text-emerald-600' : value >= 0.5 ? 'text-amber-600' : 'text-red-600'
  return <span className={cn('font-semibold', tone)}>{percent(value)}</span>
}

function parseSnapshot(snapshot: string): Record<string, unknown> | null {
  try {
    return JSON.parse(snapshot) as Record<string, unknown>
  } catch {
    return null
  }
}

// Older runs stored the enum as its numeric value.
const strategies = ['FixedToken', 'Recursive', 'Semantic']
const strategyName = (value: unknown) => (typeof value === 'number' ? (strategies[value] ?? String(value)) : String(value))

function ConfigBadges({ snapshot }: { snapshot: string }) {
  const s = parseSnapshot(snapshot)
  if (!s) return null
  return (
    <div className="flex max-w-xs flex-wrap gap-1">
      <Badge tone="brand">{strategyName(s.ChunkingStrategy)}</Badge>
      <Badge>{String(s.ChunkSize)}/{String(s.ChunkOverlap)}</Badge>
      <Badge>k={String(s.TopK)}</Badge>
      {Boolean(s.UseHybridSearch) && <Badge tone="blue">hybrid</Badge>}
      {Boolean(s.UseReranker) && <Badge tone="blue">rerank</Badge>}
    </div>
  )
}

function RunDetails({ workspaceId, runId, onClose }: { workspaceId: string; runId: string | null; onClose: () => void }) {
  const { data, isLoading } = useQuery({
    queryKey: ['evals', workspaceId, runId],
    queryFn: () => api.get<EvalRunDetails>(`/api/workspaces/${workspaceId}/evals/${runId}`),
    enabled: !!runId,
  })

  return (
    <Dialog open={!!runId} onOpenChange={(o) => !o && onClose()} title={data?.run.datasetName ?? 'Evaluation run'} description="Per-question results" className="max-w-5xl">
      {isLoading || !data ? (
        <Spinner />
      ) : (
        <div className="space-y-3">
          {data.results.map((r, i) => (
            <div key={i} className="rounded-lg border border-slate-200 p-3">
              <div className="flex items-start gap-2">
                {r.hit ? <CheckCircle2 className="mt-0.5 h-4 w-4 shrink-0 text-emerald-600" /> : <XCircle className="mt-0.5 h-4 w-4 shrink-0 text-red-500" />}
                <p className="flex-1 text-sm font-medium text-slate-800">{r.question}</p>
                <div className="flex shrink-0 gap-1">
                  <Badge>RR {r.reciprocalRank.toFixed(2)}</Badge>
                  {r.relevance != null && <Badge tone="blue">Rel {r.relevance}</Badge>}
                  {r.groundedness != null && <Badge tone="blue">Grd {r.groundedness}</Badge>}
                  <Badge>{(r.latencyMs / 1000).toFixed(1)}s</Badge>
                </div>
              </div>
              <p className="mt-1 pl-6 text-xs text-slate-500">
                expected: {r.expectedDocuments.join(', ') || '–'} · retrieved: {r.retrievedDocuments.join(', ') || '–'}
              </p>
              {r.answer && <p className="mt-2 whitespace-pre-wrap pl-6 text-sm text-slate-600">{r.answer}</p>}
              {r.error && <p className="mt-2 pl-6 text-xs text-red-600">{r.error}</p>}
            </div>
          ))}
        </div>
      )}
    </Dialog>
  )
}
