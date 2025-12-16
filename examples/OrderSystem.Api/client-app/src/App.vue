<script setup lang="ts">
import { ref, computed } from 'vue'
import { useRouter, useRoute } from 'vue-router'

const router = useRouter()
const route = useRoute()
const isSidebarMinimized = ref(false)

// Determine if we're in admin or shop mode based on route
const isAdminMode = computed(() => route.path.startsWith('/admin'))

// Shop menu items (user facing)
const shopMenuItems = [
  { title: '商城首页', icon: 'store', to: '/' },
  { title: '我的订单', icon: 'receipt_long', to: '/my-orders' },
  { title: '购物车', icon: 'shopping_cart', to: '/cart' },
]

// Admin menu items
const adminMenuItems = [
  { title: '仪表盘', icon: 'dashboard', to: '/admin' },
  { title: '订单管理', icon: 'list_alt', to: '/admin/orders' },
  { title: '可观测性', icon: 'monitoring', to: '/admin/observability' },
  { title: '热重载', icon: 'autorenew', to: '/admin/hotreload' },
  { title: '读模型同步', icon: 'sync', to: '/admin/readmodelsync' },
  { title: '系统设置', icon: 'settings', to: '/admin/settings' },
]

const currentMenuItems = computed(() => isAdminMode.value ? adminMenuItems : shopMenuItems)

const switchMode = () => {
  if (isAdminMode.value) {
    router.push('/')
  } else {
    router.push('/admin')
  }
}
</script>

<template>
  <va-layout class="app-layout">
    <!-- Sidebar -->
    <template #left>
      <va-sidebar
        v-model="isSidebarMinimized"
        :width="isSidebarMinimized ? '64px' : '240px'"
        minimized-width="64px"
      >
        <va-sidebar-item>
          <va-sidebar-item-content>
            <va-icon :name="isAdminMode ? 'admin_panel_settings' : 'storefront'" color="primary" />
            <va-sidebar-item-title v-if="!isSidebarMinimized">
              {{ isAdminMode ? '管理后台' : '商城' }}
            </va-sidebar-item-title>
          </va-sidebar-item-content>
        </va-sidebar-item>

        <va-sidebar-item
          v-for="item in currentMenuItems"
          :key="item.to"
          :active="route.path === item.to"
          @click="router.push(item.to)"
        >
          <va-sidebar-item-content>
            <va-icon :name="item.icon" />
            <va-sidebar-item-title v-if="!isSidebarMinimized">
              {{ item.title }}
            </va-sidebar-item-title>
          </va-sidebar-item-content>
        </va-sidebar-item>

        <va-divider />

        <va-sidebar-item @click="switchMode">
          <va-sidebar-item-content>
            <va-icon :name="isAdminMode ? 'store' : 'admin_panel_settings'" />
            <va-sidebar-item-title v-if="!isSidebarMinimized">
              {{ isAdminMode ? '返回商城' : '管理后台' }}
            </va-sidebar-item-title>
          </va-sidebar-item-content>
        </va-sidebar-item>
      </va-sidebar>
    </template>

    <!-- Content -->
    <template #content>
      <va-navbar :color="isAdminMode ? 'primary' : 'backgroundPrimary'" class="app-navbar">
        <template #left>
          <va-button
            preset="secondary"
            :color="isAdminMode ? 'backgroundPrimary' : undefined"
            @click="isSidebarMinimized = !isSidebarMinimized"
          >
            <va-icon name="menu" />
          </va-button>
        </template>
        <template #center>
          <span class="navbar-title" :class="{ 'text-white': isAdminMode }">
            {{ isAdminMode ? 'OrderSystem 管理后台' : 'OrderSystem 商城' }}
          </span>
        </template>
        <template #right>
          <va-button
            preset="secondary"
            :color="isAdminMode ? 'backgroundPrimary' : undefined"
            href="https://github.com/Cricle/Catga"
            target="_blank"
          >
            <va-icon name="code" class="mr-2" />
            GitHub
          </va-button>
        </template>
      </va-navbar>

      <main class="app-main">
        <router-view />
      </main>
    </template>
  </va-layout>
</template>

<style>
.app-layout {
  height: 100vh;
}

.app-navbar {
  padding: 0 1rem;
}

.navbar-title {
  font-weight: 600;
  font-size: 1.125rem;
}

.text-white {
  color: white;
}

.app-main {
  padding: 1.5rem;
  background: #f8fafc;
  min-height: calc(100vh - 64px);
  overflow-y: auto;
}

.mr-2 {
  margin-right: 0.5rem;
}
</style>
