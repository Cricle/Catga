<script setup lang="ts">
import { computed } from 'vue'
import { useRouter, useRoute } from 'vue-router'
import { useCartStore } from './stores/cart'

const router = useRouter()
const route = useRoute()
const cart = useCartStore()

const isAdminMode = computed(() => route.path.startsWith('/admin'))
const isShopPage = computed(() => !isAdminMode.value)
</script>

<template>
  <div class="app-container">
    <!-- Top Navigation Bar -->
    <header class="top-header" :class="{ 'admin-header': isAdminMode }">
      <div class="header-content">
        <div class="header-left">
          <div class="brand" @click="router.push(isAdminMode ? '/admin' : '/')">
            <va-icon :name="isAdminMode ? 'admin_panel_settings' : 'storefront'" size="1.5rem" />
            <span class="brand-name">{{ isAdminMode ? 'OrderSystem Admin' : 'OrderSystem' }}</span>
          </div>
        </div>

        <nav class="header-nav" v-if="isShopPage">
          <a class="nav-link" :class="{ active: route.path === '/' }" @click="router.push('/')">
            <va-icon name="store" class="mr-1" /> 商品
          </a>
          <a class="nav-link" :class="{ active: route.path === '/my-orders' }" @click="router.push('/my-orders')">
            <va-icon name="receipt_long" class="mr-1" /> 我的订单
          </a>
        </nav>

        <nav class="header-nav" v-else>
          <a class="nav-link" :class="{ active: route.path === '/admin' }" @click="router.push('/admin')">
            <va-icon name="dashboard" class="mr-1" /> 仪表盘
          </a>
          <a class="nav-link" :class="{ active: route.path === '/admin/orders' }" @click="router.push('/admin/orders')">
            <va-icon name="list_alt" class="mr-1" /> 订单
          </a>
          <a class="nav-link" :class="{ active: route.path === '/admin/observability' }" @click="router.push('/admin/observability')">
            <va-icon name="monitoring" class="mr-1" /> 可观测性
          </a>
          <a class="nav-link" :class="{ active: route.path === '/admin/hotreload' }" @click="router.push('/admin/hotreload')">
            <va-icon name="autorenew" class="mr-1" /> 热重载
          </a>
          <a class="nav-link" :class="{ active: route.path === '/admin/readmodelsync' }" @click="router.push('/admin/readmodelsync')">
            <va-icon name="sync" class="mr-1" /> 读模型
          </a>
        </nav>

        <div class="header-right">
          <va-button v-if="isShopPage" preset="secondary" class="cart-btn" @click="router.push('/cart')">
            <va-icon name="shopping_cart" />
            <va-badge v-if="cart.count > 0" :text="String(cart.count)" color="danger" class="cart-badge" />
          </va-button>
          <va-button preset="secondary" @click="router.push(isAdminMode ? '/' : '/admin')">
            <va-icon :name="isAdminMode ? 'store' : 'admin_panel_settings'" class="mr-1" />
            {{ isAdminMode ? '商城' : '管理' }}
          </va-button>
          <va-button preset="secondary" href="https://github.com/Cricle/Catga" target="_blank">
            <va-icon name="code" />
          </va-button>
        </div>
      </div>
    </header>

    <!-- Main Content -->
    <main class="main-content">
      <router-view />
    </main>

    <!-- Footer -->
    <footer class="app-footer">
      <p>Powered by <strong>Catga CQRS Framework</strong> | <a href="https://github.com/Cricle/Catga" target="_blank">GitHub</a></p>
    </footer>
  </div>
</template>

<style>
* {
  margin: 0;
  padding: 0;
  box-sizing: border-box;
}

.app-container {
  min-height: 100vh;
  display: flex;
  flex-direction: column;
  background: #f5f5f5;
}

.top-header {
  background: #fff;
  box-shadow: 0 2px 8px rgba(0,0,0,0.1);
  position: sticky;
  top: 0;
  z-index: 100;
}

.top-header.admin-header {
  background: linear-gradient(135deg, #512da8 0%, #673ab7 100%);
}

.top-header.admin-header .brand-name,
.top-header.admin-header .nav-link,
.top-header.admin-header .va-button {
  color: #fff !important;
}

.header-content {
  max-width: 1400px;
  margin: 0 auto;
  padding: 0 1.5rem;
  height: 64px;
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.header-left {
  display: flex;
  align-items: center;
}

.brand {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  cursor: pointer;
  color: #512da8;
}

.admin-header .brand {
  color: #fff;
}

.brand-name {
  font-size: 1.25rem;
  font-weight: 700;
}

.header-nav {
  display: flex;
  gap: 0.5rem;
}

.nav-link {
  display: flex;
  align-items: center;
  padding: 0.5rem 1rem;
  border-radius: 8px;
  cursor: pointer;
  color: #666;
  font-weight: 500;
  transition: all 0.2s;
  text-decoration: none;
}

.nav-link:hover {
  background: rgba(103, 58, 183, 0.1);
  color: #512da8;
}

.nav-link.active {
  background: rgba(103, 58, 183, 0.15);
  color: #512da8;
}

.admin-header .nav-link:hover,
.admin-header .nav-link.active {
  background: rgba(255,255,255,0.2);
  color: #fff;
}

.header-right {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.cart-btn {
  position: relative;
}

.cart-badge {
  position: absolute;
  top: -4px;
  right: -4px;
}

.main-content {
  flex: 1;
  max-width: 1400px;
  margin: 0 auto;
  padding: 2rem 1.5rem;
  width: 100%;
}

.app-footer {
  background: #fff;
  border-top: 1px solid #e0e0e0;
  padding: 1rem;
  text-align: center;
  color: #666;
  font-size: 0.875rem;
}

.app-footer a {
  color: #512da8;
  text-decoration: none;
}

.mr-1 {
  margin-right: 0.25rem;
}

.mr-2 {
  margin-right: 0.5rem;
}

@media (max-width: 768px) {
  .header-nav {
    display: none;
  }

  .brand-name {
    font-size: 1rem;
  }
}
</style>
