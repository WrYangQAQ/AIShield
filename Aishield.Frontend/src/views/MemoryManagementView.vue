<template>
  <div class="panel">
    <div class="panel-header">
      <div>
        <h2 class="page-title">记忆管理</h2>
        <p class="page-subtitle">
          管理 {{ auth.agentName || '当前 Agent' }} 的记忆安全索引、置信度衰减和待同步动作。
        </p>
      </div>
      <div class="header-actions">
        <el-button :icon="Refresh" circle :loading="activeLoading" title="刷新" @click="refreshActiveTab" />
        <el-button :icon="Plus" type="primary" @click="openPutDialog">新增记忆</el-button>
        <el-button :icon="Upload" @click="openBulkDialog">批量初始化</el-button>
        <el-button :icon="Timer" @click="openBatchDecayDialog">批量衰减</el-button>
      </div>
    </div>

    <div class="panel-body">
      <el-alert
        v-if="!auth.agentId"
        title="请先在 Agent 管理页面选择当前 Agent"
        type="warning"
        show-icon
        :closable="false"
      />

      <el-tabs v-model="activeTab" @tab-change="handleTabChange">
        <el-tab-pane label="记忆索引" name="memories">
          <div class="filter-bar">
            <el-input
              v-model="filters.keyword"
              clearable
              placeholder="记忆 ID 或业务键"
              @keyup.enter="submitSearch"
            />
            <el-select v-model="filters.source" clearable placeholder="记忆来源">
              <el-option
                v-for="option in sourceOptions"
                :key="option.value"
                :label="option.label"
                :value="option.value"
              />
            </el-select>
            <el-select v-model="filters.archiveStatus" placeholder="归档状态">
              <el-option label="全部状态" value="all" />
              <el-option label="正常" value="active" />
              <el-option label="已归档" value="archived" />
            </el-select>
            <el-input-number
              v-model="filters.minConfidence"
              :min="0"
              :max="1"
              :step="0.1"
              :precision="2"
              placeholder="最低置信度"
              controls-position="right"
            />
            <el-select v-model="filters.sortBy" placeholder="排序字段">
              <el-option label="最近更新" value="updatedAt" />
              <el-option label="置信度" value="confidence" />
              <el-option label="创建时间" value="createdAt" />
              <el-option label="最后引用" value="lastPositiveRef" />
            </el-select>
            <el-select v-model="filters.descending" placeholder="排序方式">
              <el-option label="降序" :value="true" />
              <el-option label="升序" :value="false" />
            </el-select>
            <el-button type="primary" :disabled="!auth.agentId" @click="submitSearch">查询</el-button>
            <el-button @click="resetFilters">重置</el-button>
          </div>

          <el-table v-loading="memoryLoading" :data="memories" height="560">
            <el-table-column prop="memoryId" label="记忆 ID" min-width="180" show-overflow-tooltip />
            <el-table-column label="来源" width="110">
              <template #default="{ row }">
                <el-tag type="info">{{ sourceText(row.source) }}</el-tag>
              </template>
            </el-table-column>
            <el-table-column prop="memoryKey" label="业务键" min-width="140" show-overflow-tooltip>
              <template #default="{ row }">{{ row.memoryKey || '-' }}</template>
            </el-table-column>
            <el-table-column label="置信度" width="150">
              <template #default="{ row }">
                <div class="confidence-cell">
                  <el-progress
                    :percentage="Math.round(row.confidence * 100)"
                    :stroke-width="8"
                    :show-text="false"
                    :color="confidenceColor(row.confidence)"
                  />
                  <span>{{ formatConfidence(row.confidence) }}</span>
                </div>
              </template>
            </el-table-column>
            <el-table-column label="最后引用" width="175">
              <template #default="{ row }">{{ formatDateTime(row.lastPositiveRef) }}</template>
            </el-table-column>
            <el-table-column label="更新时间" width="175">
              <template #default="{ row }">{{ formatDateTime(row.updatedAt) }}</template>
            </el-table-column>
            <el-table-column label="状态" width="110">
              <template #default="{ row }">
                <el-tooltip v-if="row.isArchived && row.archiveReason" :content="row.archiveReason">
                  <el-tag type="info">已归档</el-tag>
                </el-tooltip>
                <el-tag v-else-if="row.isArchived" type="info">已归档</el-tag>
                <el-tag v-else type="success">正常</el-tag>
              </template>
            </el-table-column>
            <el-table-column label="操作" width="230" fixed="right">
              <template #default="{ row }">
                <el-button v-if="row.isArchived" link type="primary" @click="openRestoreDialog(row)">
                  恢复
                </el-button>
                <template v-else>
                  <el-button link type="primary" @click="openReferenceDialog(row)">引用</el-button>
                  <el-button link type="warning" @click="decayMemory(row)">衰减</el-button>
                  <el-button link type="danger" @click="archiveMemory(row)">归档</el-button>
                </template>
              </template>
            </el-table-column>
          </el-table>

          <div class="pagination-bar">
            <el-pagination
              v-model:current-page="pageIndex"
              v-model:page-size="pageSize"
              :total="total"
              :page-sizes="[10, 20, 50, 100]"
              layout="total, sizes, prev, pager, next, jumper"
              @size-change="handlePageSizeChange"
              @current-change="searchMemories"
            />
          </div>
        </el-tab-pane>

        <el-tab-pane name="sync">
          <template #label>
            <span>待同步动作 <el-badge :value="syncActions.length" :max="99" /></span>
          </template>
          <div class="sync-toolbar">
            <div>
              <strong>Agent 主记忆库同步队列</strong>
              <span>执行成功后确认；失败动作可在当前页面重新入队。</span>
            </div>
            <el-button :icon="Refresh" :loading="syncLoading" @click="loadSyncActions">刷新队列</el-button>
          </div>

          <el-table v-loading="syncLoading" :data="syncActions" height="560">
            <el-table-column prop="actionId" label="动作 ID" min-width="210" show-overflow-tooltip />
            <el-table-column prop="memoryId" label="记忆 ID" min-width="180" show-overflow-tooltip />
            <el-table-column label="动作" width="130">
              <template #default="{ row }">
                <el-tag :type="syncActionTag(row.actionType)">{{ syncActionText(row.actionType) }}</el-tag>
              </template>
            </el-table-column>
            <el-table-column label="新置信度" width="110">
              <template #default="{ row }">
                {{ row.newConfidence == null ? '-' : formatConfidence(row.newConfidence) }}
              </template>
            </el-table-column>
            <el-table-column prop="reason" label="原因" min-width="180" show-overflow-tooltip>
              <template #default="{ row }">{{ row.reason || '-' }}</template>
            </el-table-column>
            <el-table-column label="创建时间" width="175">
              <template #default="{ row }">{{ formatDateTime(row.createdAt) }}</template>
            </el-table-column>
            <el-table-column label="状态" width="100">
              <template #default="{ row }">
                <el-tag :type="row.localStatus === 'Failed' ? 'danger' : 'warning'">
                  {{ row.localStatus === 'Failed' ? '执行失败' : '待处理' }}
                </el-tag>
              </template>
            </el-table-column>
            <el-table-column label="操作" width="180" fixed="right">
              <template #default="{ row }">
                <el-button
                  v-if="row.localStatus === 'Failed'"
                  link
                  type="primary"
                  @click="retrySyncAction(row)"
                >
                  重试
                </el-button>
                <template v-else>
                  <el-button link type="success" @click="confirmSyncAction(row)">确认</el-button>
                  <el-button link type="danger" @click="failSyncAction(row)">报告失败</el-button>
                </template>
              </template>
            </el-table-column>
          </el-table>
        </el-tab-pane>
      </el-tabs>
    </div>

    <el-dialog v-model="putVisible" title="新增或更新记忆" width="680px">
      <el-form label-position="top">
        <div class="form-grid">
          <el-form-item label="记忆 ID" required>
            <el-input v-model="putForm.memoryId" maxlength="200" />
          </el-form-item>
          <el-form-item label="记忆来源" required>
            <el-select v-model="putForm.source">
              <el-option
                v-for="option in sourceOptions"
                :key="option.value"
                :label="option.label"
                :value="option.value"
              />
            </el-select>
          </el-form-item>
          <el-form-item label="业务键">
            <el-input v-model="putForm.memoryKey" maxlength="200" placeholder="用于识别确定冲突" />
          </el-form-item>
          <el-form-item label="初始置信度">
            <el-input-number
              v-model="putForm.confidence"
              :min="0"
              :max="1"
              :step="0.05"
              :precision="2"
              controls-position="right"
            />
          </el-form-item>
          <el-form-item label="原始创建时间">
            <el-date-picker
              v-model="putForm.memoryCreatedAt"
              type="datetime"
              value-format="YYYY-MM-DDTHH:mm:ss"
              placeholder="默认使用当前时间"
            />
          </el-form-item>
          <el-form-item label="最后正向引用">
            <el-date-picker
              v-model="putForm.lastPositiveRef"
              type="datetime"
              value-format="YYYY-MM-DDTHH:mm:ss"
              placeholder="可留空"
            />
          </el-form-item>
        </div>
        <el-form-item label="记忆正文" required>
          <el-input
            v-model="putForm.content"
            type="textarea"
            :rows="6"
            maxlength="10000"
            show-word-limit
            placeholder="正文仅在写入和冲突检测时提交"
          />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="putVisible = false">取消</el-button>
        <el-button type="primary" :loading="submitLoading" @click="submitMemory">保存</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="bulkVisible" title="批量初始化记忆" width="780px">
      <el-alert
        title="仅首次初始化或历史导入时使用。请输入 MemoryPutRequest 数组。"
        type="info"
        show-icon
        :closable="false"
      />
      <el-input
        v-model="bulkJson"
        class="bulk-json"
        type="textarea"
        :rows="15"
        spellcheck="false"
      />
      <el-checkbox v-model="skipConflictCheck">跳过冲突检测（仅建议用于可信历史数据）</el-checkbox>
      <template #footer>
        <el-button @click="fillBulkExample">填入示例</el-button>
        <el-button @click="bulkVisible = false">取消</el-button>
        <el-button type="primary" :loading="submitLoading" @click="submitBulk">开始导入</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="batchDecayVisible" title="批量衰减" width="480px">
      <el-form label-position="top">
        <el-form-item label="本批处理数量">
          <el-input-number v-model="batchDecayForm.batchSize" :min="1" :max="1000" controls-position="right" />
        </el-form-item>
        <el-form-item label="仅处理此前更新的记忆">
          <el-date-picker
            v-model="batchDecayForm.updatedBefore"
            type="datetime"
            value-format="YYYY-MM-DDTHH:mm:ss"
            placeholder="留空表示不限制"
          />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="batchDecayVisible = false">取消</el-button>
        <el-button type="warning" :loading="submitLoading" @click="submitBatchDecay">执行衰减</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="referenceVisible" title="记录正向引用" width="460px">
      <el-form label-position="top">
        <el-form-item label="引用时间">
          <el-date-picker
            v-model="referenceAt"
            type="datetime"
            value-format="YYYY-MM-DDTHH:mm:ss"
            placeholder="请选择引用时间"
          />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="referenceVisible = false">取消</el-button>
        <el-button type="primary" :loading="submitLoading" @click="submitReference">确认引用</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="restoreVisible" title="恢复记忆" width="460px">
      <el-form label-position="top">
        <el-form-item label="恢复后的置信度">
          <el-input-number
            v-model="restoreForm.confidence"
            :min="0"
            :max="1"
            :step="0.05"
            :precision="2"
            controls-position="right"
          />
        </el-form-item>
        <el-form-item label="恢复确认时间">
          <el-date-picker
            v-model="restoreForm.referencedAt"
            type="datetime"
            value-format="YYYY-MM-DDTHH:mm:ss"
          />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="restoreVisible = false">取消</el-button>
        <el-button type="primary" :loading="submitLoading" @click="submitRestore">恢复</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Plus, Refresh, Timer, Upload } from '@element-plus/icons-vue'
import { memoryApi } from '../api/services'
import { useAuthStore } from '../stores/auth'
import type {
  MemoryListItem,
  MemoryPutRequest,
  MemorySearchRequest,
  MemorySource,
  MemorySyncActionItem,
  MemorySyncActionType
} from '../api/types'

type LocalSyncAction = MemorySyncActionItem & { localStatus?: 'Pending' | 'Failed' }

const auth = useAuthStore()
const activeTab = ref('memories')
const memoryLoading = ref(false)
const syncLoading = ref(false)
const submitLoading = ref(false)
const memories = ref<MemoryListItem[]>([])
const syncActions = ref<LocalSyncAction[]>([])
const total = ref(0)
const pageIndex = ref(1)
const pageSize = ref(20)

const putVisible = ref(false)
const bulkVisible = ref(false)
const batchDecayVisible = ref(false)
const referenceVisible = ref(false)
const restoreVisible = ref(false)
const selectedMemory = ref<MemoryListItem | null>(null)
const bulkJson = ref('[]')
const skipConflictCheck = ref(false)
const referenceAt = ref(nowValue())

const sourceOptions: Array<{ label: string; value: MemorySource }> = [
  { label: '未知', value: 'Unknown' },
  { label: '用户', value: 'User' },
  { label: '系统', value: 'System' },
  { label: '管理员', value: 'Admin' },
  { label: '文档', value: 'Document' },
  { label: '网站', value: 'Website' },
  { label: '对话', value: 'Conversation' },
  { label: '工具', value: 'Tool' },
  { label: '导入', value: 'Import' }
]

const filters = reactive({
  keyword: '',
  source: null as MemorySource | null,
  archiveStatus: 'all' as 'all' | 'active' | 'archived',
  minConfidence: null as number | null,
  sortBy: 'updatedAt' as MemorySearchRequest['sortBy'],
  descending: true
})

const putForm = reactive<MemoryPutRequest>({
  memoryId: '',
  content: '',
  confidence: 1,
  source: 'User',
  memoryKey: '',
  embedding: null,
  lastPositiveRef: '',
  memoryCreatedAt: ''
})

const batchDecayForm = reactive({
  batchSize: 200,
  updatedBefore: ''
})

const restoreForm = reactive({
  confidence: 0.6 as number | null,
  referencedAt: nowValue()
})

const activeLoading = computed(() => activeTab.value === 'memories' ? memoryLoading.value : syncLoading.value)

async function searchMemories() {
  if (!auth.agentId) {
    memories.value = []
    total.value = 0
    return
  }

  memoryLoading.value = true
  try {
    const { data } = await memoryApi.search(buildSearchRequest())
    memories.value = data.items
    total.value = data.total
    pageIndex.value = data.pageIndex
    pageSize.value = data.pageSize
  } catch {
    memories.value = []
    total.value = 0
  } finally {
    memoryLoading.value = false
  }
}

function buildSearchRequest(): MemorySearchRequest {
  return {
    keyword: filters.keyword || null,
    source: filters.source,
    isArchived: filters.archiveStatus === 'all' ? null : filters.archiveStatus === 'archived',
    minConfidence: filters.minConfidence,
    maxConfidence: null,
    archiveReason: null,
    lastPositiveRefFrom: null,
    lastPositiveRefTo: null,
    sortBy: filters.sortBy,
    descending: filters.descending,
    pageIndex: pageIndex.value,
    pageSize: pageSize.value
  }
}

function submitSearch() {
  pageIndex.value = 1
  searchMemories()
}

function resetFilters() {
  filters.keyword = ''
  filters.source = null
  filters.archiveStatus = 'all'
  filters.minConfidence = null
  filters.sortBy = 'updatedAt'
  filters.descending = true
  pageIndex.value = 1
  searchMemories()
}

function handlePageSizeChange() {
  pageIndex.value = 1
  searchMemories()
}

function handleTabChange(name: string | number) {
  if (name === 'sync' && syncActions.value.length === 0) {
    loadSyncActions()
  }
}

function refreshActiveTab() {
  if (activeTab.value === 'sync') loadSyncActions()
  else searchMemories()
}

function openPutDialog() {
  if (!ensureAgent()) return
  Object.assign(putForm, {
    memoryId: '',
    content: '',
    confidence: 1,
    source: 'User',
    memoryKey: '',
    embedding: null,
    lastPositiveRef: '',
    memoryCreatedAt: nowValue()
  })
  putVisible.value = true
}

function openBulkDialog() {
  if (!ensureAgent()) return
  bulkVisible.value = true
}

function openBatchDecayDialog() {
  if (!ensureAgent()) return
  batchDecayVisible.value = true
}

async function submitMemory() {
  if (!putForm.memoryId.trim() || !putForm.content.trim()) {
    ElMessage.warning('请填写记忆 ID 和记忆正文')
    return
  }

  submitLoading.value = true
  try {
    const { data } = await memoryApi.put({
      ...putForm,
      memoryId: putForm.memoryId.trim(),
      content: putForm.content.trim(),
      memoryKey: putForm.memoryKey?.trim() || null
    })
    if (!data.success) {
      ElMessage.error(data.message || '记忆保存失败')
      return
    }
    ElMessage.success(data.data?.details?.action === 'updated' ? '记忆已更新' : '记忆已保存')
    putVisible.value = false
    await refreshData()
  } finally {
    submitLoading.value = false
  }
}

function fillBulkExample() {
  bulkJson.value = JSON.stringify([
    {
      memoryId: 'memory-001',
      content: '用户偏好简洁、直接的回答。',
      confidence: 0.9,
      source: 'User',
      memoryKey: 'response_style',
      lastPositiveRef: nowValue(),
      memoryCreatedAt: nowValue()
    }
  ], null, 2)
}

async function submitBulk() {
  let parsed: unknown
  try {
    parsed = JSON.parse(bulkJson.value)
  } catch {
    ElMessage.error('JSON 格式不正确')
    return
  }

  if (!Array.isArray(parsed) || parsed.length === 0) {
    ElMessage.warning('请输入至少一条记忆')
    return
  }

  submitLoading.value = true
  try {
    const { data } = await memoryApi.bulkPut({
      memories: parsed as MemoryPutRequest[],
      skipConflictCheck: skipConflictCheck.value
    })
    if (!data.success) {
      ElMessage.error(data.message || '批量初始化失败')
      return
    }
    const details = data.data?.details
    ElMessage.success(
      details
        ? `处理 ${details.total} 条，成功 ${details.succeeded} 条，冲突 ${details.conflicts} 项`
        : '批量初始化完成'
    )
    bulkVisible.value = false
    await refreshData()
  } finally {
    submitLoading.value = false
  }
}

async function decayMemory(row: MemoryListItem) {
  try {
    await ElMessageBox.confirm(
      `确定对记忆“${row.memoryId}”执行一次置信度衰减吗？`,
      '确认衰减',
      { type: 'warning', confirmButtonText: '执行', cancelButtonText: '取消' }
    )
  } catch {
    return
  }
  const { data } = await memoryApi.decay({ memoryId: row.memoryId })
  if (!data.success) {
    ElMessage.error(data.message || '衰减失败')
    return
  }
  ElMessage.success('记忆衰减完成')
  await refreshData()
}

async function archiveMemory(row: MemoryListItem) {
  try {
    await ElMessageBox.confirm(
      `归档后会生成一条同步动作，确认归档“${row.memoryId}”吗？`,
      '确认归档',
      { type: 'warning', confirmButtonText: '归档', cancelButtonText: '取消' }
    )
  } catch {
    return
  }
  const { data } = await memoryApi.remove(row.memoryId)
  if (!data.success) {
    ElMessage.error(data.message || '归档失败')
    return
  }
  ElMessage.success('记忆已归档')
  await refreshData()
}

function openReferenceDialog(row: MemoryListItem) {
  selectedMemory.value = row
  referenceAt.value = nowValue()
  referenceVisible.value = true
}

async function submitReference() {
  if (!selectedMemory.value || !referenceAt.value) return
  submitLoading.value = true
  try {
    const { data } = await memoryApi.updateReference(selectedMemory.value.memoryId, {
      referencedAt: referenceAt.value
    })
    if (!data.success) {
      ElMessage.error(data.message || '引用时间更新失败')
      return
    }
    ElMessage.success('正向引用已记录')
    referenceVisible.value = false
    await searchMemories()
  } finally {
    submitLoading.value = false
  }
}

function openRestoreDialog(row: MemoryListItem) {
  selectedMemory.value = row
  restoreForm.confidence = Math.max(row.confidence, 0.6)
  restoreForm.referencedAt = nowValue()
  restoreVisible.value = true
}

async function submitRestore() {
  if (!selectedMemory.value) return
  submitLoading.value = true
  try {
    const { data } = await memoryApi.restore(selectedMemory.value.memoryId, {
      confidence: restoreForm.confidence,
      referencedAt: restoreForm.referencedAt
    })
    if (!data.success) {
      ElMessage.error(data.message || '恢复失败')
      return
    }
    ElMessage.success('记忆已恢复')
    restoreVisible.value = false
    await refreshData()
  } finally {
    submitLoading.value = false
  }
}

async function submitBatchDecay() {
  submitLoading.value = true
  try {
    const { data } = await memoryApi.batchDecay({
      batchSize: batchDecayForm.batchSize,
      updatedBefore: batchDecayForm.updatedBefore || null
    })
    if (!data.success) {
      ElMessage.error(data.message || '批量衰减失败')
      return
    }
    const details = data.data?.details
    ElMessage.success(
      details
        ? `已处理 ${details.processed} 条，衰减 ${details.decayed} 条，归档 ${details.archived} 条`
        : '批量衰减完成'
    )
    batchDecayVisible.value = false
    await refreshData()
  } finally {
    submitLoading.value = false
  }
}

async function loadSyncActions() {
  if (!auth.agentId) {
    syncActions.value = []
    return
  }
  syncLoading.value = true
  try {
    const { data } = await memoryApi.getPendingSyncActions(100)
    syncActions.value = data.map((item) => ({ ...item, localStatus: 'Pending' }))
  } catch {
    syncActions.value = []
  } finally {
    syncLoading.value = false
  }
}

async function confirmSyncAction(row: LocalSyncAction) {
  const { data } = await memoryApi.confirmSyncAction(row.actionId)
  if (!data.success) {
    ElMessage.error(data.message || '同步确认失败')
    return
  }
  syncActions.value = syncActions.value.filter((item) => item.actionId !== row.actionId)
  ElMessage.success('同步动作已确认')
}

async function failSyncAction(row: LocalSyncAction) {
  let failureMessage = ''
  try {
    const result = await ElMessageBox.prompt('请填写 Agent 执行失败的原因', '报告同步失败', {
      confirmButtonText: '提交',
      cancelButtonText: '取消',
      inputPattern: /\S+/,
      inputErrorMessage: '失败原因不能为空'
    })
    failureMessage = result.value.trim()
  } catch {
    return
  }
  const { data } = await memoryApi.failSyncAction(row.actionId, { failureMessage })
  if (!data.success) {
    ElMessage.error(data.message || '失败状态提交失败')
    return
  }
  row.localStatus = 'Failed'
  ElMessage.success('已记录失败原因')
}

async function retrySyncAction(row: LocalSyncAction) {
  const { data } = await memoryApi.retrySyncAction(row.actionId)
  if (!data.success) {
    ElMessage.error(data.message || '重新入队失败')
    return
  }
  row.localStatus = 'Pending'
  ElMessage.success('同步动作已重新入队')
}

async function refreshData() {
  await Promise.all([searchMemories(), loadSyncActions()])
}

function ensureAgent() {
  if (auth.agentId) return true
  ElMessage.warning('请先选择当前 Agent')
  return false
}

function sourceText(source: MemorySource) {
  return sourceOptions.find((option) => option.value === source)?.label || source
}

function syncActionText(action: MemorySyncActionType) {
  const map: Record<MemorySyncActionType, string> = {
    UpdateConfidence: '更新置信度',
    Archive: '归档',
    Restore: '恢复'
  }
  return map[action]
}

function syncActionTag(action: MemorySyncActionType) {
  if (action === 'Archive') return 'danger'
  if (action === 'Restore') return 'success'
  return 'warning'
}

function confidenceColor(value: number) {
  if (value < 0.3) return '#f5222d'
  if (value < 0.6) return '#fa8c16'
  return '#22c55e'
}

function formatConfidence(value: number) {
  return `${Math.round(value * 100)}%`
}

function formatDateTime(value: string | null) {
  if (!value) return '-'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return value
  const pad = (number: number) => String(number).padStart(2, '0')
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())} ${pad(date.getHours())}:${pad(date.getMinutes())}`
}

function nowValue() {
  const date = new Date()
  const pad = (number: number) => String(number).padStart(2, '0')
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}:${pad(date.getSeconds())}`
}

onMounted(searchMemories)
</script>

<style scoped>
.header-actions,
.filter-bar,
.sync-toolbar,
.confidence-cell,
.pagination-bar {
  display: flex;
  align-items: center;
}

.header-actions {
  flex-wrap: wrap;
  justify-content: flex-end;
  gap: 8px;
}

.filter-bar {
  flex-wrap: wrap;
  gap: 10px;
  margin-bottom: 16px;
}

.filter-bar :deep(.el-input) {
  width: 220px;
}

.filter-bar :deep(.el-select) {
  width: 140px;
}

.filter-bar :deep(.el-input-number) {
  width: 150px;
}

.confidence-cell {
  gap: 10px;
}

.confidence-cell :deep(.el-progress) {
  width: 84px;
}

.confidence-cell span {
  width: 38px;
  color: #475569;
  font-variant-numeric: tabular-nums;
}

.pagination-bar {
  justify-content: flex-end;
  margin-top: 16px;
}

.sync-toolbar {
  justify-content: space-between;
  gap: 16px;
  margin-bottom: 16px;
}

.sync-toolbar strong {
  display: block;
  margin-bottom: 4px;
  color: #0f172a;
}

.sync-toolbar span {
  color: var(--color-muted);
  font-size: 13px;
}

.form-grid :deep(.el-select),
.form-grid :deep(.el-date-editor),
.form-grid :deep(.el-input-number) {
  width: 100%;
}

:deep(.el-input-number .el-input__inner) {
  text-align: left;
}

.bulk-json {
  margin: 16px 0 12px;
}

.bulk-json :deep(textarea) {
  font-family: Consolas, "Courier New", monospace;
  line-height: 1.55;
}

:deep(.el-badge) {
  margin-left: 6px;
}

@media (max-width: 980px) {
  .panel-header {
    align-items: flex-start;
    flex-direction: column;
  }

  .header-actions {
    justify-content: flex-start;
  }
}

@media (max-width: 760px) {
  .filter-bar :deep(.el-input),
  .filter-bar :deep(.el-select),
  .filter-bar :deep(.el-input-number) {
    width: 100%;
  }

  .sync-toolbar {
    align-items: flex-start;
    flex-direction: column;
  }
}
</style>
