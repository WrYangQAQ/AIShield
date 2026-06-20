<template>
  <div class="auth-page">
    <div class="auth-card">
      <div class="auth-brand">
        <img :src="logoUrl" alt="AIShield" />
        <div>
          <h1>AIShield</h1>
          <p>登录本地管理端</p>
        </div>
      </div>

      <el-form label-position="top" :model="form" @submit.prevent>
        <el-form-item label="管理员密码">
          <el-input v-model="form.password" type="password" show-password placeholder="请输入本地管理员密码" />
        </el-form-item>
        <el-button type="primary" class="auth-submit" :loading="loading" @click="submit">登录</el-button>
      </el-form>

      <div class="auth-link">
        默认密码可在后端配置项 Admin:Password 中修改
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { reactive, ref } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { agentApi, authApi } from '../api/services'
import { useAuthStore } from '../stores/auth'
import logoUrl from '../../picture/logo.png'

const router = useRouter()
const auth = useAuthStore()
const loading = ref(false)
const form = reactive({
  password: ''
})

async function submit() {
  loading.value = true

  try {
    const { data } = await authApi.login({ password: form.password })

    if (!data.success || !data.token) {
      ElMessage.error(data.message || '登录失败')
      return
    }

    auth.setSession({
      token: data.token,
      tokenExpiresAt: data.tokenExpiresAt
    })

    const agents = await agentApi.list()
    auth.setActiveAgent(agents.data[0] ?? null)

    ElMessage.success(data.message || '登录成功')
    router.push(agents.data.length > 0 ? '/dashboard' : '/register')
  } finally {
    loading.value = false
  }
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

.auth-brand p {
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
</style>
