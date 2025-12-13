<template>
  <div class="orders-page">
    <va-card>
      <va-card-title>
        <div class="header-row">
          <span>Order Management</span>
          <div class="actions">
            <va-select
              v-model="statusFilter"
              :options="statusOptions"
              placeholder="Filter by status"
              clearable
              class="filter-select"
            />
            <va-button color="primary" @click="showCreateModal = true">
              <va-icon name="add" class="mr-2" />
              New Order
            </va-button>
          </div>
        </div>
      </va-card-title>
      <va-card-content>
        <va-data-table
          :items="filteredOrders"
          :columns="columns"
          :loading="loading"
          hoverable
          clickable
          striped
          @row:click="(row) => $router.push(`/orders/${row.item.orderId}`)"
        >
          <template #cell(status)="{ value }">
            <va-badge :text="StatusNames[value]" :color="StatusColors[value]" />
          </template>
          <template #cell(totalAmount)="{ value }">
            <strong>${{ value.toFixed(2) }}</strong>
          </template>
          <template #cell(createdAt)="{ value }">
            {{ new Date(value).toLocaleString() }}
          </template>
          <template #cell(items)="{ value }">
            {{ value?.length || 0 }} items
          </template>
          <template #cell(actions)="{ row }">
            <va-button-group>
              <va-button size="small" preset="secondary" @click.stop="viewOrder(row.item.orderId)">
                View
              </va-button>
            </va-button-group>
          </template>
        </va-data-table>
      </va-card-content>
    </va-card>

    <va-modal v-model="showCreateModal" title="Create Order" hide-default-actions>
      <CreateOrderForm @created="onOrderCreated" @cancel="showCreateModal = false" />
    </va-modal>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { useOrderStore } from '../stores/order'
import { StatusNames, StatusColors, type OrderStatus } from '../types'
import CreateOrderForm from '../components/CreateOrderForm.vue'

const router = useRouter()
const store = useOrderStore()
const showCreateModal = ref(false)
const statusFilter = ref<OrderStatus | null>(null)

const orders = computed(() => store.orders)
const loading = computed(() => store.loading)

const statusOptions = [
  { value: 0, text: 'Pending' },
  { value: 1, text: 'Paid' },
  { value: 2, text: 'Processing' },
  { value: 3, text: 'Shipped' },
  { value: 4, text: 'Delivered' },
  { value: 5, text: 'Cancelled' },
]

const filteredOrders = computed(() => {
  if (statusFilter.value === null) return orders.value
  return orders.value.filter(o => o.status === statusFilter.value)
})

const columns = [
  { key: 'orderId', label: 'Order ID', sortable: true },
  { key: 'customerId', label: 'Customer', sortable: true },
  { key: 'items', label: 'Items' },
  { key: 'status', label: 'Status', sortable: true },
  { key: 'totalAmount', label: 'Amount', sortable: true },
  { key: 'createdAt', label: 'Created', sortable: true },
  { key: 'actions', label: '' },
]

function viewOrder(orderId: string) {
  router.push(`/orders/${orderId}`)
}

function onOrderCreated() {
  showCreateModal.value = false
  store.fetchOrders()
}

onMounted(() => {
  store.fetchOrders()
})
</script>

<style scoped>
.header-row {
  display: flex;
  justify-content: space-between;
  align-items: center;
  width: 100%;
}

.actions {
  display: flex;
  gap: 1rem;
  align-items: center;
}

.filter-select {
  width: 200px;
}

.mr-2 {
  margin-right: 0.5rem;
}
</style>
