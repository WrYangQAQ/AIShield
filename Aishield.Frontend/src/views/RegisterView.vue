<template>
  <div class="auth-page">
    <div class="auth-card wide">
      <div class="auth-brand">
        <img :src="logoUrl" alt="AIShield" />
        <div>
          <h1>注册 Agent</h1>
          <p>生成 Agent Key 后即可接入 AIShield</p>
        </div>
      </div>

      <el-form label-position="top" :model="form">
        <el-form-item label="Agent 名称">
          <el-input v-model="form.agentName" placeholder="如：客服问答 Agent" />
        </el-form-item>
        <el-form-item label="应用场景">
          <el-input v-model="form.scenario" placeholder="如：个人博客智能问答" />
        </el-form-item>
        <el-button type="primary" class="auth-submit" :loading="loading" @click="submit">
          注册并生成 Agent Key
        </el-button>
      </el-form>

      <div class="auth-link">
        已有 Agent？
        <RouterLink to="/dashboard">返回总览</RouterLink>
      </div>
    </div>

    <el-dialog v-model="keyDialogVisible" title="请保存 Agent Key" width="620px" :close-on-click-modal="false">
      <p class="notice">Agent Key 只在注册成功后展示一次，后端仅保存哈希值。</p>
      <div class="key-box">{{ generatedKey }}</div>
      <template #footer>
        <el-button @click="copyKey">复制 Agent Key</el-button>
        <el-button type="primary" @click="goDashboard">进入总览</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { reactive, ref } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { agentApi } from '../api/services'
import { useAuthStore } from '../stores/auth'
import logoUrl from '../../picture/logo.png'

const router = useRouter()
const auth = useAuthStore()
const loading = ref(false)
const keyDialogVisible = ref(false)
const generatedKey = ref('')
const form = reactive({
  agentName: '',
  scenario: ''
})

async function submit() {
  loading.value = true

  try {
    const { data } = await agentApi.register(form)
    generatedKey.value = data.agentKey
    auth.setActiveAgent({
      agentId: data.agentId,
      agentName: data.agentName,
      scenario: form.scenario,
      agentKeyPreview: data.agentKey.replace(/^ak_(.{4}).*(.{3})$/, 'ak_$1**********$2'),
      enabled: true,
      createdAt: new Date().toISOString(),
      lastUsedAt: null
    })
    keyDialogVisible.value = true
    ElMessage.success(data.message || '注册成功')
  } finally {
    loading.value = false
  }
}

async function copyKey() {
  await navigator.clipboard.writeText(generatedKey.value)
  ElMessage.success('已复制 Agent Key')
}

function goDashboard() {
  router.push('/dashboard')
}
</script>

<style scoped>
.auth-page {
  display: grid;
  min-height: 100vh;
  place-items: center;
  background:
    radial-gradient(circle at 20% 20%, rgba(0, 80, 228, 0.14), transparent 28%),
    linear-gradient(135deg, #f8fbff 0%, #eef4ff 100%);
}

.auth-card {
  width: min(420px, calc(100vw - 32px));
  padding: 34px;
  border: 1px solid var(--color-border);
  border-radius: 8px;
  background: #fff;
  box-shadow: var(--shadow-card);
}

.auth-card.wide {
  width: min(620px, calc(100vw - 32px));
}

.auth-brand {
  display: flex;
  align-items: center;
  gap: 14px;
  margin-bottom: 28px;
}

.auth-brand img {
  width: 42px;
  height: 42px;
}

.auth-brand h1 {
  margin: 0;
  font-size: 28px;
}

.auth-brand p,
.notice {
  margin: 4px 0 0;
  color: var(--color-muted);
}

.auth-submit {
  width: 100%;
  height: 42px;
}

.auth-link {
  margin-top: 18px;
  color: var(--color-muted);
  text-align: center;
}

.auth-link a {
  color: var(--color-primary);
}

.key-box {
  margin-top: 14px;
  padding: 16px;
  border-radius: 8px;
  background: #f1f5ff;
  color: var(--color-primary);
  font-family: Consolas, monospace;
  word-break: break-all;
}
</style>
