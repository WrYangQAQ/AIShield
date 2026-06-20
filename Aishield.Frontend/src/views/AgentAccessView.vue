<template>
  <div class="panel">
    <div class="panel-header">
      <div>
        <h2 class="page-title">Agent 管理</h2>
        <p class="page-subtitle">查看已接入 AIShield 的 Agent，管理当前操作对象和接入状态。</p>
      </div>
      <div class="toolbar">
        <el-button @click="guideVisible = true">接入说明</el-button>
        <el-button type="primary" @click="$router.push('/register')">注册新 Agent</el-button>
        <el-button :icon="Refresh" circle :loading="loading" @click="loadAgents" />
      </div>
    </div>

    <div class="panel-body">
      <el-table v-loading="loading" :data="agents" height="560">
        <el-table-column prop="agentName" label="Agent 名称" min-width="180" show-overflow-tooltip />
        <el-table-column prop="scenario" label="使用场景" min-width="240" show-overflow-tooltip />
        <el-table-column label="Key 预览" width="230">
          <template #default="{ row }">
            <span class="key-preview">{{ row.agentKeyPreview || '-' }}</span>
          </template>
        </el-table-column>
        <el-table-column label="状态" width="110">
          <template #default="{ row }">
            <el-tag :type="row.enabled ? 'success' : 'info'">{{ row.enabled ? '启用' : '禁用' }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column label="创建时间" width="180">
          <template #default="{ row }">
            {{ formatDateTime(row.createdAt) }}
          </template>
        </el-table-column>
        <el-table-column label="操作" width="150" align="center" header-align="center" fixed="right">
          <template #default="{ row }">
            <div class="icon-actions">
              <el-tooltip content="详情" placement="top">
                <el-button :icon="Document" circle text type="primary" @click="showDetail(row)" />
              </el-tooltip>
              <el-tooltip content="编辑" placement="top">
                <el-button :icon="EditPen" circle text type="primary" @click="openEdit(row)" />
              </el-tooltip>
              <el-tooltip content="删除" placement="top">
                <el-button :icon="Delete" circle text type="danger" @click="deleteAgent(row)" />
              </el-tooltip>
            </div>
          </template>
        </el-table-column>
        <el-table-column label="当前" width="110" fixed="right">
          <template #default="{ row }">
            <el-switch
              :model-value="auth.agentId === row.agentId"
              @change="(value: string | number | boolean) => toggleCurrentAgent(row, Boolean(value))"
            />
          </template>
        </el-table-column>
      </el-table>
    </div>

    <el-dialog v-model="guideVisible" title="接入说明" width="760px">
      <el-descriptions :column="2" border>
        <el-descriptions-item label="Agent">xxxAgent</el-descriptions-item>
        <el-descriptions-item label="xxxx">1</el-descriptions-item>
        <el-descriptions-item label="端口号">5069</el-descriptions-item>
        <el-descriptions-item label="鉴权方式">X-API-Key 请求头</el-descriptions-item>
      </el-descriptions>

      <h3>接入示例</h3>
      <pre class="json-box">fetch('http://localhost:5069/api/security/check-input', {
  method: 'POST',
  headers: {
    'Content-Type': 'application/json',
    'X-API-Key': '注册成功时复制的 Agent Key'
  },
  body: JSON.stringify({
    subjectHash: '可选的匿名主体哈希',
    content: '用户输入内容'
  })
})</pre>
    </el-dialog>

    <el-dialog v-model="detailVisible" title="Agent 详情" width="640px">
      <el-descriptions v-if="selectedAgent" :column="1" border>
        <el-descriptions-item label="Agent ID">{{ selectedAgent.agentId }}</el-descriptions-item>
        <el-descriptions-item label="Agent 名称">{{ selectedAgent.agentName }}</el-descriptions-item>
        <el-descriptions-item label="使用场景">{{ selectedAgent.scenario || '-' }}</el-descriptions-item>
        <el-descriptions-item label="Agent Key 预览">{{ selectedAgent.agentKeyPreview || '-' }}</el-descriptions-item>
        <el-descriptions-item label="启用状态">{{ selectedAgent.enabled ? '启用' : '禁用' }}</el-descriptions-item>
        <el-descriptions-item label="创建时间">{{ formatDateTime(selectedAgent.createdAt) }}</el-descriptions-item>
        <el-descriptions-item label="最后使用时间">{{ formatDateTime(selectedAgent.lastUsedAt) }}</el-descriptions-item>
      </el-descriptions>
    </el-dialog>

    <el-dialog v-model="editVisible" title="编辑 Agent" width="520px">
      <el-form label-position="top" :model="editForm">
        <el-form-item label="Agent 名称">
          <el-input v-model="editForm.agentName" maxlength="100" show-word-limit />
        </el-form-item>
        <el-form-item label="使用场景">
          <el-input v-model="editForm.scenario" type="textarea" :rows="4" maxlength="500" show-word-limit />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="editVisible = false">取消</el-button>
        <el-button type="primary" :loading="saving" @click="saveAgent">保存</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Delete, Document, EditPen, Refresh } from '@element-plus/icons-vue'
import { agentApi } from '../api/services'
import { useAuthStore } from '../stores/auth'
import type { AgentSummaryResponse } from '../api/types'

const auth = useAuthStore()
const loading = ref(false)
const saving = ref(false)
const agents = ref<AgentSummaryResponse[]>([])
const guideVisible = ref(false)
const detailVisible = ref(false)
const editVisible = ref(false)
const selectedAgent = ref<AgentSummaryResponse | null>(null)
const editForm = reactive({
  id: 0,
  agentName: '',
  scenario: ''
})

async function loadAgents() {
  loading.value = true
  try {
    const { data } = await agentApi.list()
    agents.value = data
  } finally {
    loading.value = false
  }
}

function toggleCurrentAgent(agent: AgentSummaryResponse, enabled: boolean) {
  if (enabled) {
    auth.setActiveAgent(agent)
    ElMessage.success(`当前 Agent 已切换为 ${agent.agentName}`)
    return
  }

  if (auth.agentId === agent.agentId) {
    auth.setActiveAgent(null)
    ElMessage.success('已切换为全局模式')
  }
}

function showDetail(agent: AgentSummaryResponse) {
  selectedAgent.value = agent
  detailVisible.value = true
}

function openEdit(agent: AgentSummaryResponse) {
  editForm.id = agent.agentId
  editForm.agentName = agent.agentName
  editForm.scenario = agent.scenario
  editVisible.value = true
}

async function saveAgent() {
  if (!editForm.agentName.trim()) {
    ElMessage.warning('Agent 名称不能为空')
    return
  }

  saving.value = true
  try {
    const { data } = await agentApi.update(editForm.id, {
      id: editForm.id,
      agentName: editForm.agentName.trim(),
      scenario: editForm.scenario.trim()
    })

    const index = agents.value.findIndex((item) => item.agentId === data.agentId)
    if (index >= 0) {
      agents.value[index] = data
    }

    if (auth.agentId === data.agentId) {
      auth.setActiveAgent(data)
    }

    editVisible.value = false
    ElMessage.success('Agent 信息已更新')
  } finally {
    saving.value = false
  }
}

async function deleteAgent(agent: AgentSummaryResponse) {
  await ElMessageBox.confirm(
    `确认删除 Agent「${agent.agentName}」？删除后该 Agent 绑定的规则关系会同步移除，历史审计日志会保留。`,
    '删除 Agent',
    { type: 'warning' }
  )

  await agentApi.remove(agent.agentId)
  agents.value = agents.value.filter((item) => item.agentId !== agent.agentId)

  if (auth.agentId === agent.agentId) {
    auth.setActiveAgent(null)
  }

  if (selectedAgent.value?.agentId === agent.agentId) {
    selectedAgent.value = null
    detailVisible.value = false
  }

  ElMessage.success('Agent 已删除')
}

function formatDateTime(value?: string | null) {
  if (!value) return '-'

  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return value

  const pad = (num: number) => String(num).padStart(2, '0')
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())} ${pad(date.getHours())}:${pad(date.getMinutes())}:${pad(date.getSeconds())}`
}

onMounted(loadAgents)
</script>

<style scoped>
h3 {
  margin: 24px 0 12px;
}

.key-preview {
  display: inline-block;
  max-width: 100%;
  overflow: hidden;
  white-space: nowrap;
  text-overflow: ellipsis;
  vertical-align: middle;
}

.icon-actions {
  display: flex;
  align-items: center;
  gap: 5px;
}

.icon-actions :deep(.el-button + .el-button) {
  margin-left: 0;
}

.icon-actions :deep(.el-button) {
  width: 34px;
  height: 34px;
  font-size: 18px;
}

.icon-actions :deep(.el-icon) {
  font-size: 18px;
}
</style>
