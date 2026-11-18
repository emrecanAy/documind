import { useState } from 'react'
import { useNavigate } from 'react-router'
import { toast } from 'sonner'
import { FileText, FolderPlus, Layers } from 'lucide-react'
import { Badge, Button, Card, EmptyState, Input, Label, PageHeader, Spinner, Textarea } from '@/components/ui'
import { Dialog } from '@/components/ui/dialog'
import { formatDate } from '@/lib/utils'
import { useCreateWorkspace, useWorkspaces } from './api'

export function WorkspacesPage() {
  const navigate = useNavigate()
  const { data, isLoading } = useWorkspaces()
  const create = useCreateWorkspace()
  const [open, setOpen] = useState(false)
  const [name, setName] = useState('')
  const [description, setDescription] = useState('')

  const submit = async (e: React.FormEvent) => {
    e.preventDefault()
    try {
      const workspace = await create.mutateAsync({ name, description: description || undefined })
      setOpen(false)
      navigate(`/w/${workspace.id}/documents`)
    } catch (error) {
      toast.error((error as Error).message)
    }
  }

  return (
    <div className="h-full overflow-y-auto">
      <div className="mx-auto max-w-5xl px-8 py-10">
        <PageHeader
          title="Workspaces"
          description="Each workspace is an isolated knowledge base with its own documents, members and RAG settings."
          actions={<Button onClick={() => setOpen(true)}><FolderPlus className="h-4 w-4" /> New workspace</Button>}
        />

        {isLoading ? (
          <Spinner />
        ) : !data?.length ? (
          <Card>
            <EmptyState
              icon={<Layers className="h-6 w-6" />}
              title="No workspaces yet"
              description="Create a workspace for a team or topic, then upload documents to start asking questions."
              action={<Button onClick={() => setOpen(true)}>Create workspace</Button>}
            />
          </Card>
        ) : (
          <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
            {data.map((w) => (
              <button
                key={w.id}
                onClick={() => navigate(`/w/${w.id}/chat`)}
                className="group rounded-xl border border-slate-200 bg-white p-5 text-left shadow-sm transition hover:border-brand-300 hover:shadow-md"
              >
                <div className="flex items-start justify-between">
                  <div className="rounded-lg bg-brand-50 p-2 text-brand-600"><Layers className="h-5 w-5" /></div>
                  <Badge tone={w.role === 'Owner' ? 'brand' : 'gray'}>{w.role}</Badge>
                </div>
                <h3 className="mt-4 font-semibold text-slate-900 group-hover:text-brand-700">{w.name}</h3>
                <p className="mt-1 line-clamp-2 min-h-10 text-sm text-slate-500">{w.description || 'No description'}</p>
                <div className="mt-4 flex items-center justify-between text-xs text-slate-400">
                  <span className="flex items-center gap-1"><FileText className="h-3.5 w-3.5" /> {w.documentCount} documents</span>
                  <span>{formatDate(w.createdAt)}</span>
                </div>
              </button>
            ))}
          </div>
        )}
      </div>

      <Dialog open={open} onOpenChange={setOpen} title="New workspace" description="You will be the owner and can invite members later.">
        <form onSubmit={submit} className="space-y-4">
          <div>
            <Label htmlFor="ws-name">Name</Label>
            <Input id="ws-name" value={name} onChange={(e) => setName(e.target.value)} placeholder="e.g. HR Policies" required maxLength={100} />
          </div>
          <div>
            <Label htmlFor="ws-desc">Description</Label>
            <Textarea id="ws-desc" rows={3} value={description} onChange={(e) => setDescription(e.target.value)} maxLength={500} />
          </div>
          <div className="flex justify-end gap-2">
            <Button type="button" variant="secondary" onClick={() => setOpen(false)}>Cancel</Button>
            <Button type="submit" loading={create.isPending} disabled={!name.trim()}>Create</Button>
          </div>
        </form>
      </Dialog>
    </div>
  )
}
