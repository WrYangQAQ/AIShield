import axios from 'axios'
import { ElMessage } from 'element-plus'
import { useAuthStore } from '../stores/auth'

export const http = axios.create({
  baseURL: 'http://localhost:5069',
  timeout: 12000
})

http.interceptors.request.use((config) => {
  const auth = useAuthStore()

  if (auth.token) {
    config.headers.Authorization = `Bearer ${auth.token}`
  }

  if (auth.agentId) {
    config.headers['X-Agent-Id'] = String(auth.agentId)
  }

  return config
})

http.interceptors.response.use(
  (response) => response,
  (error) => {
    const message = error.response?.data?.message || error.response?.data?.Message || error.message || '请求失败'
    ElMessage.error(message)
    return Promise.reject(error)
  }
)
