<template>
  <div class="dashboard">
    <!-- Stats Cards -->
    <div class="stats-grid">
      <va-card class="stat-card">
        <va-card-content>
          <div class="stat-value">{{ stats?.total || 0 }}</div>
          <div class="stat-label">Total Orders</div>
        </va-card-content>
      </va-card>
      <va-card class="stat-card" color="warning">
        <va-card-content>
          <div class="stat-value">{{ stats?.pending || 0 }}</div>
          <div class="stat-label">Pending</div>
        </va-card-content>
      </va-card>
      <va-card class="stat-card" color="info">
        <va-card-content>
          <div class="stat-value">{{ stats?.paid || 0 }}</div>
          <div class="stat-label">Paid</div>
        </va-card-content>
      </va-card>
      <va-card class="stat-card" color="secondary">
        <va-card-content>
          <div class="stat-value">{{ stats?.processing || 0 }}</div>
          <div class="stat-label">Processing</div>
        </va-card-content>
      </va-card>
      <va-card class="stat-card" color="primary">
        <va-card-content>
          <div class="stat-value">{{ stats?.shipped || 0 }}</div>
          <div class="stat-label">Shipped</div>
        </va-card-content>
      </va-card>
      <va-card class="stat-card" color="success">
        <va-card-content>
          <div class="stat-value">{{ stats?.delivered || 0 }}</div>
          <div class="stat-label">Delivered</div>
        </va-card-content>
      </va-card>
      <va-card class="stat-card" color="danger">
        <va-card-content>
          <div class="stat-value">{{ stats?.cancelled || 0 }}</div>
          <div class="stat-label">Cancelled</div>
        </va-card-content>
      </va-card>
      <va-card class="stat-card revenue">
        <va-card-content>
          <div class="stat-value">${{ (stats?.totalRevenue || 0).toLocaleString() }}</div>
          <div class="stat-label">Revenue</div>
        </va-card-content>
      </va-card>
    </div>

    <!-- Quick Actions -->
    <va-card class="mt-4">
      <va-card-title>Quick Actions</va-card-title>
      <va-card-content>
        <div class="quick-actions">
          <va-button color="primary" @click="showCreateModal = true">
            <va-icon name="add" class="mr-2" />
            Create Order
          </va-button>
          <va-button color="secondary" @click="$router.push('/flow')">
            <va-icon name="account_tree" class="mr-2" />
            Flow Demo
          </va-button>
          <va-button @click="refreshData">
            <va-icon name="refresh" class="mr-2" />
            Refresh
          </va-button>
        </div>
      </va-card-content>
    </va-card>

    <!-- Recent Orders -->
    <va-card class="mt-4">
      <va-card-title>Recent Orders</va-card-title>
      <va-card-content>
        <va-data-table
          :items="orders.slice(0, 10)"
          :columns="columns"
          :loading="loading"
          hoverable
          clickable
          @row:click="(row) => $router.push(`/orders/${row.item.orderId}`)"
        >
          <template #cell(status)="{ value }">
            <va-badge :text="StatusNames[value]" :color="StatusColors[value]" />
          </template>
          <template #cell(totalAmount)="{ value }">
            ${{ value.toFixed(2) }}
          </template>
          <template #cell(createdAt)="{ value }">
            {{ new Date(value).toLocaleString() }}
          </template>
        </va-data-table>
      </va-card-content>
    </va-card>

    <!-- Create Order Modal -->
    <va-modal v-model="showCreateModal" title="Create Order" hide-default-actions>
      <CreateOrderForm @created="onOrderCreated" @cancel="showCreateModal = false" />
    </va-modal>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import { useOrderStore } from '../stores/order'
import { StatusNames, StatusColors } from '../types'
import CreateOrderForm from '../components/CreateOrderForm.vue'

const store = useOrderStore()
const showCreateModal = ref(false)

const stats = computed(() => store.stats)
const orders = computed(() => store.orders)
const loading = computed(() => store.loading)

const columns = [
  { key: 'orderId', label: 'Order ID' },
  { key: 'customerId', label: 'Customer' },
  { key: 'status', label: 'Status' },
  { key: 'totalAmount', label: 'Amount' },
  { key: 'createdAt', label: 'Created' },
]

async function refreshData() {
  await Promise.all([store.fetchOrders(), store.fetchStats()])
}

function onOrderCreated() {
  showCreateModal.value = false
  refreshData()
}

onMounted(() => {
  refreshData()
  // Auto-refresh every 10 seconds
  setInterval(refreshData, 10000)
})
</script>

<style scoped>
.stats-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(150px, 1fr));
  gap: 1rem;
}

.stat-card {
  text-align: center;
}

.stat-value {
  font-size: 2rem;
  font-weight: bold;
}

.stat-label {
  font-size: 0.875rem;
  opacity: 0.8;
}

.revenue {
  background: linear-gradient(135deg, #10b981, #059669);
  color: white;
}

.quick-actions {
  display: flex;
  gap: 1rem;
  flex-wrap: wrap;
}

.mt-4 {
  margin-top: 1rem;
}

.mr-2 {
  margin-right: 0.5rem;
}
</style>
