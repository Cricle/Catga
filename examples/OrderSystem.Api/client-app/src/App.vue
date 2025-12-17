<script setup lang="ts">
import { ref, computed } from 'vue'
import { useRouter, useRoute } from 'vue-router'
import { useCartStore } from './stores/cart'

const router = useRouter()
const route = useRoute()
const cart = useCartStore()
const mobileMenu = ref(false)

const navItems = [
  { path: '/', icon: 'store', label: '商品' },
  { path: '/cart', icon: 'shopping_cart', label: '购物车' },
  { path: '/orders', icon: 'receipt', label: '订单' },
]

const isActive = (path: string) => route.path === path
</script>

<template>
  <div class="app">
    <header class="header">
      <div class="header-inner">
        <div class="logo" @click="router.push('/')">
          <va-icon name="storefront" size="1.5rem" />
          <span>OrderSystem</span>
        </div>

        <nav class="nav-desktop">
          <a v-for="item in navItems" :key="item.path"
             :class="['nav-item', { active: isActive(item.path) }]"
             @click="router.push(item.path)">
            <va-icon :name="item.icon" />
            <span>{{ item.label }}</span>
            <va-badge v-if="item.path === '/cart' && cart.count > 0"
                      :text="String(cart.count)" color="danger" class="cart-badge" />
          </a>
        </nav>

        <button class="menu-btn" @click="mobileMenu = !mobileMenu">
          <va-icon :name="mobileMenu ? 'close' : 'menu'" size="1.5rem" />
        </button>
      </div>
    </header>

    <nav v-if="mobileMenu" class="nav-mobile">
      <a v-for="item in navItems" :key="item.path"
         :class="['nav-item', { active: isActive(item.path) }]"
         @click="router.push(item.path); mobileMenu = false">
        <va-icon :name="item.icon" />
        <span>{{ item.label }}</span>
        <va-badge v-if="item.path === '/cart' && cart.count > 0"
                  :text="String(cart.count)" color="danger" />
      </a>
    </nav>

    <main class="main">
      <router-view />
    </main>

    <footer class="footer">
      <p>Powered by <strong>Catga</strong> CQRS Framework</p>
    </footer>
  </div>
</template>

<style>
:root {
  --primary: #6366f1;
  --primary-dark: #4f46e5;
  --bg: #f8fafc;
  --card: #ffffff;
  --text: #1e293b;
  --text-secondary: #64748b;
  --border: #e2e8f0;
  --danger: #ef4444;
  --success: #22c55e;
}

* { margin: 0; padding: 0; box-sizing: border-box; }
html, body, #app { min-height: 100vh; }
body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; background: var(--bg); color: var(--text); }

.app {
  min-height: 100vh;
  display: flex;
  flex-direction: column;
}

.header {
  background: var(--card);
  border-bottom: 1px solid var(--border);
  position: sticky;
  top: 0;
  z-index: 100;
}

.header-inner {
  max-width: 1200px;
  margin: 0 auto;
  padding: 0 1rem;
  height: 60px;
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.logo {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  font-weight: 700;
  font-size: 1.25rem;
  color: var(--primary);
  cursor: pointer;
}

.nav-desktop {
  display: flex;
  gap: 0.5rem;
}

.nav-item {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  padding: 0.5rem 1rem;
  border-radius: 8px;
  cursor: pointer;
  color: var(--text-secondary);
  font-weight: 500;
  text-decoration: none;
  transition: all 0.2s;
  position: relative;
}

.nav-item:hover { background: var(--bg); color: var(--primary); }
.nav-item.active { background: var(--primary); color: white; }

.cart-badge { position: absolute; top: 0; right: 0; transform: translate(30%, -30%); }

.menu-btn {
  display: none;
  background: none;
  border: none;
  cursor: pointer;
  padding: 0.5rem;
  color: var(--text);
}

.nav-mobile {
  display: none;
  background: var(--card);
  border-bottom: 1px solid var(--border);
  padding: 1rem;
  flex-direction: column;
  gap: 0.5rem;
}

.main {
  flex: 1;
  max-width: 1200px;
  margin: 0 auto;
  padding: 1.5rem 1rem;
  width: 100%;
}

.footer {
  background: var(--card);
  border-top: 1px solid var(--border);
  padding: 1rem;
  text-align: center;
  color: var(--text-secondary);
  font-size: 0.875rem;
}

@media (max-width: 768px) {
  .nav-desktop { display: none; }
  .menu-btn { display: block; }
  .nav-mobile { display: flex; }
  .main { padding: 1rem; }
}
</style>
