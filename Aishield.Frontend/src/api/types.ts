export interface RegisterAgentRequest {
  agentName: string
  scenario: string
}

export interface RegisterAgentResponse {
  agentId: number
  agentName: string
  agentKey: string
  message: string
}

export interface AdminLoginRequest {
  password: string
}

export interface AdminLoginResponse {
  success: boolean
  token: string
  tokenExpiresAt: string | null
  message: string
}

export interface AgentSummaryResponse {
  agentId: number
  agentName: string
  scenario: string
  agentKeyPreview: string
  enabled: boolean
  createdAt: string
  lastUsedAt: string | null
}

export interface ModifyAgentRequest {
  id: number
  agentName: string
  scenario: string
}

export interface SecurityCheckRequest {
  subjectHash?: string
  content: string
}

export interface SecurityCheckResponse {
  allowed: boolean
  action: string
  riskLevel: string
  processedContent: string | null
  reason: string
  hitRules: string[]
}

export interface ToolCallCheckRequest {
  subjectHash?: string
  toolName: string
  arguments: Record<string, unknown>
}

export interface SecurityRule {
  ruleId: string
  name: string
  ruleType: 'Input' | 'Output'
  matchType: 'Regex' | 'Keyword'
  pattern: string
  riskLevel: 'None' | 'Low' | 'Medium' | 'High' | 'Critical'
  action: 'Allow' | 'Warn' | 'Block' | 'Mask' | 'NeedApproval'
  replacement: string
  enabled: boolean
}

export interface SecurityRuleSet {
  inputRules: SecurityRule[]
  outputRules: SecurityRule[]
  toolPolicy: {
    dangerousTools: string[]
    dangerousArgumentPatterns: string[]
    appToolAllowList: Record<string, string[]>
  }
}

export interface RuleOptionsResponse {
  ruleTypes: string[]
  matchTypes: string[]
  riskLevels: string[]
  actions: string[]
}

export interface TestRuleRequest {
  ruleId: string
  testContent: string
}

export interface TestRuleResponse {
  isMatch: boolean
  matchDetails: string
}

export interface AuditRecord {
  id: number
  agentId: number
  agentName: string
  subjectHash: string | null
  createdAt: string
  direction: string
  originalContent: string
  processedContent: string | null
  riskLevel: string
  action: string
  hitRules: string
  reason: string
  clientIp: string
  durationMs: number
}

export interface AuditSearchRequest {
  agentId?: number | null
  direction?: string | null
  riskLevel?: string | null
  action?: string | null
  hitRule?: string | null
  keyword?: string | null
  startTime?: string | null
  endTime?: string | null
  pageIndex: number
  pageSize: number
}

export interface PagedResult<T> {
  items: T[]
  total: number
  pageIndex: number
  pageSize: number
}

export interface RiskTrendPoint {
  date: string
  label: string
  blockCount: number
  maskCount: number
  highRiskCount: number
}

export interface RiskTrendResponse {
  days: number
  points: RiskTrendPoint[]
}

export interface HealthStatusResponse {
  healthScore: number
  averageResponseTime: number
  errorRate: number
  availability: number
}

export interface OverviewResponse {
  dayRequestCount: number
  dayBlockedCount: number
  dayMaskedCount: number
  dayRiskEventCount: number
}

export type MemorySource =
  | 'Unknown'
  | 'User'
  | 'System'
  | 'Admin'
  | 'Document'
  | 'Website'
  | 'Conversation'
  | 'Tool'
  | 'Import'

export interface MemorySearchRequest {
  keyword?: string | null
  source?: MemorySource | null
  isArchived?: boolean | null
  minConfidence?: number | null
  maxConfidence?: number | null
  archiveReason?: string | null
  lastPositiveRefFrom?: string | null
  lastPositiveRefTo?: string | null
  sortBy: 'updatedAt' | 'confidence' | 'createdAt' | 'lastPositiveRef'
  descending: boolean
  pageIndex: number
  pageSize: number
}

export interface MemoryListItem {
  memoryId: string
  source: MemorySource
  memoryKey: string | null
  confidence: number
  memoryCreatedAt: string
  lastPositiveRef: string | null
  lastDecayAt: string | null
  isArchived: boolean
  archiveReason: string | null
  createdAt: string
  updatedAt: string
}

export interface MemoryPutRequest {
  memoryId: string
  content: string
  confidence: number
  source: MemorySource
  memoryKey?: string | null
  embedding?: number[] | null
  lastPositiveRef: string
  memoryCreatedAt: string
}

export interface MemoryBulkPutRequest {
  memories: MemoryPutRequest[]
  skipConflictCheck: boolean
}

export interface MemoryDecayRequest {
  memoryId: string
}

export interface MemoryReferenceRequest {
  referencedAt: string
}

export interface MemoryBatchDecayRequest {
  batchSize: number
  updatedBefore?: string | null
}

export interface MemoryRestoreRequest {
  confidence?: number | null
  referencedAt: string
}

export interface MemoryConflictDetails {
  memoryId: string
  similarity: number
  reason: string
  action: string
  newConfidence: number
}

export interface MemoryDetails {
  memoryId: string
  confidence: number
  source: MemorySource
  memoryKey: string | null
  lastPositiveRef: string | null
  action: string
  conflicts: MemoryConflictDetails[]
  memoryCreatedAt: string
}

export interface MemoryBulkDetails {
  total: number
  succeeded: number
  failed: number
  conflicts: number
  items: GuardAlgorithmResult<MemoryDetails>[]
}

export interface MemoryBatchDecayDetails {
  processed: number
  decayed: number
  archived: number
  items: MemoryDetails[]
}

export interface GuardAlgorithmData<T> {
  requestId: string
  blocked: boolean
  riskLabel: string | null
  details: T | null
}

export interface GuardAlgorithmResult<T> {
  success: boolean
  message: string
  data: GuardAlgorithmData<T> | null
}

export type MemorySyncActionType = 'UpdateConfidence' | 'Archive' | 'Restore'
export type MemorySyncActionStatus = 'Pending' | 'Confirmed' | 'Failed'

export interface MemorySyncActionItem {
  actionId: string
  memoryId: string
  actionType: MemorySyncActionType
  newConfidence: number | null
  reason: string | null
  createdAt: string
}

export interface MemorySyncFailureRequest {
  failureMessage: string
}

export interface MemorySyncActionResult {
  actionId: string
  status: MemorySyncActionStatus
  updatedAt: string
}
