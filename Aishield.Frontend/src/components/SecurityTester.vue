<template>
  <div class="panel">
    <div class="panel-header">
      <div>
        <h2 class="page-title">{{ title }}</h2>
        <p class="page-subtitle">{{ subtitle }}</p>
      </div>
      <el-button type="primary" :loading="loading" @click="submit">开始检测</el-button>
    </div>
    <div class="panel-body tester-grid">
      <el-form label-position="top">
        <el-form-item label="匿名主体哈希（可选）">
          <el-input v-model="form.subjectHash" placeholder="如：user_hash_001" />
        </el-form-item>
        <el-form-item label="待检测内容">
          <el-input v-model="form.content" type="textarea" :rows="12" :placeholder="placeholder" show-word-limit maxlength="2000" />
        </el-form-item>
      </el-form>

      <div class="result-area">
        <div class="result-card">
          <el-icon :class="result?.allowed ? 'ok' : 'danger'"><CircleCheckFilled /></el-icon>
          <div>
            <strong>{{ result ? (result.allowed ? '允许通过' : '检测到风险') : '等待检测' }}</strong>
            <span>{{ result?.reason || '提交内容后展示风险等级、处理动作和命中规则' }}</span>
          </div>
        </div>
        <pre class="json-box">{{ resultText }}</pre>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, reactive, ref } from 'vue'
import { CircleCheckFilled } from '@element-plus/icons-vue'
import { securityApi } from '../api/services'
import type { SecurityCheckResponse } from '../api/types'

const props = defineProps<{
  title: string
  subtitle: string
  mode: 'input' | 'output'
  placeholder: string
}>()

const loading = ref(false)
const result = ref<SecurityCheckResponse | null>(null)
const form = reactive({
  subjectHash: '',
  content: ''
})

const resultText = computed(() => (result.value ? JSON.stringify(result.value, null, 2) : '等待检测'))

async function submit() {
  loading.value = true
  try {
    const api = props.mode === 'input' ? securityApi.checkInput : securityApi.checkOutput
    const { data } = await api({
      subjectHash: form.subjectHash || undefined,
      content: form.content
    })
    result.value = data
  } finally {
    loading.value = false
  }
}
</script>

<style scoped>
.tester-grid {
  display: grid;
  grid-template-columns: minmax(420px, 1fr) minmax(420px, 1fr);
  gap: 24px;
}

.result-area {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.result-card {
  display: flex;
  align-items: center;
  gap: 16px;
  min-height: 96px;
  padding: 18px;
  border: 1px solid var(--color-border);
  border-radius: 8px;
  background: #fff;
}

.result-card .el-icon {
  font-size: 34px;
}

.result-card .ok {
  color: var(--color-success);
}

.result-card .danger {
  color: var(--color-danger);
}

.result-card strong,
.result-card span {
  display: block;
}

.result-card span {
  margin-top: 6px;
  color: var(--color-muted);
}

@media (max-width: 980px) {
  .tester-grid {
    grid-template-columns: 1fr;
  }
}
</style>
