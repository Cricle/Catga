<script setup lang="ts">
import { ref, onMounted, watch } from 'vue'
import { useOrderStore } from '../../stores/order'
import type { Order, OrderStatus } from '../../types'

const store = useOrderStore()
const orders = ref<Order[]>([])
const loading = ref(false)
const selectedOrder = ref<Order | null>(null)
const statusFilter = ref<OrderStatus | null>(null)

const statusOptions = [
  { value: null, label: '全部' },
  { value: 0, label: '待支付' },
  { value: 1, label: '已支付' },
  { value: 2, label: '处理中' },
  { value: 3, label: '已发货' },
  { value: 4, label: '已完成' },
  { value: 5, label: '已取消' },
]

const statusLabels: Record<number, { text: string; color: string }> = {
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
    orders.value = await store.fetchOrders(100, statusFilter.value ?? undefined)
  } finally {
    loading.value = false
  }
}

watch(statusFilter, () => loadOrders())

// Order actions
const processOrder = async (orderId: string) => {
  await store.processOrder(orderId)
  await loadOrders()
  updateSelectedOrder(orderId)
}

const shipOrder = async (orderId: string, trackingNumber: string) => {
  await store.shipOrder(orderId, trackingNumber)
  await loadOrders()
  updateSelectedOrder(orderId)
}

const deliverOrder = async (orderId: string) => {
  await store.deliverOrder(orderId)
  await loadOrders()
  updateSelectedOrder(orderId)
}

const cancelOrder = async (orderId: string, reason: string) => {
  await store.cancelOrder(orderId, reason)
  await loadOrders()
  updateSelectedOrder(orderId)
}

const updateSelectedOrder = (orderId: string) => {
  if (selectedOrder.value?.orderId === orderId) {
    selectedOrder.value = orders.value.find(o => o.orderId === orderId) || null
  }
}

// Ship dialog
const showShipDialog = ref(false)
const shipOrderId = ref('')
const trackingNumber = ref('')

const openShipDialog = (orderId: string) => {
  shipOrderId.value = orderId
  trackingNumber.value = 'SF' + Date.now()
  showShipDialog.value = true
}

const confirmShip = async () => {
  await shipOrder(shipOrderId.value, trackingNumber.value)
  showShipDialog.value = false
}

// Cancel dialog
const showCancelDialog = ref(false)
const cancelOrderId = ref('')
const cancelReason = ref('')

const openCancelDialog = (orderId: string) => {
  cancelOrderId.value = orderId
  cancelReason.value = ''
  showCancelDialog.value = true
}

const confirmCancel = async () => {
  await cancelOrder(cancelOrderId.value, cancelReason.value || '管理员取消')
  showCancelDialog.value = false
}

const formatDate = (date?: string | Date) => {
  if (!date) return '-'
  return new Date(date).toLocaleString('zh-CN')
}

onMounted(() => loadOrders())
</script>

<template>
  <div class="admin-orders">
    <div class="page-header">
      <h1>订单管理</h1>
      <div class="header-actions">
        <va-select
          v-model="statusFilter"
          :options="statusOptions"
          placeholder="筛选状态"
          style="width: 150px;"
          clearable
        />
        <va-button preset="secondary" @click="loadOrders" :loading="loading">
          <va-icon name="refresh" />
        </va-button>
      </div>
    </div>

    <va-card>
      <va-card-content>
        <va-data-table
          :items="orders"
          :columns="[
            { key: 'orderId', label: '订单号', width: '180px' },
            { key: 'customerId', label: '客户ID', width: '150px' },
            { key: 'totalAmount', label: '金额' },
            { key: 'status', label: '状态' },
            { key: 'createdAt', label: '创建时间' },
            { key: 'actions', label: '操作', width: '280px' }
          ]"
          :loading="loading"
        >
          <template #cell(orderId)="{ value }">
            <code class="order-id">{{ value }}</code>
          </template>

          <template #cell(customerId)="{ value }">
            <code class="customer-id">{{ value }}</code>
          </template>

          <template #cell(totalAmount)="{ value }">
            <span class="amount">¥{{ value?.toFixed(2) || '0.00' }}</span>
          </template>

          <template #cell(status)="{ value }">
            <va-badge
              :text="statusLabels[value]?.text || '未知'"
              :color="statusLabels[value]?.color || 'secondary'"
            />
          </template>

          <template #cell(createdAt)="{ value }">
            {{ formatDate(value) }}
          </template>

          <template #cell(actions)="{ row }">
            <div class="action-buttons">
              <va-button size="small" preset="secondary" @click="selectedOrder = row">
                详情
              </va-button>

              <!-- Status: Paid -> Processing -->
              <va-button
                v-if="row.status === 1"
                size="small"
                color="primary"
                @click="processOrder(row.orderId)"
              >
                处理
              </va-button>

              <!-- Status: Processing -> Shipped -->
              <va-button
                v-if="row.status === 2"
                size="small"
                color="info"
                @click="openShipDialog(row.orderId)"
              >
                发货
              </va-button>

              <!-- Status: Shipped -> Delivered -->
              <va-button
                v-if="row.status === 3"
                size="small"
                color="success"
                @click="deliverOrder(row.orderId)"
              >
                确认送达
              </va-button>

              <!-- Cancel (before delivery) -->
              <va-button
                v-if="row.status < 4"
                size="small"
                preset="secondary"
                color="danger"
                @click="openCancelDialog(row.orderId)"
              >
                取消
              </va-button>
            </div>
          </template>
        </va-data-table>
      </va-card-content>
    </va-card>

    <!-- Order Detail Modal -->
    <va-modal v-model="selectedOrder" title="订单详情" size="large">
      <template v-if="selectedOrder">
        <div class="order-detail">
          <div class="detail-section">
            <h4>基本信息</h4>
            <div class="detail-grid">
              <div class="detail-item">
                <span class="label">订单号</span>
                <code>{{ selectedOrder.orderId }}</code>
              </div>
              <div class="detail-item">
                <span class="label">客户ID</span>
                <code>{{ selectedOrder.customerId }}</code>
              </div>
              <div class="detail-item">
                <span class="label">状态</span>
                <va-badge
                  :text="statusLabels[selectedOrder.status]?.text"
                  :color="statusLabels[selectedOrder.status]?.color"
                />
              </div>
              <div class="detail-item">
                <span class="label">总金额</span>
                <span class="amount">¥{{ selectedOrder.totalAmount.toFixed(2) }}</span>
              </div>
            </div>
          </div>

          <div class="detail-section">
            <h4>时间线</h4>
            <div class="timeline">
              <div class="timeline-item">
                <va-icon name="add_circle" color="primary" />
                <span>创建: {{ formatDate(selectedOrder.createdAt) }}</span>
              </div>
              <div v-if="selectedOrder.paidAt" class="timeline-item">
                <va-icon name="payment" color="success" />
                <span>支付: {{ formatDate(selectedOrder.paidAt) }}</span>
              </div>
              <div v-if="selectedOrder.shippedAt" class="timeline-item">
                <va-icon name="local_shipping" color="info" />
                <span>发货: {{ formatDate(selectedOrder.shippedAt) }}</span>
              </div>
              <div v-if="selectedOrder.deliveredAt" class="timeline-item">
                <va-icon name="check_circle" color="success" />
                <span>送达: {{ formatDate(selectedOrder.deliveredAt) }}</span>
              </div>
              <div v-if="selectedOrder.cancelledAt" class="timeline-item">
                <va-icon name="cancel" color="danger" />
                <span>取消: {{ formatDate(selectedOrder.cancelledAt) }}</span>
              </div>
            </div>
          </div>

          <div class="detail-section">
            <h4>商品列表</h4>
            <div class="items-list">
              <div v-for="item in selectedOrder.items" :key="item.productId" class="item-row">
                <span class="item-name">{{ item.productName }}</span>
                <span class="item-qty">x{{ item.quantity }}</span>
                <span class="item-price">¥{{ item.unitPrice.toFixed(2) }}</span>
                <span class="item-subtotal">¥{{ (item.unitPrice * item.quantity).toFixed(2) }}</span>
              </div>
            </div>
          </div>
        </div>
      </template>
    </va-modal>

    <!-- Ship Dialog -->
    <va-modal v-model="showShipDialog" title="发货" size="small">
      <va-input v-model="trackingNumber" label="物流单号" placeholder="请输入物流单号" />
      <template #footer>
        <va-button preset="secondary" @click="showShipDialog = false">取消</va-button>
        <va-button color="primary" @click="confirmShip">确认发货</va-button>
      </template>
    </va-modal>

    <!-- Cancel Dialog -->
    <va-modal v-model="showCancelDialog" title="取消订单" size="small">
      <va-textarea v-model="cancelReason" label="取消原因" placeholder="请输入取消原因（可选）" />
      <template #footer>
        <va-button preset="secondary" @click="showCancelDialog = false">返回</va-button>
        <va-button color="danger" @click="confirmCancel">确认取消</va-button>
      </template>
    </va-modal>
  </div>
</template>

<style scoped>
.admin-orders h1 {
  font-size: 1.5rem;
  margin: 0;
}

.page-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 1.5rem;
}

.header-actions {
  display: flex;
  gap: 1rem;
}

.order-id, .customer-id {
  font-size: 0.85rem;
}

.amount {
  font-weight: 600;
  color: #e53e3e;
}

.action-buttons {
  display: flex;
  gap: 0.5rem;
  flex-wrap: wrap;
}

.order-detail {
  min-width: 500px;
}

.detail-section {
  margin-bottom: 1.5rem;
}

.detail-section h4 {
  font-size: 1rem;
  margin-bottom: 1rem;
  padding-bottom: 0.5rem;
  border-bottom: 1px solid #eee;
}

.detail-grid {
  display: grid;
  grid-template-columns: repeat(2, 1fr);
  gap: 1rem;
}

.detail-item {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
}

.detail-item .label {
  font-size: 0.875rem;
  color: #666;
}

.timeline {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.timeline-item {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.items-list {
  display: flex;
  flex-direction: column;
}

.item-row {
  display: grid;
  grid-template-columns: 1fr auto auto auto;
  gap: 1rem;
  padding: 0.75rem 0;
  border-bottom: 1px solid #eee;
}

.item-qty {
  color: #666;
}

.item-price {
  color: #666;
}

.item-subtotal {
  font-weight: 500;
}
</style>
