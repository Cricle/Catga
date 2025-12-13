<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import { useOrderStore } from '../../stores/order'
import type { OrderStats } from '../../types'

const store = useOrderStore()
const stats = ref<OrderStats | null>(null)
const recentOrders = ref<any[]>([])
const loading = ref(false)

const statusColors = ['warning', 'info', 'primary', 'secondary', 'success', 'danger']
const statusLabels = ['待支付', '已支付', '处理中', '已发货', '已完成', '已取消']

const loadData = async () => {
  loading.value = true
  try {
    stats.value = await store.fetchStats()
    recentOrders.value = (await store.fetchOrders(10)).slice(0, 5)
  } finally {
    loading.value = false
  }
}

const statusDistribution = computed(() => {
  if (!stats.value) return []
  return [
    { label: '待支付', value: stats.value.pending, color: '#ecc94b' },
    { label: '已支付', value: stats.value.paid, color: '#4299e1' },
    { label: '处理中', value: stats.value.processing, color: '#667eea' },
    { label: '已发货', value: stats.value.shipped, color: '#718096' },
    { label: '已完成', value: stats.value.delivered, color: '#48bb78' },
    { label: '已取消', value: stats.value.cancelled, color: '#f56565' },
  ]
})

const formatDate = (date: string) => new Date(date).toLocaleString('zh-CN')

onMounted(() => {
  loadData()
  // Auto refresh every 30 seconds
  setInterval(loadData, 30000)
})
</script>

<template>
  <div class="admin-dashboard">
    <h1>管理仪表盘</h1>

    <!-- Stats Cards -->
    <div class="stats-grid">
      <va-card class="stat-card">
        <va-card-content>
          <div class="stat-icon" style="background: #667eea;">
            <va-icon name="receipt_long" color="#fff" />
          </div>
          <div class="stat-info">
            <span class="stat-label">总订单数</span>
            <span class="stat-value">{{ stats?.total || 0 }}</span>
          </div>
        </va-card-content>
      </va-card>

      <va-card class="stat-card">
        <va-card-content>
          <div class="stat-icon" style="background: #48bb78;">
            <va-icon name="payments" color="#fff" />
          </div>
          <div class="stat-info">
            <span class="stat-label">总收入</span>
            <span class="stat-value">¥{{ (stats?.totalRevenue || 0).toFixed(2) }}</span>
          </div>
        </va-card-content>
      </va-card>

      <va-card class="stat-card">
        <va-card-content>
          <div class="stat-icon" style="background: #ecc94b;">
            <va-icon name="pending" color="#fff" />
          </div>
          <div class="stat-info">
            <span class="stat-label">待处理</span>
            <span class="stat-value">{{ (stats?.pending || 0) + (stats?.paid || 0) }}</span>
          </div>
        </va-card-content>
      </va-card>

      <va-card class="stat-card">
        <va-card-content>
          <div class="stat-icon" style="background: #4299e1;">
            <va-icon name="local_shipping" color="#fff" />
          </div>
          <div class="stat-info">
            <span class="stat-label">运输中</span>
            <span class="stat-value">{{ stats?.shipped || 0 }}</span>
          </div>
        </va-card-content>
      </va-card>
    </div>

    <!-- Charts Row -->
    <div class="charts-row">
      <va-card class="chart-card">
        <va-card-title>订单状态分布</va-card-title>
        <va-card-content>
          <div class="status-bars">
            <div
              v-for="item in statusDistribution"
              :key="item.label"
              class="status-bar-item"
            >
              <div class="bar-label">
                <span>{{ item.label }}</span>
                <span>{{ item.value }}</span>
              </div>
              <div class="bar-track">
                <div
                  class="bar-fill"
                  :style="{
                    width: `${(item.value / (stats?.total || 1)) * 100}%`,
                    background: item.color
                  }"
                ></div>
              </div>
            </div>
          </div>
        </va-card-content>
      </va-card>

      <va-card class="chart-card">
        <va-card-title>最近订单</va-card-title>
        <va-card-content>
          <div class="recent-orders">
            <div v-for="order in recentOrders" :key="order.orderId" class="recent-order-item">
              <div class="order-info">
                <code class="order-id">{{ order.orderId }}</code>
                <span class="order-amount">¥{{ order.totalAmount?.toFixed(2) }}</span>
              </div>
              <va-badge
                :text="statusLabels[order.status]"
                :color="statusColors[order.status]"
                size="small"
              />
            </div>
          </div>
        </va-card-content>
      </va-card>
    </div>

    <!-- Quick Actions -->
    <va-card>
      <va-card-title>快捷操作</va-card-title>
      <va-card-content>
        <div class="quick-actions">
          <va-button @click="$router.push('/admin/orders')">
            <va-icon name="list_alt" class="mr-2" />
            查看所有订单
          </va-button>
          <va-button preset="secondary" @click="loadData" :loading="loading">
            <va-icon name="refresh" class="mr-2" />
            刷新数据
          </va-button>
        </div>
      </va-card-content>
    </va-card>
  </div>
</template>

<style scoped>
.admin-dashboard h1 {
  font-size: 1.5rem;
  margin-bottom: 1.5rem;
}

.stats-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
  gap: 1rem;
  margin-bottom: 1.5rem;
}

.stat-card .va-card-content {
  display: flex;
  align-items: center;
  gap: 1rem;
}

.stat-icon {
  width: 48px;
  height: 48px;
  border-radius: 12px;
  display: flex;
  align-items: center;
  justify-content: center;
}

.stat-info {
  display: flex;
  flex-direction: column;
}

.stat-label {
  font-size: 0.875rem;
  color: #666;
}

.stat-value {
  font-size: 1.5rem;
  font-weight: 700;
}

.charts-row {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(350px, 1fr));
  gap: 1rem;
  margin-bottom: 1.5rem;
}

.chart-card {
  min-height: 300px;
}

.status-bars {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.status-bar-item {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
}

.bar-label {
  display: flex;
  justify-content: space-between;
  font-size: 0.875rem;
}

.bar-track {
  height: 8px;
  background: #e2e8f0;
  border-radius: 4px;
  overflow: hidden;
}

.bar-fill {
  height: 100%;
  border-radius: 4px;
  transition: width 0.3s ease;
}

.recent-orders {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.recent-order-item {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 0.5rem 0;
  border-bottom: 1px solid #eee;
}

.order-info {
  display: flex;
  flex-direction: column;
}

.order-id {
  font-size: 0.8rem;
}

.order-amount {
  font-weight: 600;
  color: #e53e3e;
}

.quick-actions {
  display: flex;
  gap: 1rem;
  flex-wrap: wrap;
}

.mr-2 {
  margin-right: 0.5rem;
}
</style>
