<template>
  <div class="dashboard">
    <section class="metric-grid">
      <div v-for="metric in metrics" :key="metric.label" class="metric-card panel">
        <div>
          <span>{{ metric.label }}</span>
          <strong :class="metric.tone">{{ metric.value }}</strong>
          <small>{{ metric.delta }}</small>
        </div>
        <el-icon><component :is="metric.icon" /></el-icon>
      </div>
    </section>

    <section class="dashboard-main">
      <div class="panel trend-panel">
        <div class="panel-header">
          <h2 class="page-title">风险趋势</h2>
          <div class="toolbar">
            <el-select v-model="range" style="width: 110px" @change="loadDashboardData">
              <el-option label="近7天" value="7" />
              <el-option label="近30天" value="30" />
            </el-select>
            <el-button :icon="Refresh" circle :loading="loading" @click="loadDashboardData" />
          </div>
        </div>
        <div ref="trendChartRef" class="chart"></div>
      </div>

      <div class="panel">
        <div class="panel-header">
          <h2 class="page-title">最近审计日志</h2>
          <el-button link type="primary" @click="$router.push('/audit')">查看全部</el-button>
        </div>
        <div class="panel-body">
          <el-table :data="recentRecords" height="280">
            <el-table-column label="时间" width="170">
              <template #default="{ row }">
                {{ formatDateTime(row.createdAt) }}
              </template>
            </el-table-column>
            <el-table-column prop="direction" label="事件类型" width="110" />
            <el-table-column label="风险等级" width="100">
              <template #default="{ row }">
                <el-tag :type="riskTag(row.riskLevel)">{{ row.riskLevel }}</el-tag>
              </template>
            </el-table-column>
            <el-table-column prop="action" label="动作" width="90" />
            <el-table-column prop="reason" label="描述" min-width="180" show-overflow-tooltip />
          </el-table>
        </div>
      </div>
    </section>

    <section class="dashboard-bottom">
      <div class="panel">
        <div class="panel-header">
          <h2 class="page-title">风险分布</h2>
        </div>
        <div ref="riskChartRef" class="small-chart"></div>
      </div>

      <div class="panel">
        <div class="panel-header">
          <h2 class="page-title">策略状态</h2>
          <el-button link type="primary" @click="$router.push('/rules')">策略管理</el-button>
        </div>
        <div class="panel-body policy-list">
          <div v-for="item in policyStatus" :key="item.label">
            <span>{{ item.label }}</span>
            <el-tag type="success">已启用</el-tag>
          </div>
        </div>
      </div>

      <div class="panel health-panel">
        <div class="panel-header">
          <h2 class="page-title">Agent 健康状态</h2>
        </div>
        <div class="panel-body health-body">
          <div class="score" :style="{ color: healthScoreColor }">
            <strong>{{ formattedHealth.healthScore }}</strong>
            <span>{{ healthScoreText }}</span>
          </div>
          <dl class="health-metrics">
            <dt>平均响应时间</dt>
            <dd>{{ formattedHealth.averageResponseTime }} ms</dd>
            <dt>错误率</dt>
            <dd>{{ formattedHealth.errorRate }}%</dd>
            <dt>可用性</dt>
            <dd><span class="status-dot"></span>{{ formattedHealth.availability }}%</dd>
          </dl>
          <div class="health-status-bar">
            <span v-for="item in healthStatusItems" :key="item"><i></i>{{ item }}：正常</span>
          </div>
        </div>
      </div>
    </section>
  </div>
</template>

<script setup lang="ts">
import { computed, nextTick, onMounted, onUnmounted, ref } from 'vue'
import * as echarts from 'echarts'
import { DataAnalysis, Document, Lock, Refresh, Warning } from '@element-plus/icons-vue'
import { auditApi, dashboardApi } from '../api/services'
import { useAuthStore } from '../stores/auth'
import type { AuditRecord, HealthStatusResponse, OverviewResponse, RiskTrendPoint } from '../api/types'

const auth = useAuthStore()
const range = ref('7')
const records = ref<AuditRecord[]>([])
const trendPoints = ref<RiskTrendPoint[]>([])
const overview = ref<OverviewResponse>({
  dayRequestCount: 0,
  dayBlockedCount: 0,
  dayMaskedCount: 0,
  dayRiskEventCount: 0
})
const healthStatus = ref<HealthStatusResponse>({
  healthScore: 100,
  averageResponseTime: 0,
  errorRate: 0,
  availability: 1
})
const loading = ref(false)
const trendChartRef = ref<HTMLDivElement>()
const riskChartRef = ref<HTMLDivElement>()
let trendChart: echarts.ECharts | null = null
let riskChart: echarts.ECharts | null = null

const recentRecords = computed(() => records.value.slice(0, 6))

const formattedHealth = computed(() => ({
  healthScore: Math.floor(clamp(healthStatus.value.healthScore, 0, 100)),
  averageResponseTime: Math.ceil(Math.max(0, healthStatus.value.averageResponseTime)),
  errorRate: formatRateAsPercent(healthStatus.value.errorRate),
  availability: formatRateAsPercent(healthStatus.value.availability)
}))

const healthScoreText = computed(() => {
  const score = formattedHealth.value.healthScore

  if (score === 100) return '非常安全'
  if (score >= 90) return '运行良好'
  if (score >= 70) return '需要关注'
  return '风险较高'
})

const healthScoreColor = computed(() => {
  const score = formattedHealth.value.healthScore

  if (score < 80) return '#EE6666'
  if (score < 90) return '#FAC858'
  return 'var(--color-success)'
})

const healthStatusItems = ['输入检测', '输出过滤', '规则管理', '审计日志']

const metrics = computed(() => [
  {
    label: '今日请求数',
    value: overview.value.dayRequestCount,
    delta: '来自总览统计接口',
    icon: DataAnalysis,
    tone: ''
  },
  {
    label: '拦截次数',
    value: overview.value.dayBlockedCount,
    delta: '今日安全拦截',
    icon: Lock,
    tone: ''
  },
  {
    label: '脱敏次数',
    value: overview.value.dayMaskedCount,
    delta: '今日输出脱敏',
    icon: Document,
    tone: ''
  },
  {
    label: '高风险事件',
    value: overview.value.dayRiskEventCount,
    delta: '今日风险事件',
    icon: Warning,
    tone: 'danger'
  }
])

const policyStatus = [
  { label: '输入检测策略' },
  { label: '输出过滤策略' },
  { label: '工具调用防护策略' },
  { label: '敏感信息脱敏策略' }
]

function riskTag(level: string) {
  if (level === 'High' || level === 'Critical') return 'danger'
  if (level === 'Medium') return 'warning'
  if (level === 'Low') return 'primary'
  return 'info'
}

function formatDateTime(value: string) {
  if (!value) return '-'

  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return value

  const pad = (num: number) => String(num).padStart(2, '0')
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())} ${pad(date.getHours())}:${pad(date.getMinutes())}:${pad(date.getSeconds())}`
}

async function loadDashboardData() {
  loading.value = true

  try {
    const agentId = auth.agentId
    const [auditResponse, trendResponse] = await Promise.all([
      auditApi.list({ agentId, limit: 100 }),
      dashboardApi.getRiskTrend(agentId, Number(range.value))
    ])

    records.value = auditResponse.data
    trendPoints.value = trendResponse.data.points

    if (agentId) {
      const [healthResponse, overviewResponse] = await Promise.all([
        dashboardApi.getHealthStatus(agentId),
        dashboardApi.getOverview(agentId)
      ])

      healthStatus.value = healthResponse.data
      overview.value = overviewResponse.data
    }
  } catch {
    records.value = []
    trendPoints.value = []
  } finally {
    loading.value = false
  }

  await nextTick()
  renderCharts()
}

function renderCharts() {
  renderTrendChart()
  renderRiskChart()
}

function renderTrendChart() {
  if (!trendChartRef.value) {
    return
  }

  trendChart ||= echarts.init(trendChartRef.value)
  trendChart.setOption({
    tooltip: { trigger: 'axis' },
    legend: { top: 4, data: ['拦截次数', '脱敏次数', '高风险事件'] },
    grid: { left: 42, right: 24, top: 54, bottom: 34 },
    xAxis: { type: 'category', data: trendPoints.value.map((item) => item.label) },
    yAxis: { type: 'value' },
    series: [
      {
        name: '拦截次数',
        type: 'line',
        smooth: true,
        data: trendPoints.value.map((item) => item.blockCount)
      },
      {
        name: '脱敏次数',
        type: 'line',
        smooth: true,
        data: trendPoints.value.map((item) => item.maskCount)
      },
      {
        name: '高风险事件',
        type: 'line',
        smooth: true,
        data: trendPoints.value.map((item) => item.highRiskCount)
      }
    ]
  })
}

function renderRiskChart() {
  if (!riskChartRef.value) {
    return
  }

  riskChart ||= echarts.init(riskChartRef.value)
  const groups = ['Critical', 'High', 'Medium', 'Low', 'None'].map((name) => ({
    name,
    value: records.value.filter((item) => item.riskLevel === name).length
  }))

  riskChart.setOption({
    tooltip: { trigger: 'item' },
    legend: { right: 16, top: 24, orient: 'vertical' },
    series: [{ type: 'pie', radius: ['48%', '72%'], center: ['34%', '52%'], data: groups }]
  })
}

function resizeCharts() {
  trendChart?.resize()
  riskChart?.resize()
}

function clamp(value: number, min: number, max: number) {
  return Math.min(Math.max(value, min), max)
}

function formatRateAsPercent(value: number) {
  const percent = clamp(value, 0, 1) * 100
  return (Math.floor(percent * 100) / 100).toFixed(2)
}

onMounted(() => {
  loadDashboardData()
  window.addEventListener('resize', resizeCharts)
})

onUnmounted(() => {
  window.removeEventListener('resize', resizeCharts)
  trendChart?.dispose()
  riskChart?.dispose()
})
</script>

<style scoped>
.dashboard {
  display: flex;
  flex-direction: column;
  gap: 20px;
}

.metric-grid,
.dashboard-bottom {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 18px;
}

.metric-card {
  display: flex;
  align-items: center;
  justify-content: space-between;
  min-height: 132px;
  padding: 22px;
}

.metric-card span,
.metric-card small {
  color: var(--color-muted);
}

.metric-card strong {
  display: block;
  margin: 12px 0 8px;
  color: var(--color-primary);
  font-size: 32px;
}

.metric-card strong.danger {
  color: var(--color-danger);
}

.metric-card .el-icon {
  display: grid;
  place-items: center;
  width: 58px;
  height: 58px;
  border-radius: 8px;
  background: #eaf1ff;
  color: var(--color-primary);
  font-size: 30px;
}

.dashboard-main {
  display: grid;
  grid-template-columns: minmax(0, 1fr) minmax(520px, 1.35fr);
  gap: 18px;
}

.chart {
  height: 340px;
}

.small-chart {
  height: 240px;
}

.dashboard-bottom {
  grid-template-columns: 1fr 1fr 1.4fr;
}

.policy-list {
  display: flex;
  flex-direction: column;
  gap: 18px;
}

.policy-list div,
.health-body {
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.health-body {
  flex-wrap: wrap;
  gap: 18px 24px;
  min-height: 190px;
}

.score {
  display: flex;
  align-items: center;
  justify-content: center;
  flex-direction: column;
  width: 126px;
  height: 126px;
  border: 8px solid #dbeafe;
  border-radius: 50%;
  color: var(--color-success);
}

.score strong {
  font-size: 36px;
  font-weight: 800;
  line-height: 1;
}

.score span {
  margin-top: 8px;
  font-size: 12px;
  font-weight: 700;
}

dl {
  display: grid;
  grid-template-columns: auto auto;
  gap: 14px 34px;
  margin: 0;
}

dt {
  display: flex;
  align-items: center;
  gap: 6px;
  color: var(--color-muted);
}

dd {
  margin: 0;
  font-weight: 700;
}

.health-metrics dd .status-dot {
  display: none;
}

.health-metrics dt:nth-of-type(2)::after,
.health-metrics dt:nth-of-type(3)::after {
  content: "";
  width: 9px;
  height: 9px;
  border-radius: 50%;
}

.health-metrics dt:nth-of-type(2)::after {
  background: var(--color-danger);
}

.health-metrics dt:nth-of-type(3)::after {
  background: var(--color-success);
}

.health-status-bar {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 10px;
  width: 100%;
}

.health-status-bar span {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 7px;
  min-height: 34px;
  padding: 0 10px;
  border: 1px solid #e2e8f0;
  border-radius: 8px;
  background: #fff;
  color: #475569;
  font-size: 12px;
  font-weight: 600;
  white-space: nowrap;
}

.health-status-bar i {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  background: var(--color-success);
}

@media (max-width: 1200px) {
  .metric-grid,
  .dashboard-main,
  .dashboard-bottom {
    grid-template-columns: 1fr 1fr;
  }
}

@media (max-width: 760px) {
  .metric-grid,
  .dashboard-main,
  .dashboard-bottom {
    grid-template-columns: 1fr;
  }

  .health-status-bar {
    grid-template-columns: 1fr 1fr;
  }
}
</style>
