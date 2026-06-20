<template>
  <div class="panel">
    <div class="panel-header">
      <div>
        <h2 class="page-title">工具调用防护</h2>
        <p class="page-subtitle">检查 Agent 调用外部工具时是否存在危险工具名或高风险参数。</p>
      </div>
      <el-button type="primary" :loading="loading" @click="submit">开始检测</el-button>
    </div>
    <div class="panel-body tool-grid">
      <el-form label-position="top">
        <el-form-item label="匿名主体哈希（可选）">
          <el-input v-model="form.subjectHash" placeholder="如：user_hash_001" />
        </el-form-item>
        <el-form-item label="工具名称">
          <el-input v-model="form.toolName" placeholder="例如 rm_file、read_database" />
        </el-form-item>
        <el-form-item label="调用参数 JSON">
          <el-input v-model="argumentsText" type="textarea" :rows="10" />
        </el-form-item>
      </el-form>

      <div>
        <h3>检测结果</h3>
        <pre class="json-box">{{ resultText }}</pre>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, reactive, ref } from 'vue'
import { ElMessage } from 'element-plus'
import { securityApi } from '../api/services'
import type { SecurityCheckResponse } from '../api/types'

const loading = ref(false)
const result = ref<SecurityCheckResponse | null>(null)
const argumentsText = ref(JSON.stringify({ path: '/etc/passwd' }, null, 2))
const form = reactive({
  subjectHash: '',
  toolName: 'read_file'
})

const resultText = computed(() => (result.value ? JSON.stringify(result.value, null, 2) : '等待检测'))

async function submit() {
  let args: Record<string, unknown>
  try {
    args = JSON.parse(argumentsText.value)
  } catch {
    ElMessage.warning('调用参数必须是合法 JSON')
    return
  }

  loading.value = true
  try {
    const { data } = await securityApi.checkToolCall({
      subjectHash: form.subjectHash || undefined,
      toolName: form.toolName,
      arguments: args
    })
    result.value = data
  } finally {
    loading.value = false
  }
}
</script>

<style scoped>
.tool-grid {
  display: grid;
  grid-template-columns: minmax(360px, 0.9fr) minmax(420px, 1.1fr);
  gap: 24px;
}

h3 {
  margin: 0 0 12px;
}

@media (max-width: 980px) {
  .tool-grid {
    grid-template-columns: 1fr;
  }
}
</style>
