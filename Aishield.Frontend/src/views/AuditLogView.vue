<template>
  <div class="panel">
    <div class="panel-header">
      <div>
        <h2 class="page-title">审计日志</h2>
        <p class="page-subtitle">查看输入检测、输出过滤和工具调用防护产生的审计记录。</p>
      </div>
      <div class="header-actions">
        <el-button class="refresh-button" :icon="Refresh" circle :loading="loading" @click="searchRecords" />
        <el-button type="primary" :loading="loading" @click="submitSearch">查询</el-button>
        <el-button @click="resetFilters">重置</el-button>
      </div>
    </div>

    <div class="panel-body">
      <div class="filter-bar">
        <el-input v-model="filters.keyword" clearable placeholder="关键词：原因 / 原始内容 / 处理后内容" @keyup.enter="submitSearch" />
        <el-input v-model="filters.hitRule" clearable placeholder="命中规则，如 PI001" @keyup.enter="submitSearch" />
        <el-select v-model="filters.direction" clearable placeholder="事件类型">
          <el-option label="输入检测" value="Input" />
          <el-option label="输出过滤" value="Output" />
          <el-option label="工具调用" value="ToolCall" />
        </el-select>
        <el-select v-model="filters.riskLevel" clearable placeholder="风险等级">
          <el-option v-for="level in riskLevels" :key="level" :label="level" :value="level" />
        </el-select>
        <el-select v-model="filters.action" clearable placeholder="处理动作">
          <el-option v-for="action in actions" :key="action" :label="action" :value="action" />
        </el-select>
        <el-date-picker
          class="time-range-picker"
          v-model="timeRange"
          type="datetimerange"
          start-placeholder="开始时间"
          end-placeholder="结束时间"
          value-format="YYYY-MM-DDTHH:mm:ss"
        />
      </div>

      <el-table v-loading="loading" :data="records" height="560">
        <el-table-column label="时间" width="180">
          <template #default="{ row }">
            {{ formatDateTime(row.createdAt) }}
          </template>
        </el-table-column>
        <el-table-column prop="agentName" label="Agent" width="150" />
        <el-table-column prop="direction" label="事件类型" width="110">
          <template #default="{ row }">{{ directionText(row.direction) }}</template>
        </el-table-column>
        <el-table-column label="风险等级" width="110">
          <template #default="{ row }">
            <el-tag :type="riskTag(row.riskLevel)">{{ row.riskLevel }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="action" label="动作" width="90" />
        <el-table-column prop="durationMs" label="耗时(ms)" width="100" />
        <el-table-column prop="hitRules" label="命中规则" width="160" show-overflow-tooltip />
        <el-table-column prop="reason" label="原因" min-width="220" show-overflow-tooltip />
        <el-table-column label="详情" width="90" fixed="right">
          <template #default="{ row }">
            <el-button link type="primary" @click="showRecord(row)">查看</el-button>
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
          @current-change="searchRecords"
        />
      </div>
    </div>

    <el-dialog v-model="detailVisible" title="审计详情" width="760px">
      <template v-if="selectedRecord">
        <el-descriptions :column="2" border>
          <el-descriptions-item label="时间">{{ formatDateTime(selectedRecord.createdAt) }}</el-descriptions-item>
          <el-descriptions-item label="Agent">{{ selectedRecord.agentName || '-' }}</el-descriptions-item>
          <el-descriptions-item label="事件类型">{{ directionText(selectedRecord.direction) }}</el-descriptions-item>
          <el-descriptions-item label="风险等级">
            <el-tag :type="riskTag(selectedRecord.riskLevel)">{{ selectedRecord.riskLevel }}</el-tag>
          </el-descriptions-item>
          <el-descriptions-item label="处理动作">{{ selectedRecord.action }}</el-descriptions-item>
          <el-descriptions-item label="耗时">{{ selectedRecord.durationMs }} ms</el-descriptions-item>
          <el-descriptions-item label="命中规则">{{ selectedRecord.hitRules || '-' }}</el-descriptions-item>
          <el-descriptions-item label="客户端 IP">{{ selectedRecord.clientIp || '-' }}</el-descriptions-item>
        </el-descriptions>

        <div class="detail-section">
          <h3>处理原因</h3>
          <p>{{ selectedRecord.reason || '-' }}</p>
        </div>

        <div class="detail-section">
          <h3>原始内容</h3>
          <pre>{{ selectedRecord.originalContent || '-' }}</pre>
        </div>

        <div class="detail-section">
          <h3>处理后内容</h3>
          <pre>{{ selectedRecord.processedContent || '-' }}</pre>
        </div>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue'
import { ElMessage } from 'element-plus'
import { Refresh } from '@element-plus/icons-vue'
import { auditApi } from '../api/services'
import { useAuthStore } from '../stores/auth'
import type { AuditRecord, AuditSearchRequest } from '../api/types'

const loading = ref(false)
const records = ref<AuditRecord[]>([])
const detailVisible = ref(false)
const selectedRecord = ref<AuditRecord | null>(null)
const auth = useAuthStore()
const total = ref(0)
const pageIndex = ref(1)
const pageSize = ref(20)
const timeRange = ref<[string, string] | null>(null)
const riskLevels = ['None', 'Low', 'Medium', 'High', 'Critical']
const actions = ['Allow', 'Warn', 'Block', 'Mask', 'NeedApproval']
const filters = reactive({
  keyword: '',
  hitRule: '',
  direction: '',
  riskLevel: '',
  action: ''
})

async function searchRecords() {
  loading.value = true
  try {
    const request = buildSearchRequest()
    const { data } = await auditApi.search(request)
    records.value = data.items
    total.value = data.total
    pageIndex.value = data.pageIndex
    pageSize.value = data.pageSize
  } catch {
    records.value = []
    total.value = 0
  } finally {
    loading.value = false
  }
}

function submitSearch() {
  pageIndex.value = 1
  searchRecords()
}

function buildSearchRequest(): AuditSearchRequest {
  return {
    agentId: auth.agentId,
    keyword: filters.keyword || null,
    hitRule: filters.hitRule || null,
    direction: filters.direction || null,
    riskLevel: filters.riskLevel || null,
    action: filters.action || null,
    startTime: timeRange.value?.[0] ?? null,
    endTime: timeRange.value?.[1] ?? null,
    pageIndex: pageIndex.value,
    pageSize: pageSize.value
  }
}

function resetFilters() {
  filters.keyword = ''
  filters.hitRule = ''
  filters.direction = ''
  filters.riskLevel = ''
  filters.action = ''
  timeRange.value = null
  pageIndex.value = 1
  searchRecords()
}

function handlePageSizeChange() {
  pageIndex.value = 1
  searchRecords()
}

function formatDateTime(value: string) {
  if (!value) return '-'

  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return value

  const pad = (num: number) => String(num).padStart(2, '0')
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())} ${pad(date.getHours())}:${pad(date.getMinutes())}:${pad(date.getSeconds())}`
}

function showRecord(row: AuditRecord) {
  selectedRecord.value = row
  detailVisible.value = true
}

function directionText(value: string) {
  const map: Record<string, string> = {
    Input: '输入检测',
    Output: '输出过滤',
    ToolCall: '工具调用'
  }
  return map[value] || value
}

function riskTag(level: string) {
  if (level === 'High' || level === 'Critical') return 'danger'
  if (level === 'Medium') return 'warning'
  if (level === 'Low') return 'primary'
  return 'info'
}

onMounted(searchRecords)
</script>

<style scoped>
.filter-bar {
  display: flex;
  flex-wrap: wrap;
  gap: 12px;
  margin-bottom: 16px;
  align-items: center;
}

.filter-bar :deep(.el-input) {
  width: 260px;
}

.filter-bar :deep(.el-select) {
  width: 140px;
}

.time-range-picker {
  width: 330px;
  flex: 0 0 330px;
}

.time-range-picker :deep(.el-range-input) {
  width: 138px;
  flex: 0 0 138px;
}

.time-range-picker :deep(.el-range-separator) {
  flex: 0 0 8px;
  width: 8px;
  padding: 0;
}

.header-actions {
  display: flex;
  gap: 8px;
  justify-content: flex-end;
  align-items: flex-start;
  white-space: nowrap;
}

.refresh-button {
  margin-top: -2px;
}

.pagination-bar {
  display: flex;
  justify-content: flex-end;
  margin-top: 16px;
}

.detail-section {
  margin-top: 18px;
}

.detail-section h3 {
  margin: 0 0 8px;
  color: #0f172a;
  font-size: 15px;
}

.detail-section p,
.detail-section pre {
  margin: 0;
  padding: 12px;
  border: 1px solid var(--color-border);
  border-radius: 8px;
  background: #f8fafc;
  color: #334155;
  line-height: 1.6;
}

.detail-section pre {
  max-height: 180px;
  overflow: auto;
  white-space: pre-wrap;
  word-break: break-word;
}

@media (max-width: 1300px) {
  .filter-bar {
    gap: 10px;
  }
}

@media (max-width: 760px) {
  .filter-bar :deep(.el-input),
  .filter-bar :deep(.el-select),
  .time-range-picker {
    width: 100%;
    flex-basis: 100%;
  }
}
</style>
