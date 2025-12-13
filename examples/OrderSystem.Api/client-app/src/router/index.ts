import { createRouter, createWebHistory } from 'vue-router'

const routes = [
  { path: '/', name: 'Dashboard', component: () => import('../views/Dashboard.vue') },
  { path: '/orders', name: 'Orders', component: () => import('../views/Orders.vue') },
  { path: '/orders/:id', name: 'OrderDetail', component: () => import('../views/OrderDetail.vue') },
  { path: '/flow', name: 'Flow', component: () => import('../views/Flow.vue') },
  { path: '/events', name: 'Events', component: () => import('../views/Events.vue') },
  { path: '/settings', name: 'Settings', component: () => import('../views/Settings.vue') },
]

export default createRouter({
  history: createWebHistory(),
  routes,
})
