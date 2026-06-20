import { http } from './http'
import type {
  AdminLoginRequest,
  AdminLoginResponse,
  AgentSummaryResponse,
  AuditSearchRequest,
  AuditRecord,
  HealthStatusResponse,
  GuardAlgorithmResult,
  MemoryBatchDecayDetails,
  MemoryBatchDecayRequest,
  MemoryBulkDetails,
  MemoryBulkPutRequest,
  MemoryDecayRequest,
  MemoryDetails,
  MemoryListItem,
  MemoryPutRequest,
  MemoryReferenceRequest,
  MemoryRestoreRequest,
  MemorySearchRequest,
  MemorySyncActionItem,
  MemorySyncActionResult,
  MemorySyncFailureRequest,
  ModifyAgentRequest,
  OverviewResponse,
  PagedResult,
  RegisterAgentRequest,
  RegisterAgentResponse,
  RiskTrendResponse,
  RuleOptionsResponse,
  SecurityCheckRequest,
  SecurityCheckResponse,
  SecurityRule,
  SecurityRuleSet,
  TestRuleRequest,
  TestRuleResponse,
  ToolCallCheckRequest
} from './types'

export const authApi = {
  login(data: AdminLoginRequest) {
    return http.post<AdminLoginResponse>('/api/auth/login', data)
  }
}

export const agentApi = {
  register(data: RegisterAgentRequest) {
    return http.post<RegisterAgentResponse>('/api/agent/register', data)
  },
  list() {
    return http.get<AgentSummaryResponse[]>('/api/agent')
  },
  updateEnabled(agentId: number, enabled: boolean) {
    return http.patch<AgentSummaryResponse>(`/api/agent/${agentId}/enabled`, { enabled })
  },
  update(agentId: number, data: ModifyAgentRequest) {
    return http.put<AgentSummaryResponse>(`/api/agent/${agentId}`, data)
  },
  remove(agentId: number) {
    return http.delete(`/api/agent/${agentId}`)
  }
}

export const securityApi = {
  checkInput(data: SecurityCheckRequest) {
    return http.post<SecurityCheckResponse>('/api/security/check-input', data)
  },
  checkOutput(data: SecurityCheckRequest) {
    return http.post<SecurityCheckResponse>('/api/security/check-output', data)
  },
  checkToolCall(data: ToolCallCheckRequest) {
    return http.post<SecurityCheckResponse>('/api/security/check-tool-call', data)
  }
}

export const memoryApi = {
  search(params: MemorySearchRequest) {
    return http.get<PagedResult<MemoryListItem>>('/api/security/memories', { params })
  },
  put(data: MemoryPutRequest) {
    return http.post<GuardAlgorithmResult<MemoryDetails>>('/api/security/memory-put', data)
  },
  bulkPut(data: MemoryBulkPutRequest) {
    return http.post<GuardAlgorithmResult<MemoryBulkDetails>>('/api/security/memory-bulk', data)
  },
  decay(data: MemoryDecayRequest) {
    return http.post<GuardAlgorithmResult<MemoryDetails>>('/api/security/memory-decay', data)
  },
  batchDecay(data: MemoryBatchDecayRequest) {
    return http.post<GuardAlgorithmResult<MemoryBatchDecayDetails>>('/api/security/memory/decay-batch', data)
  },
  updateReference(memoryId: string, data: MemoryReferenceRequest) {
    return http.post<GuardAlgorithmResult<MemoryDetails>>(
      `/api/security/memory/${encodeURIComponent(memoryId)}/reference`,
      data
    )
  },
  restore(memoryId: string, data: MemoryRestoreRequest) {
    return http.post<GuardAlgorithmResult<MemoryDetails>>(
      `/api/security/memory/${encodeURIComponent(memoryId)}/restore`,
      data
    )
  },
  remove(memoryId: string) {
    return http.delete<GuardAlgorithmResult<MemoryDetails>>(
      `/api/security/memory/${encodeURIComponent(memoryId)}`
    )
  },
  getPendingSyncActions(limit = 100) {
    return http.get<MemorySyncActionItem[]>('/api/security/memory-sync-actions', { params: { limit } })
  },
  confirmSyncAction(actionId: string) {
    return http.post<GuardAlgorithmResult<MemorySyncActionResult>>(
      `/api/security/memory-sync-actions/${actionId}/confirm`
    )
  },
  failSyncAction(actionId: string, data: MemorySyncFailureRequest) {
    return http.post<GuardAlgorithmResult<MemorySyncActionResult>>(
      `/api/security/memory-sync-actions/${actionId}/fail`,
      data
    )
  },
  retrySyncAction(actionId: string) {
    return http.post<GuardAlgorithmResult<MemorySyncActionResult>>(
      `/api/security/memory-sync-actions/${actionId}/retry`
    )
  }
}

export const rulesApi = {
  getAll(agentId?: number | null) {
    return http.get<SecurityRuleSet>('/api/rules', { params: agentId ? { agentId } : {} })
  },
  getOptions() {
    return http.get<RuleOptionsResponse>('/api/rules/options')
  },
  create(data: SecurityRule, agentId?: number | null) {
    return http.post<SecurityRule>('/api/rules', data, { params: agentId ? { agentId } : {} })
  },
  update(ruleId: string, data: SecurityRule) {
    return http.put<SecurityRule>(`/api/rules/${ruleId}`, data)
  },
  updateEnabled(ruleId: string, enabled: boolean, agentId?: number | null) {
    return http.patch<SecurityRule>(`/api/rules/${ruleId}/enabled`, { enabled }, { params: agentId ? { agentId } : {} })
  },
  test(data: TestRuleRequest) {
    return http.post<TestRuleResponse>('/api/rules/test', data)
  },
  remove(ruleId: string) {
    return http.delete(`/api/rules/${ruleId}`)
  }
}

export const auditApi = {
  list(params: { agentId?: number | null; limit?: number } = {}) {
    return http.get<AuditRecord[]>('/api/audit', { params })
  },
  search(data: AuditSearchRequest) {
    return http.post<PagedResult<AuditRecord>>('/api/audit/search', data)
  }
}

export const dashboardApi = {
  getRiskTrend(agentId: number | null, days: number) {
    return http.get<RiskTrendResponse>('/api/dashboard/risk-trend', { params: { agentId, days } })
  },
  getHealthStatus(agentId: number) {
    return http.get<HealthStatusResponse>('/api/dashboard/health-status', { params: { agentId } })
  },
  getOverview(agentId: number) {
    return http.get<OverviewResponse>('/api/dashboard/overview', { params: { agentId } })
  }
}
