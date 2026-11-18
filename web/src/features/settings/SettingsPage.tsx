import { useState } from 'react'
import { useNavigate, useParams } from 'react-router'
import { toast } from 'sonner'
import { Trash2, UserPlus } from 'lucide-react'
import { Badge, Button, Card, CardHeader, Input, Label, PageHeader, Select, Spinner, Switch, Textarea } from '@/components/ui'
import {
  isOwner, useAddMember, useDeleteWorkspace, useRemoveMember, useSystemInfo, useUpdateSettings, useUpdateWorkspace, useWorkspace,
} from '@/features/workspaces/api'
import type { ChunkingStrategy, RagSettings, WorkspaceDetails, WorkspaceRole } from '@/lib/types'

const strategies: { value: ChunkingStrategy; label: string; hint: string }[] = [
  { value: 'Recursive', label: 'Recursive (structure-aware)', hint: 'Splits on paragraphs → lines → sentences. Best default.' },
  { value: 'FixedToken', label: 'Fixed token windows', hint: 'Fast, predictable baseline that ignores structure.' },
  { value: 'Semantic', label: 'Semantic', hint: 'Embeds sentences and splits where the topic changes. Slower ingestion.' },
]

export function SettingsPage() {
  const { workspaceId = '' } = useParams()
  const { data: workspace, isLoading } = useWorkspace(workspaceId)
  if (isLoading || !workspace) return <div className="p-8"><Spinner /></div>
  // keyed so local form state is re-initialised when switching workspaces
  return <SettingsForm key={workspace.id} workspace={workspace} />
}

function SettingsForm({ workspace }: { workspace: WorkspaceDetails }) {
  const workspaceId = workspace.id
  const navigate = useNavigate()
  const { data: info } = useSystemInfo()
  const updateWorkspace = useUpdateWorkspace(workspaceId)
  const updateSettings = useUpdateSettings(workspaceId)
  const addMember = useAddMember(workspaceId)
  const removeMember = useRemoveMember(workspaceId)
  const deleteWorkspace = useDeleteWorkspace()

  const [name, setName] = useState(workspace.name)
  const [description, setDescription] = useState(workspace.description ?? '')
  const [settings, setSettings] = useState<RagSettings>(workspace.settings)
  const [reindex, setReindex] = useState(true)
  const [memberEmail, setMemberEmail] = useState('')
  const [memberRole, setMemberRole] = useState<WorkspaceRole>('Viewer')

  const owner = isOwner(workspace.role)
  const set = <K extends keyof RagSettings>(key: K, value: RagSettings[K]) => setSettings({ ...settings, [key]: value })
  const chunkingChanged =
    settings.chunkingStrategy !== workspace.settings.chunkingStrategy ||
    settings.chunkSize !== workspace.settings.chunkSize ||
    settings.chunkOverlap !== workspace.settings.chunkOverlap ||
    settings.semanticBreakpointPercentile !== workspace.settings.semanticBreakpointPercentile

  const run = async (action: () => Promise<unknown>, success: string) => {
    try {
      await action()
      toast.success(success)
    } catch (error) {
      toast.error((error as Error).message)
    }
  }

  return (
    <div className="h-full overflow-y-auto">
      <div className="mx-auto max-w-4xl space-y-6 px-8 py-8">
        <PageHeader title="Settings" description="Tune the RAG pipeline and manage who can access this workspace." />

        <Card>
          <CardHeader title="General" />
          <div className="grid gap-4 p-5">
            <div>
              <Label htmlFor="name">Name</Label>
              <Input id="name" value={name} disabled={!owner} onChange={(e) => setName(e.target.value)} />
            </div>
            <div>
              <Label htmlFor="desc">Description</Label>
              <Textarea id="desc" rows={2} value={description} disabled={!owner} onChange={(e) => setDescription(e.target.value)} />
            </div>
            {owner && (
              <div className="flex justify-end">
                <Button loading={updateWorkspace.isPending} onClick={() => run(() => updateWorkspace.mutateAsync({ name, description }), 'Workspace updated')}>
                  Save
                </Button>
              </div>
            )}
          </div>
        </Card>

        <Card>
          <CardHeader
            title="Retrieval pipeline"
            description={info ? `Chat: ${info.chatProvider}/${info.chatModel} · Embeddings: ${info.embeddingProvider}/${info.embeddingModel} · ${info.vectorStore}` : undefined}
          />
          <div className="grid gap-6 p-5 md:grid-cols-2">
            <div className="space-y-4">
              <h4 className="text-xs font-semibold uppercase tracking-wide text-slate-400">Ingestion</h4>
              <div>
                <Label htmlFor="strategy">Chunking strategy</Label>
                <Select id="strategy" value={settings.chunkingStrategy} disabled={!owner} onChange={(e) => set('chunkingStrategy', e.target.value as ChunkingStrategy)}>
                  {strategies.map((s) => <option key={s.value} value={s.value}>{s.label}</option>)}
                </Select>
                <p className="mt-1 text-xs text-slate-500">{strategies.find((s) => s.value === settings.chunkingStrategy)?.hint}</p>
              </div>
              <div className="grid grid-cols-2 gap-3">
                <div>
                  <Label htmlFor="size">Chunk size (tokens)</Label>
                  <Input id="size" type="number" min={64} max={4096} step={32} disabled={!owner} value={settings.chunkSize} onChange={(e) => set('chunkSize', Number(e.target.value))} />
                </div>
                <div>
                  <Label htmlFor="overlap">Overlap (tokens)</Label>
                  <Input id="overlap" type="number" min={0} step={8} disabled={!owner} value={settings.chunkOverlap} onChange={(e) => set('chunkOverlap', Number(e.target.value))} />
                </div>
              </div>
              {settings.chunkingStrategy === 'Semantic' && (
                <div>
                  <Label htmlFor="pct">Breakpoint percentile: {settings.semanticBreakpointPercentile}</Label>
                  <input id="pct" type="range" min={0.6} max={0.98} step={0.01} disabled={!owner} className="w-full accent-brand-600"
                    value={settings.semanticBreakpointPercentile} onChange={(e) => set('semanticBreakpointPercentile', Number(e.target.value))} />
                </div>
              )}
            </div>

            <div className="space-y-1">
              <h4 className="mb-3 text-xs font-semibold uppercase tracking-wide text-slate-400">Retrieval</h4>
              <div className="mb-3">
                <Label htmlFor="topk">Top-K passages: {settings.topK}</Label>
                <input id="topk" type="range" min={1} max={20} disabled={!owner} className="w-full accent-brand-600" value={settings.topK} onChange={(e) => set('topK', Number(e.target.value))} />
              </div>
              <Switch label="Hybrid search" description="Dense vectors + BM25 sparse vectors fused with RRF" checked={settings.useHybridSearch} onChange={(v) => owner && set('useHybridSearch', v)} />
              <Switch label="LLM reranker" description="Retrieve 3× candidates, let the model re-score them" checked={settings.useReranker} onChange={(v) => owner && set('useReranker', v)} />
              <Switch label="Query rewriting" description="Turn follow-up questions into standalone queries" checked={settings.useQueryRewriting} onChange={(v) => owner && set('useQueryRewriting', v)} />
              <Switch label="Query expansion" description="Search with 2 extra paraphrases (multi-query)" checked={settings.useQueryExpansion} onChange={(v) => owner && set('useQueryExpansion', v)} />
            </div>
          </div>
          {owner && (
            <div className="flex items-center justify-between gap-4 border-t border-slate-100 px-5 py-4">
              <label className="flex items-center gap-2 text-sm text-slate-600">
                <input type="checkbox" className="accent-brand-600" checked={reindex} onChange={(e) => setReindex(e.target.checked)} />
                Re-index all documents {chunkingChanged && <Badge tone="amber">chunking changed</Badge>}
              </label>
              <Button loading={updateSettings.isPending} onClick={() => run(() => updateSettings.mutateAsync({ settings, reindex: reindex && chunkingChanged }), 'Settings saved')}>
                Save pipeline settings
              </Button>
            </div>
          )}
        </Card>

        <Card>
          <CardHeader title="Members" description="Viewers can ask questions, Editors manage documents, Owners manage settings and members." />
          <ul className="divide-y divide-slate-100">
            {workspace.members.map((m) => (
              <li key={m.userId} className="flex items-center gap-3 px-5 py-3">
                <div className="flex h-8 w-8 items-center justify-center rounded-full bg-slate-100 text-sm font-semibold text-slate-600">{m.displayName[0]}</div>
                <div className="min-w-0 flex-1">
                  <p className="text-sm font-medium text-slate-800">{m.displayName}</p>
                  <p className="text-xs text-slate-500">{m.email}</p>
                </div>
                <Badge tone={m.role === 'Owner' ? 'brand' : 'gray'}>{m.role}</Badge>
                {owner && m.role !== 'Owner' && (
                  <Button variant="ghost" size="icon" title="Remove" onClick={() => run(() => removeMember.mutateAsync(m.userId), 'Member removed')}>
                    <Trash2 className="h-4 w-4 text-red-500" />
                  </Button>
                )}
              </li>
            ))}
          </ul>
          {owner && (
            <form
              className="flex flex-wrap gap-2 border-t border-slate-100 px-5 py-4"
              onSubmit={(e) => {
                e.preventDefault()
                void run(async () => {
                  await addMember.mutateAsync({ email: memberEmail, role: memberRole })
                  setMemberEmail('')
                }, 'Member added')
              }}
            >
              <Input className="min-w-56 flex-1" type="email" placeholder="colleague@company.com" value={memberEmail} onChange={(e) => setMemberEmail(e.target.value)} required />
              <Select className="w-32" value={memberRole} onChange={(e) => setMemberRole(e.target.value as WorkspaceRole)}>
                <option value="Viewer">Viewer</option>
                <option value="Editor">Editor</option>
                <option value="Owner">Owner</option>
              </Select>
              <Button type="submit" loading={addMember.isPending}><UserPlus className="h-4 w-4" /> Add</Button>
            </form>
          )}
        </Card>

        {owner && (
          <Card className="border-red-200">
            <CardHeader title="Danger zone" description="Deletes all documents, vectors, conversations and evaluation runs of this workspace." />
            <div className="flex justify-end px-5 py-4">
              <Button
                variant="danger"
                loading={deleteWorkspace.isPending}
                onClick={() => {
                  if (window.confirm(`Delete "${workspace.name}" permanently?`)) {
                    void run(async () => {
                      await deleteWorkspace.mutateAsync(workspaceId)
                      navigate('/workspaces')
                    }, 'Workspace deleted')
                  }
                }}
              >
                Delete workspace
              </Button>
            </div>
          </Card>
        )}
      </div>
    </div>
  )
}
