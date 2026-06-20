<template>
  <div class="rules-page">
    <div class="panel">
      <div class="panel-header">
        <div>
          <h2 class="page-title">安全规则管理</h2>
          <p class="page-subtitle">通过下拉选择和表单配置规则，后端会保存到数据库并绑定到当前 Agent。</p>
        </div>
        <div class="toolbar">
          <el-button :icon="Refresh" circle @click="loadRules" />
          <el-button @click="jsonDialogVisible = true">查看 JSON</el-button>
          <el-button type="primary" :icon="Plus" @click="openCreate">新增规则</el-button>
        </div>
      </div>

      <div class="panel-body">
        <div class="toolbar filter-bar">
          <el-select v-model="filters.ruleType" clearable placeholder="规则类型" style="width: 150px">
            <el-option v-for="item in options.ruleTypes" :key="item" :label="ruleTypeText(item)" :value="item" />
          </el-select>
          <el-select v-model="filters.matchType" clearable placeholder="匹配方式" style="width: 150px">
            <el-option v-for="item in options.matchTypes" :key="item" :label="matchTypeText(item)" :value="item" />
          </el-select>
          <el-select v-model="filters.riskLevel" clearable placeholder="风险等级" style="width: 150px">
            <el-option v-for="item in options.riskLevels" :key="item" :label="riskText(item)" :value="item" />
          </el-select>
          <el-switch v-model="onlyEnabled" active-text="仅看已启用" />
          <el-input v-model="keyword" clearable placeholder="搜索规则名称或编号" style="width: 240px" />
        </div>

        <el-table v-loading="loading" :data="filteredRules" height="460">
          <el-table-column prop="ruleId" label="规则编号" width="120" />
          <el-table-column prop="name" label="规则名称" min-width="180" show-overflow-tooltip />
          <el-table-column label="规则类型" width="120">
            <template #default="{ row }">{{ ruleTypeText(row.ruleType) }}</template>
          </el-table-column>
          <el-table-column label="匹配方式" width="120">
            <template #default="{ row }">{{ matchTypeText(row.matchType) }}</template>
          </el-table-column>
          <el-table-column label="风险等级" width="110">
            <template #default="{ row }">
              <el-tag :type="riskTag(row.riskLevel)">{{ riskText(row.riskLevel) }}</el-tag>
            </template>
          </el-table-column>
          <el-table-column label="处理动作" width="110">
            <template #default="{ row }">{{ actionText(row.action) }}</template>
          </el-table-column>
          <el-table-column label="启用状态" width="120">
            <template #default="{ row }">
              <el-switch :model-value="row.enabled" @change="(value: string | number | boolean) => toggleRule(row, Boolean(value))" />
            </template>
          </el-table-column>
          <el-table-column label="操作" width="180" fixed="right">
            <template #default="{ row }">
              <el-button link type="primary" @click="openEdit(row)">编辑</el-button>
              <el-button link type="primary" @click="testRule(row)">测试</el-button>
              <el-button link type="danger" @click="removeRule(row)">删除</el-button>
            </template>
          </el-table-column>
        </el-table>
      </div>
    </div>

    <div class="panel">
      <div class="panel-header">
        <h2 class="page-title">当前规则 JSON</h2>
      </div>
      <div class="panel-body">
        <pre class="json-box compact">{{ ruleJson }}</pre>
      </div>
    </div>

    <el-dialog v-model="jsonDialogVisible" title="当前规则 JSON" width="720px">
      <pre class="json-box compact">{{ ruleJson }}</pre>
    </el-dialog>

    <el-drawer v-model="drawerVisible" :title="editing ? '编辑规则' : '新增规则'" size="420px">
      <el-form label-position="top">
        <el-form-item label="规则编号">
          <el-input v-model="form.ruleId" placeholder="如：R-1001" />
        </el-form-item>
        <el-form-item label="规则名称">
          <el-input v-model="form.name" placeholder="如：提示词注入检测" />
        </el-form-item>
        <el-form-item label="规则类型">
          <el-select v-model="form.ruleType" style="width: 100%">
            <el-option v-for="item in options.ruleTypes" :key="item" :label="ruleTypeText(item)" :value="item" />
          </el-select>
        </el-form-item>
        <el-form-item label="匹配方式">
          <el-select v-model="form.matchType" style="width: 100%">
            <el-option v-for="item in options.matchTypes" :key="item" :label="matchTypeText(item)" :value="item" />
          </el-select>
        </el-form-item>
        <el-form-item label="匹配内容">
          <el-input v-model="form.pattern" type="textarea" :rows="5" maxlength="1000" show-word-limit />
        </el-form-item>
        <el-form-item label="风险等级">
          <el-select v-model="form.riskLevel" style="width: 100%">
            <el-option v-for="item in options.riskLevels" :key="item" :label="riskText(item)" :value="item" />
          </el-select>
        </el-form-item>
        <el-form-item label="处理动作">
          <el-select v-model="form.action" style="width: 100%">
            <el-option v-for="item in options.actions" :key="item" :label="actionText(item)" :value="item" />
          </el-select>
        </el-form-item>
        <el-form-item label="替换文本">
          <el-input v-model="form.replacement" placeholder="处理动作为 Mask 时生效" />
        </el-form-item>
        <el-form-item label="启用">
          <el-switch v-model="form.enabled" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="drawerVisible = false">取消</el-button>
        <el-button type="primary" :loading="saving" @click="saveRule">保存</el-button>
      </template>
    </el-drawer>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Plus, Refresh } from '@element-plus/icons-vue'
import { rulesApi } from '../api/services'
import { useAuthStore } from '../stores/auth'
import type { RuleOptionsResponse, SecurityRule, SecurityRuleSet } from '../api/types'

const auth = useAuthStore()
const loading = ref(false)
const saving = ref(false)
const drawerVisible = ref(false)
const jsonDialogVisible = ref(false)
const editing = ref(false)
const keyword = ref('')
const onlyEnabled = ref(false)
const ruleSet = ref<SecurityRuleSet>({
  inputRules: [],
  outputRules: [],
  toolPolicy: {
    dangerousTools: [],
    dangerousArgumentPatterns: [],
    appToolAllowList: {}
  }
})
const options = reactive<RuleOptionsResponse>({
  ruleTypes: [],
  matchTypes: [],
  riskLevels: [],
  actions: []
})
const filters = reactive({
  ruleType: '',
  matchType: '',
  riskLevel: ''
})
const form = reactive<SecurityRule>({
  ruleId: '',
  name: '',
  ruleType: 'Input',
  matchType: 'Regex',
  pattern: '',
  riskLevel: 'High',
  action: 'Block',
  replacement: '',
  enabled: true
})

const allRules = computed(() => [...ruleSet.value.inputRules, ...ruleSet.value.outputRules])
const filteredRules = computed(() => allRules.value.filter((rule) => {
  const hitKeyword = !keyword.value
    || rule.ruleId.toLowerCase().includes(keyword.value.toLowerCase())
    || rule.name.toLowerCase().includes(keyword.value.toLowerCase())

  return hitKeyword
    && (!filters.ruleType || rule.ruleType === filters.ruleType)
    && (!filters.matchType || rule.matchType === filters.matchType)
    && (!filters.riskLevel || rule.riskLevel === filters.riskLevel)
    && (!onlyEnabled.value || rule.enabled)
}))
const ruleJson = computed(() => JSON.stringify(ruleSet.value, null, 2))

async function loadRules() {
  loading.value = true
  try {
    const [rulesResponse, optionsResponse] = await Promise.all([
      rulesApi.getAll(auth.agentId),
      rulesApi.getOptions()
    ])
    ruleSet.value = rulesResponse.data
    Object.assign(options, optionsResponse.data)
  } finally {
    loading.value = false
  }
}

function openCreate() {
  editing.value = false
  Object.assign(form, {
    ruleId: '',
    name: '',
    ruleType: 'Input',
    matchType: 'Regex',
    pattern: '',
    riskLevel: 'High',
    action: 'Block',
    replacement: '',
    enabled: true
  })
  drawerVisible.value = true
}

function openEdit(rule: SecurityRule) {
  editing.value = true
  Object.assign(form, { ...rule })
  drawerVisible.value = true
}

async function saveRule() {
  saving.value = true
  try {
    if (editing.value) {
      await rulesApi.update(form.ruleId, { ...form })
    } else {
      await rulesApi.create({ ...form }, auth.agentId)
    }
    ElMessage.success('规则已保存')
    drawerVisible.value = false
    await loadRules()
  } finally {
    saving.value = false
  }
}

async function toggleRule(rule: SecurityRule, enabled: boolean) {
  await rulesApi.updateEnabled(rule.ruleId, enabled, auth.agentId)
  rule.enabled = enabled
  ElMessage.success(enabled ? '规则已启用' : '规则已禁用')
}

async function removeRule(rule: SecurityRule) {
  await ElMessageBox.confirm(`确认删除规则 ${rule.ruleId}？`, '删除规则', { type: 'warning' })
  await rulesApi.remove(rule.ruleId)
  ElMessage.success('规则已删除')
  await loadRules()
}

async function testRule(rule: SecurityRule) {
  try {
    const { value } = await ElMessageBox.prompt(
      `请输入用于测试规则 ${rule.ruleId} 的内容。`,
      '测试规则',
      {
        confirmButtonText: '开始测试',
        cancelButtonText: '取消',
        inputType: 'textarea',
        inputValue: buildDefaultTestContent(rule),
        inputValidator: (value) => Boolean(value?.trim()) || '测试内容不能为空'
      }
    )

    const { data } = await rulesApi.test({
      ruleId: rule.ruleId,
      testContent: value.trim()
    })

    ElMessageBox.alert(data.matchDetails, data.isMatch ? '规则已命中' : '规则未命中', {
      confirmButtonText: '知道了',
      type: data.isMatch ? 'warning' : 'success'
    })
  } catch (error) {
    if (error !== 'cancel' && error !== 'close') {
      throw error
    }
  }
}

function buildDefaultTestContent(rule: SecurityRule) {
  if (rule.matchType === 'Keyword') {
    return rule.pattern
  }

  return rule.pattern.replace(/[\\^$.*+?()[\]{}|]/g, '') || rule.pattern
}

function ruleTypeText(value: string) {
  return value === 'Input' ? '输入检测' : value === 'Output' ? '输出过滤' : value
}

function matchTypeText(value: string) {
  return value === 'Regex' ? '正则表达式' : value === 'Keyword' ? '关键词匹配' : value
}

function riskText(value: string) {
  const map: Record<string, string> = { None: '无风险', Low: '低危', Medium: '中危', High: '高危', Critical: '严重' }
  return map[value] || value
}

function actionText(value: string) {
  const map: Record<string, string> = { Allow: '放行', Warn: '告警', Block: '阻断', Mask: '替换', NeedApproval: '需审批' }
  return map[value] || value
}

function riskTag(level: string) {
  if (level === 'High' || level === 'Critical') return 'danger'
  if (level === 'Medium') return 'warning'
  if (level === 'Low') return 'primary'
  return 'info'
}

onMounted(loadRules)
</script>

<style scoped>
.rules-page {
  display: flex;
  flex-direction: column;
  gap: 20px;
}

.filter-bar {
  margin-bottom: 16px;
}

.compact {
  max-height: 280px;
  overflow: auto;
}
</style>
