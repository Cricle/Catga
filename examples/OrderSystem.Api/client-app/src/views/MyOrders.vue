<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import { useRouter, useRoute } from 'vue-router'
import { useOrderStore } from '../stores/order'
import type { Order, OrderStatus } from '../types'

const router = useRouter()
const route = useRoute()
const store = useOrderStore()

const orders = ref<Order[]>([])
const loading = ref(false)
const selectedOrder = ref<Order | null>(null)

const statusLabels: Record<OrderStatus, { text: string; color: string }> = {
  0: { text: '待支付', color: 'warning' },
  1: { text: '已支付', color: 'info' },
  2: { text: '处理中', color: 'primary' },
  3: { text: '已发货', color: 'secondary' },
  4: { text: '已完成', color: 'success' },
  5: { text: '已取消', color: 'danger' }
}

const loadOrders = async () => {
  loading.value = true
  try {
    // In real app, filter by current user's customer ID
    orders.value = await store.fetchOrders()
  } finally {
    loading.value = false
  }
}

const viewOrder = (order: Order) => {
  selectedOrder.value = order
}

const payOrder = async (orderId: string) => {
  await store.payOrder(orderId, 'Alipay', `PAY-${Date.now()}`)
  await loadOrders()
  if (selectedOrder.value?.orderId === orderId) {
    selectedOrder.value = orders.value.find(o => o.orderId === orderId) || null
  }
}

const cancelOrder = async (orderId: string) => {
  await store.cancelOrder(orderId, '用户取消')
  await loadOrders()
  if (selectedOrder.value?.orderId === orderId) {
    selectedOrder.value = orders.value.find(o => o.orderId === orderId) || null
  }
}

const confirmReceipt = async (orderId: string) => {
  // Simulate delivery confirmation (in real app would call API)
  await store.deliverOrder(orderId)
  await loadOrders()
  if (selectedOrder.value?.orderId === orderId) {
    selectedOrder.value = orders.value.find(o => o.orderId === orderId) || null
  }
}

const formatDate = (date?: string | Date) => {
  if (!date) return '-'
  return new Date(date).toLocaleString('zh-CN')
}

onMounted(() => {
  loadOrders()
})
</script>

<template>
  <div class="my-orders-page">
    <h1>我的订单</h1>

    <va-card>
      <va-card-content>
        <va-data-table
          :items="orders"
          :columns="[
            { key: 'orderId', label: '订单号', width: '180px' },
            { key: 'totalAmount', label: '金额' },
            { key: 'status', label: '状态' },
            { key: 'createdAt', label: '创建时间' },
            { key: 'actions', label: '操作', width: '200px' }
          ]"
          :loading="loading"
        >
          <template #cell(orderId)="{ value }">
            <code class="order-id">{{ value }}</code>
          </template>

          <template #cell(totalAmount)="{ value }">
            <span class="amount">¥{{ value?.toFixed(2) || '0.00' }}</span>
          </template>

          <template #cell(status)="{ value }">
            <va-badge
              :text="statusLabels[value as OrderStatus]?.text || '未知'"
              :color="statusLabels[value as OrderStatus]?.color || 'secondary'"
            />
          </template>

          <template #cell(createdAt)="{ value }">
            {{ formatDate(value) }}
          </template>

          <template #cell(actions)="{ row }">
            <div class="action-buttons">
              <va-button size="small" preset="secondary" @click="viewOrder(row)">
                详情
              </va-button>
              <va-button
                v-if="row.status === 0"
                size="small"
                color="success"
                @click="payOrder(row.orderId)"
              >
                支付
              </va-button>
              <va-button
                v-if="row.status === 3"
                size="small"
                color="primary"
                @click="confirmReceipt(row.orderId)"
              >
                确认收货
              </va-button>
              <va-button
                v-if="row.status < 3"
                size="small"
                preset="secondary"
                color="danger"
                @click="cancelOrder(row.orderId)"
              >
                取消
              </va-button>
            </div>
          </template>
        </va-data-table>
      </va-card-content>
    </va-card>

    <!-- Order Detail Modal -->
    <va-modal v-model="selectedOrder" title="订单详情" size="large" @ok="selectedOrder = null">
      <template v-if="selectedOrder">
        <div class="order-detail">
          <div class="detail-row">
            <span class="label">订单号:</span>
            <code>{{ selectedOrder.orderId }}</code>
          </div>
          <div class="detail-row">
            <span class="label">状态:</span>
            <va-badge
              :text="statusLabels[selectedOrder.status]?.text"
              :color="statusLabels[selectedOrder.status]?.color"
            />
          </div>
          <div class="detail-row">
            <span class="label">创建时间:</span>
            <span>{{ formatDate(selectedOrder.createdAt) }}</span>
          </div>
          <div class="detail-row" v-if="selectedOrder.paidAt">
            <span class="label">支付时间:</span>
            <span>{{ formatDate(selectedOrder.paidAt) }}</span>
          </div>
          <div class="detail-row" v-if="selectedOrder.shippedAt">
            <span class="label">发货时间:</span>
            <span>{{ formatDate(selectedOrder.shippedAt) }}</span>
          </div>
          <div class="detail-row" v-if="selectedOrder.trackingNumber">
            <span class="label">物流单号:</span>
            <code>{{ selectedOrder.trackingNumber }}</code>
          </div>

          <va-divider />

          <h4>商品列表</h4>
          <div class="order-items">
            <div v-for="item in selectedOrder.items" :key="item.productId" class="order-item">
              <span class="item-name">{{ item.productName }}</span>
              <span class="item-qty">x{{ item.quantity }}</span>
              <span class="item-price">¥{{ (item.unitPrice * item.quantity).toFixed(2) }}</span>
            </div>
          </div>

          <va-divider />

          <div class="order-total">
            <span>订单总额:</span>
            <span class="total-amount">¥{{ selectedOrder.totalAmount.toFixed(2) }}</span>
          </div>
        </div>
      </template>
    </va-modal>
  </div>
</template>

<style scoped>
.my-orders-page h1 {
  font-size: 1.5rem;
  margin-bottom: 1.5rem;
}

.order-id {
  font-size: 0.85rem;
}

.amount {
  font-weight: 600;
  color: #e53e3e;
}

.action-buttons {
  display: flex;
  gap: 0.5rem;
}

.order-detail {
  min-width: 400px;
}

.detail-row {
  display: flex;
  justify-content: space-between;
  padding: 0.5rem 0;
}

.detail-row .label {
  color: #666;
}

.order-items {
  margin: 1rem 0;
}

.order-item {
  display: flex;
  justify-content: space-between;
  padding: 0.75rem 0;
  border-bottom: 1px solid #eee;
}

.item-name {
  flex: 1;
}

.item-qty {
  color: #666;
  margin: 0 1rem;
}

.item-price {
  font-weight: 500;
}

.order-total {
  display: flex;
  justify-content: space-between;
  padding: 1rem 0;
  font-size: 1.1rem;
}

.total-amount {
  font-size: 1.25rem;
  font-weight: 700;
  color: #e53e3e;
}
</style>
