// Contracts mirrored from the .NET API (DocuMind.Application).

export type WorkspaceRole = 'Viewer' | 'Editor' | 'Owner'
export type ChunkingStrategy = 'FixedToken' | 'Recursive' | 'Semantic'
export type DocumentStatus = 'Queued' | 'Processing' | 'Indexed' | 'Failed'
export type ChatMode = 'Rag' | 'Agent'
export type EvalRunStatus = 'Pending' | 'Running' | 'Completed' | 'Failed'

export interface User {
  id: string
  email: string
  displayName: string
}

export interface AuthResponse {
  accessToken: string
  expiresAt: string
  refreshToken: string
  user: User
}

export interface RagSettings {
  chunkingStrategy: ChunkingStrategy
  chunkSize: number
  chunkOverlap: number
  topK: number
  useHybridSearch: boolean
  useReranker: boolean
  useQueryRewriting: boolean
  useQueryExpansion: boolean
  semanticBreakpointPercentile: number
}

export interface WorkspaceSummary {
  id: string
  name: string
  description?: string
  role: WorkspaceRole
  documentCount: number
  createdAt: string
}

export interface Member {
  userId: string
  email: string
  displayName: string
  role: WorkspaceRole
  joinedAt: string
}

export interface WorkspaceDetails {
  id: string
  name: string
  description?: string
  role: WorkspaceRole
  settings: RagSettings
  members: Member[]
  createdAt: string
}

export interface DocumentDto {
  id: string
  fileName: string
  contentType: string
  sizeBytes: number
  status: DocumentStatus
  progress: number
  error?: string
  chunkCount: number
  pageCount: number
  embeddingModel?: string
  createdAt: string
  indexedAt?: string
}

export interface ChunkDto {
  id: string
  index: number
  content: string
  page?: number
  section?: string
  tokenCount: number
}

export interface Citation {
  number: number
  chunkId: string
  documentId: string
  fileName: string
  page?: number
  section?: string
  snippet: string
  score: number
}

export interface AgentStep {
  kind: string
  name: string
  input?: string
  output?: string
}

export interface MessageUsage {
  model: string
  inputTokens: number
  outputTokens: number
  estimatedCostUsd: number
  latencyMs: number
  retrievedChunks: number
}

export interface Message {
  id: string
  role: 'User' | 'Assistant'
  content: string
  citations: Citation[]
  agentSteps: AgentStep[]
  usage?: MessageUsage
  feedback?: number
  createdAt: string
}

export interface ConversationSummary {
  id: string
  title: string
  createdAt: string
  updatedAt: string
}

export interface ConversationDetails {
  id: string
  title: string
  messages: Message[]
}

export interface UsageSummary {
  conversations: number
  answers: number
  inputTokens: number
  outputTokens: number
  estimatedCostUsd: number
  avgLatencyMs: number
  positiveFeedback: number
  negativeFeedback: number
}

export interface EvalItem {
  question: string
  expectedDocuments: string[]
  referenceAnswer?: string
}

export interface EvalDataset {
  name: string
  items: EvalItem[]
}

export interface EvalMetrics {
  items: number
  hitRate: number
  recall: number
  mrr: number
  relevance?: number
  groundedness?: number
  coherence?: number
  avgLatencyMs: number
  totalTokens: number
}

export interface EvalRunSummary {
  id: string
  datasetName: string
  status: EvalRunStatus
  useLlmJudge: boolean
  metrics?: EvalMetrics
  settingsSnapshot: string
  error?: string
  createdAt: string
  completedAt?: string
}

export interface EvalItemResult {
  question: string
  expectedDocuments: string[]
  retrievedDocuments: string[]
  hit: boolean
  recall: number
  reciprocalRank: number
  answer: string
  relevance?: number
  groundedness?: number
  coherence?: number
  latencyMs: number
  error?: string
}

export interface EvalRunDetails {
  run: EvalRunSummary
  results: EvalItemResult[]
}

export interface SystemInfo {
  chatProvider: string
  chatModel: string
  embeddingProvider: string
  embeddingModel: string
  vectorStore: string
  supportedFileTypes: string[]
}

export interface DocumentStatusChanged {
  workspaceId: string
  documentId: string
  status: DocumentStatus
  progress: number
  chunkCount: number
  error?: string
}

export interface ProblemDetails {
  title?: string
  detail?: string
  status?: number
  errors?: Record<string, string[]>
}
