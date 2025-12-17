import { createRouter, createWebHistory } from 'vue-router'

const routes = [
  { path: '/', component: () => import('../views/Shop.vue') },
  { path: '/cart', component: () => import('../views/Cart.vue') },
  { path: '/orders', component: () => import('../views/Orders.vue') },
]

export default createRouter({
  history: createWebHistory(),
  routes,
})
