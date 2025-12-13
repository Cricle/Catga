<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'

const router = useRouter()
const isSidebarMinimized = ref(false)

const menuItems = [
  { title: 'Dashboard', icon: 'dashboard', to: '/' },
  { title: 'Orders', icon: 'receipt_long', to: '/orders' },
  { title: 'Flow Demo', icon: 'account_tree', to: '/flow' },
  { title: 'Events & CQRS', icon: 'bolt', to: '/events' },
  { title: 'Settings', icon: 'settings', to: '/settings' },
]
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
            <va-icon name="shopping_cart" color="primary" />
            <va-sidebar-item-title v-if="!isSidebarMinimized">
              OrderSystem
            </va-sidebar-item-title>
          </va-sidebar-item-content>
        </va-sidebar-item>

        <va-sidebar-item
          v-for="item in menuItems"
          :key="item.to"
          :active="$route.path === item.to"
          @click="router.push(item.to)"
        >
          <va-sidebar-item-content>
            <va-icon :name="item.icon" />
            <va-sidebar-item-title v-if="!isSidebarMinimized">
              {{ item.title }}
            </va-sidebar-item-title>
          </va-sidebar-item-content>
        </va-sidebar-item>
      </va-sidebar>
    </template>

    <!-- Content -->
    <template #content>
      <va-navbar color="backgroundPrimary" class="app-navbar">
        <template #left>
          <va-button
            preset="secondary"
            @click="isSidebarMinimized = !isSidebarMinimized"
          >
            <va-icon name="menu" />
          </va-button>
        </template>
        <template #center>
          <span class="navbar-title">Catga OrderSystem Demo</span>
        </template>
        <template #right>
          <va-button preset="secondary" href="https://github.com/Cricle/Catga" target="_blank">
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
