<template>
  <div class="layout" :class="{ 'sidebar-collapsed': isSidebarCollapsed }">
    <aside class="sidebar">
      <div class="brand">
        <img :src="logoUrl" alt="AIShield" />
        <span>AIShield</span>
      </div>

      <nav class="menu">
        <RouterLink v-for="item in menuItems" :key="item.path" :to="item.path" class="menu-item">
          <el-icon><component :is="item.icon" /></el-icon>
          <span>{{ item.label }}</span>
        </RouterLink>
      </nav>

      <button
        class="sidebar-toggle"
        type="button"
        :title="isSidebarCollapsed ? '展开导航栏' : '收起导航栏'"
        :aria-label="isSidebarCollapsed ? '展开导航栏' : '收起导航栏'"
        @click="isSidebarCollapsed = !isSidebarCollapsed"
      >
        <el-icon>
          <ArrowRight v-if="isSidebarCollapsed" />
          <ArrowLeft v-else />
        </el-icon>
      </button>

      <div class="sidebar-status">
        <div><span class="status-dot"></span>系统运行正常</div>
        <small>版本 v1.3.2</small>
      </div>
    </aside>

    <main class="main">
      <header class="topbar">
        <div class="topbar-left">
          <el-icon class="hamburger"><Menu /></el-icon>
          <h1>{{ currentTitle }}</h1>
        </div>

      </header>

      <section class="content">
        <RouterView />
      </section>
    </main>
  </div>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRoute } from 'vue-router'
import {
  ArrowLeft,
  ArrowRight,
  Coin,
  Document,
  DocumentChecked,
  Filter,
  House,
  Key,
  List,
  Lock,
  Menu,
  Operation
} from '@element-plus/icons-vue'
import logoUrl from '../../picture/logo.png'

const route = useRoute()
const isSidebarCollapsed = ref(false)

const menuItems = [
  { path: '/dashboard', label: '总览', icon: House },
  { path: '/agent', label: 'Agent 管理', icon: Key },
  { path: '/input-check', label: '输入检测', icon: Lock },
  { path: '/output-filter', label: '输出过滤', icon: Filter },
  { path: '/tool-guard', label: '工具调用防护', icon: Operation },
  { path: '/rules', label: '规则管理', icon: List },
  { path: '/audit', label: '审计日志', icon: DocumentChecked },
  { path: '/guide', label: '使用说明', icon: Document }
]

menuItems.splice(6, 0, { path: '/memory', label: '记忆管理', icon: Coin })

const currentTitle = computed(() => menuItems.find((item) => item.path === route.path)?.label || 'AIShield')
</script>

<style scoped>
.layout {
  display: flex;
  min-height: 100vh;
}

.sidebar {
  position: sticky;
  top: 0;
  flex: 0 0 256px;
  display: flex;
  flex-direction: column;
  width: 256px;
  height: 100vh;
  padding: 22px 12px;
  background: var(--color-sidebar);
  color: #dbeafe;
  transition: width 0.22s ease, flex-basis 0.22s ease, padding 0.22s ease;
  z-index: 10;
}

.brand {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 4px 14px 28px;
  color: #fff;
  font-size: 24px;
  font-weight: 800;
}

.brand img {
  width: 34px;
  height: 34px;
  object-fit: contain;
}

.menu {
  display: flex;
  flex: 1;
  flex-direction: column;
  gap: 8px;
}

.menu-item {
  display: flex;
  align-items: center;
  gap: 12px;
  min-height: 48px;
  padding: 0 16px;
  border-radius: 8px;
  color: #b8c4d8;
  font-size: 15px;
}

.menu-item.router-link-active {
  background: var(--color-primary);
  color: #fff;
}

.sidebar-status {
  display: none;
  padding: 16px;
  border: 1px solid rgba(255, 255, 255, 0.08);
  border-radius: 8px;
  background: rgba(255, 255, 255, 0.04);
  color: #e2e8f0;
}

.sidebar-status small {
  display: block;
  margin-top: 8px;
  color: #94a3b8;
}

.sidebar-toggle {
  position: absolute;
  top: 50%;
  right: -18px;
  display: grid;
  place-items: center;
  width: 32px;
  height: 52px;
  padding: 0;
  border: 1px solid rgba(148, 163, 184, 0.35);
  border-left: 0;
  border-radius: 0 8px 8px 0;
  background: var(--color-sidebar);
  color: #dbeafe;
  cursor: pointer;
  transform: translateY(-50%);
}

.sidebar-collapsed .sidebar {
  position: fixed;
  left: 0;
  flex-basis: 0;
  width: 44px;
  padding-right: 0;
  padding-left: 0;
  background: transparent;
}

.sidebar-collapsed .brand,
.sidebar-collapsed .menu {
  opacity: 0;
  pointer-events: none;
  visibility: hidden;
}

.sidebar-collapsed .sidebar-toggle {
  right: auto;
  left: 8px;
  width: 28px;
  height: 48px;
  border-left: 1px solid rgba(148, 163, 184, 0.35);
  border-radius: 0 8px 8px 0;
  box-shadow: 0 8px 20px rgba(15, 23, 42, 0.16);
}

.sidebar-collapsed .main {
  flex-basis: 100%;
}

.sidebar-collapsed .content {
  padding-left: 56px;
}

.main {
  flex: 1;
  min-width: 0;
}

.topbar {
  display: flex;
  align-items: center;
  justify-content: flex-start;
  height: 88px;
  padding: 0 28px;
  border-bottom: 1px solid var(--color-border);
  background: #fff;
}

.topbar-left {
  display: flex;
  align-items: center;
  gap: 18px;
}

.topbar-left h1 {
  margin: 0;
  font-size: 28px;
}

.hamburger {
  color: #334155;
  font-size: 22px;
}

.content {
  padding: 24px 28px 32px;
}

@media (max-width: 980px) {
  .sidebar {
    flex-basis: 88px;
    width: 88px;
  }

  .brand span,
  .menu-item span {
    display: none;
  }

  .menu-item {
    justify-content: center;
    padding: 0;
  }

}
</style>
