<script setup lang="ts">
import { ref, computed } from 'vue'
import { useRouter, useRoute } from 'vue-router'
import { useCartStore } from './stores/cart'

const router = useRouter()
const route = useRoute()
const cart = useCartStore()

const isAdminMode = computed(() => route.path.startsWith('/admin'))

const shopMenuItems = [
  { title: '商品', icon: 'store', to: '/' },
  { title: '购物车', icon: 'shopping_cart', to: '/cart' },
  { title: '我的订单', icon: 'receipt_long', to: '/my-orders' },
]

const adminMenuItems = [
  { title: '仪表盘', icon: 'dashboard', to: '/admin' },
  { title: '订单管理', icon: 'list_alt', to: '/admin/orders' },
  { title: '可观测性', icon: 'monitoring', to: '/admin/observability' },
  { title: '热重载', icon: 'autorenew', to: '/admin/hotreload' },
  { title: '读模型同步', icon: 'sync', to: '/admin/readmodelsync' },
  { title: '系统设置', icon: 'settings', to: '/admin/settings' },
]

const currentMenuItems = computed(() => isAdminMode.value ? adminMenuItems : shopMenuItems)
const isSidebarVisible = ref(true)
</script>

<template>
  <va-app-layout>
    <template #top>
      <va-navbar :color="isAdminMode ? 'primary' : 'background-secondary'">
        <template #left>
          <va-button preset="secondary" @click="isSidebarVisible = !isSidebarVisible">
            <va-icon name="menu" />
          </va-button>
          <va-navbar-item class="ml-3">
            <strong>{{ isAdminMode ? 'OrderSystem Admin' : 'OrderSystem' }}</strong>
          </va-navbar-item>
        </template>
        <template #right>
          <va-button v-if="!isAdminMode" preset="secondary" @click="router.push('/cart')" class="mr-2">
            <va-icon name="shopping_cart" />
            <va-badge v-if="cart.count > 0" :text="String(cart.count)" color="danger" overlap />
          </va-button>
          <va-button preset="secondary" @click="router.push(isAdminMode ? '/' : '/admin')" class="mr-2">
            <va-icon :name="isAdminMode ? 'store' : 'admin_panel_settings'" class="mr-1" />
            {{ isAdminMode ? '商城' : '管理' }}
          </va-button>
          <va-button preset="secondary" href="https://github.com/Cricle/Catga" target="_blank">
            <va-icon name="code" />
          </va-button>
        </template>
      </va-navbar>
    </template>

    <template #left>
      <va-sidebar v-model="isSidebarVisible" minimized-width="0">
        <va-sidebar-item
          v-for="item in currentMenuItems"
          :key="item.to"
          :active="route.path === item.to"
          @click="router.push(item.to)"
        >
          <va-sidebar-item-content>
            <va-icon :name="item.icon" />
            <va-sidebar-item-title>{{ item.title }}</va-sidebar-item-title>
          </va-sidebar-item-content>
        </va-sidebar-item>

        <va-divider />

        <va-sidebar-item @click="router.push(isAdminMode ? '/' : '/admin')">
          <va-sidebar-item-content>
            <va-icon :name="isAdminMode ? 'store' : 'admin_panel_settings'" />
            <va-sidebar-item-title>{{ isAdminMode ? '返回商城' : '管理后台' }}</va-sidebar-item-title>
          </va-sidebar-item-content>
        </va-sidebar-item>
      </va-sidebar>
    </template>

    <template #content>
      <va-scroll-container vertical class="app-content">
        <router-view />
      </va-scroll-container>
    </template>
  </va-app-layout>
</template>

<style>
html, body, #app {
  height: 100%;
  margin: 0;
  padding: 0;
}

.app-content {
  padding: 1.5rem;
  background: var(--va-background-secondary);
  min-height: 100%;
}

.mr-1 { margin-right: 0.25rem; }
.mr-2 { margin-right: 0.5rem; }
.ml-3 { margin-left: 0.75rem; }
</style>
