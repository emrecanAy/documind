import { useRef, useState } from 'react'
import { useParams } from 'react-router'
import { toast } from 'sonner'
import { AlertCircle, CheckCircle2, Download, Eye, FileText, Loader2, RefreshCw, Trash2, UploadCloud } from 'lucide-react'
import { Badge, Button, Card, CardHeader, EmptyState, PageHeader, Progress, Spinner } from '@/components/ui'
import { Dialog } from '@/components/ui/dialog'
import { canEdit, useSystemInfo, useWorkspace } from '@/features/workspaces/api'
import type { DocumentDto, DocumentStatus } from '@/lib/types'
import { cn, formatBytes, formatDate } from '@/lib/utils'
import { downloadDocument, useChunks, useDocumentAction, useDocuments, useDocumentStatusStream, useUploadDocuments } from './api'

const statusTone: Record<DocumentStatus, 'gray' | 'blue' | 'green' | 'red'> = {
  Queued: 'gray',
  Processing: 'blue',
  Indexed: 'green',
  Failed: 'red',
}

export function DocumentsPage() {
  const { workspaceId = '' } = useParams()
  const { data: workspace } = useWorkspace(workspaceId)
  const { data: documents, isLoading } = useDocuments(workspaceId)
  const { data: info } = useSystemInfo()
  const upload = useUploadDocuments(workspaceId)
  const actions = useDocumentAction(workspaceId)
  const [viewing, setViewing] = useState<DocumentDto | null>(null)
  const editable = canEdit(workspace?.role)

  useDocumentStatusStream(workspaceId)

  const onFiles = async (files: File[]) => {
    if (!files.length) return
    try {
      await upload.mutateAsync(files)
      toast.success(`${files.length} file(s) queued for indexing`)
    } catch (error) {
      toast.error((error as Error).message)
    }
  }

  const indexed = documents?.filter((d) => d.status === 'Indexed') ?? []
  const totalChunks = indexed.reduce((sum, d) => sum + d.chunkCount, 0)

  return (
    <div className="h-full overflow-y-auto">
      <div className="mx-auto max-w-6xl px-8 py-8">
        <PageHeader
          title="Documents"
          description={`${indexed.length} indexed · ${totalChunks} chunks in the vector store${info ? ` · embeddings: ${info.embeddingModel}` : ''}`}
          actions={
            editable && documents?.length ? (
              <Button variant="secondary" loading={actions.reindexAll.isPending} onClick={() => actions.reindexAll.mutate()}>
                <RefreshCw className="h-4 w-4" /> Re-index all
              </Button>
            ) : undefined
          }
        />

        {editable && (
          <Dropzone onFiles={onFiles} uploading={upload.isPending} accept={info?.supportedFileTypes.join(',') ?? ''} />
        )}

        <Card className="mt-6">
          <CardHeader title="Library" description="Ingestion runs in the background worker: parse → chunk → embed → index." />
          {isLoading ? (
            <div className="p-6"><Spinner /></div>
          ) : !documents?.length ? (
            <EmptyState icon={<FileText className="h-6 w-6" />} title="No documents" description="Upload PDF, Word, Markdown, HTML or text files." />
          ) : (
            <table className="w-full text-sm">
              <thead className="border-b border-slate-100 text-left text-xs uppercase tracking-wide text-slate-400">
                <tr>
                  <th className="px-5 py-3 font-medium">Name</th>
                  <th className="px-5 py-3 font-medium">Status</th>
                  <th className="px-5 py-3 font-medium">Chunks</th>
                  <th className="px-5 py-3 font-medium">Size</th>
                  <th className="px-5 py-3 font-medium">Uploaded</th>
                  <th className="px-5 py-3" />
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {documents.map((doc) => (
                  <tr key={doc.id} className="hover:bg-slate-50/60">
                    <td className="max-w-xs px-5 py-3">
                      <div className="flex items-center gap-2">
                        <FileText className="h-4 w-4 shrink-0 text-slate-400" />
                        <span className="truncate font-medium text-slate-800" title={doc.fileName}>{doc.fileName}</span>
                      </div>
                      {doc.error && <p className="mt-1 truncate text-xs text-red-600" title={doc.error}>{doc.error}</p>}
                    </td>
                    <td className="w-48 px-5 py-3">
                      <Badge tone={statusTone[doc.status]}>
                        {doc.status === 'Processing' && <Loader2 className="h-3 w-3 animate-spin" />}
                        {doc.status === 'Indexed' && <CheckCircle2 className="h-3 w-3" />}
                        {doc.status === 'Failed' && <AlertCircle className="h-3 w-3" />}
                        {doc.status}
                      </Badge>
                      {doc.status === 'Processing' && <Progress value={doc.progress} className="mt-2" />}
                    </td>
                    <td className="px-5 py-3 text-slate-600">{doc.chunkCount || '–'}{doc.pageCount ? ` · ${doc.pageCount} p.` : ''}</td>
                    <td className="px-5 py-3 text-slate-600">{formatBytes(doc.sizeBytes)}</td>
                    <td className="px-5 py-3 text-slate-500">{formatDate(doc.createdAt)}</td>
                    <td className="px-5 py-3">
                      <div className="flex justify-end gap-1">
                        <Button variant="ghost" size="icon" title="View chunks" disabled={doc.status !== 'Indexed'} onClick={() => setViewing(doc)}>
                          <Eye className="h-4 w-4" />
                        </Button>
                        <Button variant="ghost" size="icon" title="Download" onClick={() => downloadDocument(workspaceId, doc)}>
                          <Download className="h-4 w-4" />
                        </Button>
                        {editable && (
                          <>
                            <Button variant="ghost" size="icon" title="Re-index" onClick={() => actions.reindex.mutate(doc.id)}>
                              <RefreshCw className="h-4 w-4" />
                            </Button>
                            <Button
                              variant="ghost"
                              size="icon"
                              title="Delete"
                              onClick={() => {
                                if (window.confirm(`Delete ${doc.fileName}? Its vectors are removed from the index.`)) {
                                  actions.remove.mutate(doc.id)
                                }
                              }}
                            >
                              <Trash2 className="h-4 w-4 text-red-500" />
                            </Button>
                          </>
                        )}
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </Card>
      </div>

      <ChunkViewer workspaceId={workspaceId} document={viewing} onClose={() => setViewing(null)} />
    </div>
  )
}

function Dropzone({ onFiles, uploading, accept }: { onFiles: (files: File[]) => void; uploading: boolean; accept: string }) {
  const input = useRef<HTMLInputElement>(null)
  const [dragging, setDragging] = useState(false)

  return (
    <div
      onDragOver={(e) => {
        e.preventDefault()
        setDragging(true)
      }}
      onDragLeave={() => setDragging(false)}
      onDrop={(e) => {
        e.preventDefault()
        setDragging(false)
        onFiles(Array.from(e.dataTransfer.files))
      }}
      onClick={() => input.current?.click()}
      className={cn(
        'flex cursor-pointer flex-col items-center justify-center rounded-xl border-2 border-dashed px-6 py-10 text-center transition',
        dragging ? 'border-brand-500 bg-brand-50' : 'border-slate-300 bg-white hover:border-brand-400 hover:bg-slate-50',
      )}
    >
      {uploading ? <Loader2 className="h-8 w-8 animate-spin text-brand-500" /> : <UploadCloud className="h-8 w-8 text-brand-500" />}
      <p className="mt-3 font-medium text-slate-800">Drop files here or click to upload</p>
      <p className="mt-1 text-sm text-slate-500">PDF, DOCX, Markdown, HTML, TXT · up to 25 MB each · duplicates are detected</p>
      <input
        ref={input}
        type="file"
        multiple
        hidden
        accept={accept}
        onChange={(e) => {
          onFiles(Array.from(e.target.files ?? []))
          e.target.value = ''
        }}
      />
    </div>
  )
}

function ChunkViewer({ workspaceId, document, onClose }: { workspaceId: string; document: DocumentDto | null; onClose: () => void }) {
  const { data, isLoading } = useChunks(workspaceId, document?.id ?? null)
  return (
    <Dialog
      open={!!document}
      onOpenChange={(open) => !open && onClose()}
      title={document?.fileName ?? ''}
      description="How the chunking strategy split this document. Each chunk is embedded and indexed separately."
      className="max-w-3xl"
    >
      {isLoading ? (
        <Spinner />
      ) : (
        <div className="space-y-3">
          {data?.map((chunk) => (
            <div key={chunk.id} className="rounded-lg border border-slate-200 p-3">
              <div className="mb-2 flex flex-wrap gap-2 text-xs">
                <Badge tone="brand">#{chunk.index}</Badge>
                <Badge>{chunk.tokenCount} tokens</Badge>
                {chunk.page && <Badge>page {chunk.page}</Badge>}
                {chunk.section && <Badge tone="blue">{chunk.section}</Badge>}
              </div>
              <p className="whitespace-pre-wrap text-sm text-slate-700">{chunk.content}</p>
            </div>
          ))}
        </div>
      )}
    </Dialog>
  )
}
