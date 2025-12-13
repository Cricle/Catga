<template>
  <div class="order-detail" v-if="order">
    <va-card>
      <va-card-title>
        <div class="header-row">
          <div>
            <va-button preset="secondary" size="small" @click="$router.back()">
              <va-icon name="arrow_back" />
            </va-button>
            <span class="order-id">{{ order.orderId }}</span>
            <va-badge :text="StatusNames[order.status]" :color="StatusColors[order.status]" class="ml-2" />
          </div>
        </div>
      </va-card-title>
      <va-card-content>
        <!-- Lifecycle Progress -->
        <div class="lifecycle-section">
          <h4>Order Lifecycle</h4>
          <va-progress-bar
            :model-value="lifecycleProgress"
            :color="order.status === 5 ? 'danger' : 'primary'"
          />
          <div class="lifecycle-steps">
            <div
              v-for="(step, i) in lifecycleSteps"
              :key="step.name"
              :class="['step', { active: i <= currentStepIndex, current: i === currentStepIndex }]"
            >
              <va-icon :name="step.icon" :color="i <= currentStepIndex ? 'primary' : 'secondary'" />
              <span>{{ step.name }}</span>
            </div>
          </div>
        </div>

        <va-divider />

        <!-- Order Info -->
        <div class="info-grid">
          <div class="info-item">
            <label>Customer ID</label>
            <span>{{ order.customerId }}</span>
          </div>
          <div class="info-item">
            <label>Total Amount</label>
            <span class="amount">${{ order.totalAmount.toFixed(2) }}</span>
          </div>
          <div class="info-item">
            <label>Created At</label>
            <span>{{ new Date(order.createdAt).toLocaleString() }}</span>
          </div>
          <div class="info-item" v-if="order.paidAt">
            <label>Paid At</label>
            <span>{{ new Date(order.paidAt).toLocaleString() }}</span>
          </div>
          <div class="info-item" v-if="order.shippedAt">
            <label>Shipped At</label>
            <span>{{ new Date(order.shippedAt).toLocaleString() }}</span>
          </div>
          <div class="info-item" v-if="order.trackingNumber">
            <label>Tracking Number</label>
            <span class="tracking">{{ order.trackingNumber }}</span>
          </div>
          <div class="info-item" v-if="order.paymentMethod">
            <label>Payment Method</label>
            <span>{{ order.paymentMethod }}</span>
          </div>
          <div class="info-item" v-if="order.cancellationReason">
            <label>Cancellation Reason</label>
            <span class="danger">{{ order.cancellationReason }}</span>
          </div>
        </div>

        <va-divider />

        <!-- Items -->
        <h4>Order Items</h4>
        <va-data-table :items="order.items" :columns="itemColumns">
          <template #cell(unitPrice)="{ value }">
            ${{ value.toFixed(2) }}
          </template>
          <template #cell(subtotal)="{ row }">
            ${{ (row.item.quantity * row.item.unitPrice).toFixed(2) }}
          </template>
        </va-data-table>

        <va-divider />

        <!-- Actions -->
        <div class="actions">
          <template v-if="order.status === 0">
            <va-button color="success" @click="pay">
              <va-icon name="payment" class="mr-2" />
              Pay Order
            </va-button>
            <va-button color="danger" @click="cancel">
              <va-icon name="cancel" class="mr-2" />
              Cancel
            </va-button>
          </template>
          <template v-else-if="order.status === 1">
            <va-button color="secondary" @click="process">
              <va-icon name="settings" class="mr-2" />
              Start Processing
            </va-button>
            <va-button color="danger" @click="cancel">Cancel</va-button>
          </template>
          <template v-else-if="order.status === 2">
            <va-button color="primary" @click="showShipModal = true">
              <va-icon name="local_shipping" class="mr-2" />
              Ship Order
            </va-button>
            <va-button color="danger" @click="cancel">Cancel</va-button>
          </template>
          <template v-else-if="order.status === 3">
            <va-button color="success" @click="deliver">
              <va-icon name="check_circle" class="mr-2" />
              Mark Delivered
            </va-button>
          </template>
        </div>
      </va-card-content>
    </va-card>

    <!-- Ship Modal -->
    <va-modal v-model="showShipModal" title="Ship Order">
      <va-input v-model="trackingNumber" label="Tracking Number" class="mb-4" />
      <template #footer>
        <va-button @click="showShipModal = false">Cancel</va-button>
        <va-button color="primary" @click="ship">Confirm Ship</va-button>
      </template>
    </va-modal>
  </div>
  <div v-else class="loading">
    <va-progress-circle indeterminate />
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRoute } from 'vue-router'
import { useOrderStore } from '../stores/order'
import { StatusNames, StatusColors } from '../types'

const route = useRoute()
const store = useOrderStore()
const showShipModal = ref(false)
const trackingNumber = ref('')

const order = computed(() => store.currentOrder)

const lifecycleSteps = [
  { name: 'Pending', icon: 'schedule' },
  { name: 'Paid', icon: 'payment' },
  { name: 'Processing', icon: 'settings' },
  { name: 'Shipped', icon: 'local_shipping' },
  { name: 'Delivered', icon: 'check_circle' },
]

const currentStepIndex = computed(() => {
  if (!order.value) return 0
  if (order.value.status === 5) return -1 // Cancelled
  return Math.min(order.value.status, 4)
})

const lifecycleProgress = computed(() => {
  if (!order.value || order.value.status === 5) return 0
  return (currentStepIndex.value / 4) * 100
})

const itemColumns = [
  { key: 'productName', label: 'Product' },
  { key: 'productId', label: 'SKU' },
  { key: 'quantity', label: 'Qty' },
  { key: 'unitPrice', label: 'Price' },
  { key: 'subtotal', label: 'Subtotal' },
]

async function pay() {
  await store.payOrder(order.value!.orderId, 'Credit Card', `TXN-${Date.now()}`)
}

async function process() {
  await store.processOrder(order.value!.orderId)
}

async function ship() {
  await store.shipOrder(order.value!.orderId, trackingNumber.value || `TRK-${Date.now()}`)
  showShipModal.value = false
}

async function deliver() {
  await store.deliverOrder(order.value!.orderId)
}

async function cancel() {
  if (confirm('Are you sure you want to cancel this order?')) {
    await store.cancelOrder(order.value!.orderId, 'Cancelled by user')
  }
}

onMounted(() => {
  const orderId = route.params.id as string
  store.fetchOrder(orderId)
})
</script>

<style scoped>
.header-row {
  display: flex;
  align-items: center;
  gap: 1rem;
}

.order-id {
  font-size: 1.25rem;
  font-weight: bold;
  margin-left: 0.5rem;
}

.lifecycle-section {
  margin: 1rem 0;
}

.lifecycle-steps {
  display: flex;
  justify-content: space-between;
  margin-top: 1rem;
}

.step {
  display: flex;
  flex-direction: column;
  align-items: center;
  opacity: 0.5;
  transition: all 0.3s ease;
}

.step.active {
  opacity: 1;
}

.step.current {
  transform: scale(1.1);
}

.info-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
  gap: 1rem;
  margin: 1rem 0;
}

.info-item label {
  display: block;
  font-size: 0.75rem;
  color: #666;
  margin-bottom: 0.25rem;
}

.info-item span {
  font-weight: 500;
}

.amount {
  font-size: 1.5rem;
  color: #10b981;
}

.tracking {
  font-family: monospace;
  background: #f3f4f6;
  padding: 0.25rem 0.5rem;
  border-radius: 4px;
}

.danger {
  color: #ef4444;
}

.actions {
  display: flex;
  gap: 1rem;
  margin-top: 1rem;
}

.loading {
  display: flex;
  justify-content: center;
  align-items: center;
  min-height: 400px;
}

.ml-2 { margin-left: 0.5rem; }
.mr-2 { margin-right: 0.5rem; }
.mb-4 { margin-bottom: 1rem; }
</style>
