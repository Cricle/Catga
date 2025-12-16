import { createRouter, createWebHistory } from 'vue-router'

const routes = [
  // Shop (User facing)
  { path: '/', name: 'Shop', component: () => import('../views/Shop.vue') },
  { path: '/my-orders', name: 'MyOrders', component: () => import('../views/MyOrders.vue') },
  { path: '/my-orders/:id', name: 'MyOrderDetail', component: () => import('../views/MyOrders.vue') },
  { path: '/cart', redirect: '/' }, // Cart is handled in Shop.vue modal

  // Admin Panel
  { path: '/admin', name: 'AdminDashboard', component: () => import('../views/admin/Dashboard.vue') },
  { path: '/admin/orders', name: 'AdminOrders', component: () => import('../views/admin/Orders.vue') },
  { path: '/admin/orders/:id', name: 'AdminOrderDetail', component: () => import('../views/admin/Orders.vue') },
  { path: '/admin/observability', name: 'AdminObservability', component: () => import('../views/admin/Observability.vue') },
  { path: '/admin/hotreload', name: 'AdminHotReload', component: () => import('../views/admin/HotReload.vue') },
  { path: '/admin/readmodelsync', name: 'AdminReadModelSync', component: () => import('../views/admin/ReadModelSync.vue') },
  { path: '/admin/settings', name: 'AdminSettings', component: () => import('../views/admin/Settings.vue') },
]

export default createRouter({
  history: createWebHistory(),
  routes,
})
