import { defineStore } from 'pinia'
import type { AgentSummaryResponse } from '../api/types'

interface AuthState {
  token: string
  tokenExpiresAt: string | null
  agentId: number | null
  agentName: string
}

const STORAGE_KEY = 'aishield-auth'

function getInitialState(): AuthState {
  const raw = localStorage.getItem(STORAGE_KEY)
  const emptyState: AuthState = {
    token: '',
    tokenExpiresAt: null,
    agentId: null,
    agentName: ''
  }

  if (!raw) {
    return emptyState
  }

  return {
    ...emptyState,
    ...JSON.parse(raw)
  } as AuthState
}

export const useAuthStore = defineStore('auth', {
  state: (): AuthState => getInitialState(),
  getters: {
    isLoggedIn: (state) => {
      if (!state.token) {
        return false
      }

      if (!state.tokenExpiresAt) {
        return true
      }

      return new Date(state.tokenExpiresAt).getTime() > Date.now()
    }
  },
  actions: {
    setSession(payload: Pick<AuthState, 'token' | 'tokenExpiresAt'>) {
      this.token = payload.token
      this.tokenExpiresAt = payload.tokenExpiresAt
      this.persist()
    },
    setActiveAgent(agent: AgentSummaryResponse | null) {
      this.agentId = agent?.agentId ?? null
      this.agentName = agent?.agentName ?? ''
      this.persist()
    },
    logout() {
      this.token = ''
      this.tokenExpiresAt = null
      this.agentId = null
      this.agentName = ''
      localStorage.removeItem(STORAGE_KEY)
    },
    persist() {
      localStorage.setItem(STORAGE_KEY, JSON.stringify({
        token: this.token,
        tokenExpiresAt: this.tokenExpiresAt,
        agentId: this.agentId,
        agentName: this.agentName
      }))
    }
  }
})
