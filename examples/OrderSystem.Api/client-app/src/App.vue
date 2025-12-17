<script setup lang="ts">
import { ref } from 'vue'
import { useRouter, useRoute } from 'vue-router'
import { useCartStore } from './stores/cart'

const router = useRouter()
const route = useRoute()
const cart = useCartStore()
const showSidebar = ref(true)
</script>

<template>
  <div class="layout">
    <va-navbar color="primary">
      <template #left>
        <va-button preset="secondary" color="textInverted" @click="showSidebar = !showSidebar">
          <va-icon name="menu" />
        </va-button>
        <span class="title">OrderSystem</span>
      </template>
      <template #right>
        <va-button preset="secondary" color="textInverted" @click="router.push('/cart')">
          <va-icon name="shopping_cart" />
          <va-badge v-if="cart.count > 0" :text="String(cart.count)" color="danger" />
        </va-button>
        <va-button preset="secondary" color="textInverted" @click="router.push('/orders')">
          <va-icon name="receipt" />
        </va-button>
      </template>
    </va-navbar>

    <div class="main">
      <va-sidebar v-if="showSidebar" class="sidebar">
        <va-sidebar-item :active="route.path === '/'" @click="router.push('/')">
          <va-sidebar-item-content>
            <va-icon name="store" />
            <va-sidebar-item-title>商品</va-sidebar-item-title>
          </va-sidebar-item-content>
        </va-sidebar-item>
        <va-sidebar-item :active="route.path === '/cart'" @click="router.push('/cart')">
          <va-sidebar-item-content>
            <va-icon name="shopping_cart" />
            <va-sidebar-item-title>购物车</va-sidebar-item-title>
          </va-sidebar-item-content>
        </va-sidebar-item>
        <va-sidebar-item :active="route.path === '/orders'" @click="router.push('/orders')">
          <va-sidebar-item-content>
            <va-icon name="receipt" />
            <va-sidebar-item-title>我的订单</va-sidebar-item-title>
          </va-sidebar-item-content>
        </va-sidebar-item>
      </va-sidebar>

      <div class="content">
        <router-view />
      </div>
    </div>
  </div>
</template>

<style>
* { margin: 0; padding: 0; box-sizing: border-box; }
html, body, #app { height: 100%; }

.layout {
  display: flex;
  flex-direction: column;
  height: 100%;
}

.title {
  font-weight: bold;
  font-size: 1.2rem;
  margin-left: 1rem;
  color: white;
}

.main {
  display: flex;
  flex: 1;
  overflow: hidden;
}

.sidebar {
  width: 200px;
  flex-shrink: 0;
}

.content {
  flex: 1;
  padding: 1.5rem;
  overflow-y: auto;
  background: #f5f5f5;
}
</style>
