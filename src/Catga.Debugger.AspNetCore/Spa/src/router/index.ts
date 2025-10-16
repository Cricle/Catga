import { createRouter, createWebHistory, type RouteRecordRaw } from 'vue-router';

const routes: RouteRecordRaw[] = [
  {
    path: '/',
    name: 'Dashboard',
    component: () => import('@/views/Dashboard.vue'),
  },
  {
    path: '/flows',
    name: 'Flows',
    component: () => import('@/views/FlowsView.vue'),
  },
  {
    path: '/flows/:correlationId',
    name: 'FlowDetail',
    component: () => import('@/views/FlowDetail.vue'),
  },
  {
    path: '/replay',
    name: 'Replay',
    component: () => import('@/views/ReplayView.vue'),
  },
];

const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes,
});

export default router;

