import { createRouter, createWebHistory } from 'vue-router'
import { useAuthStore } from '../stores/auth'
import MainLayout from '../layouts/MainLayout.vue'

const router = createRouter({
  history: createWebHistory(),
  routes: [
    {
      path: '/login',
      name: 'login',
      component: () => import('../views/LoginView.vue')
    },
    {
      path: '/register',
      name: 'register',
      component: () => import('../views/RegisterView.vue')
    },
    {
      path: '/',
      component: MainLayout,
      redirect: '/dashboard',
      children: [
        { path: 'dashboard', name: 'dashboard', component: () => import('../views/DashboardView.vue') },
        { path: 'agent', name: 'agent', component: () => import('../views/AgentAccessView.vue') },
        { path: 'input-check', name: 'input-check', component: () => import('../views/InputCheckView.vue') },
        { path: 'output-filter', name: 'output-filter', component: () => import('../views/OutputFilterView.vue') },
        { path: 'tool-guard', name: 'tool-guard', component: () => import('../views/ToolGuardView.vue') },
        { path: 'rules', name: 'rules', component: () => import('../views/RulesView.vue') },
        { path: 'memory', name: 'memory', component: () => import('../views/MemoryManagementView.vue') },
        { path: 'audit', name: 'audit', component: () => import('../views/AuditLogView.vue') },
        { path: 'guide', name: 'guide', component: () => import('../views/GuideView.vue') }
      ]
    }
  ]
})

router.beforeEach((to) => {
  const auth = useAuthStore()

  if (!auth.isLoggedIn && to.name !== 'login') {
    return { name: 'login' }
  }

  if (auth.isLoggedIn && to.name === 'login') {
    return { name: 'dashboard' }
  }

  return true
})

export default router
