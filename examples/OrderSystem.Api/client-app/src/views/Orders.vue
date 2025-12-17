<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { useOrderStore } from '../stores/order'
import type { Order } from '../types'

const router = useRouter()
const store = useOrderStore()
const orders = ref<Order[]>([])
const loading = ref(false)

const statusMap: Record<number, { text: string; color: string; bg: string }> = {
  0: { text: '待支付', color: '#f59e0b', bg: '#fef3c7' },
  1: { text: '已支付', color: '#3b82f6', bg: '#dbeafe' },
  2: { text: '处理中', color: '#8b5cf6', bg: '#ede9fe' },
  3: { text: '已发货', color: '#06b6d4', bg: '#cffafe' },
  4: { text: '已完成', color: '#22c55e', bg: '#dcfce7' },
  5: { text: '已取消', color: '#ef4444', bg: '#fee2e2' }
}

const load = async () => {
  loading.value = true
  try {
    orders.value = await store.fetchOrders()
  } finally {
    loading.value = false
  }
}

const pay = async (id: string) => {
  await store.payOrder(id, 'Alipay', 'TX-' + Date.now())
  await load()
}

const cancel = async (id: string) => {
  await store.cancelOrder(id, '用户取消')
  await load()
}

const formatDate = (d: string) => new Date(d).toLocaleString('zh-CN')

onMounted(load)
</script>

<template>
  <div class="orders">
    <div class="orders-header">
      <div>
        <h1>我的订单</h1>
        <p>共 {{ orders.length }} 个订单</p>
      </div>
      <button class="refresh-btn" @click="load" :disabled="loading">
        <va-icon :name="loading ? 'sync' : 'refresh'" :class="{ spin: loading }" />
      </button>
    </div>

    <div v-if="orders.length === 0 && !loading" class="empty">
      <va-icon name="inbox" size="4rem" color="secondary" />
      <h2>暂无订单</h2>
      <p>快去选购心仪的商品吧</p>
      <button class="primary-btn" @click="router.push('/')">
        <va-icon name="store" />
        <span>去购物</span>
      </button>
    </div>

    <div v-else class="order-list">
      <div v-for="order in orders" :key="order.orderId" class="order-card">
        <div class="order-header">
          <div class="order-id">
            <span>订单号</span>
            <code>{{ order.orderId }}</code>
          </div>
          <span class="status" :style="{ color: statusMap[order.status]?.color, background: statusMap[order.status]?.bg }">
            {{ statusMap[order.status]?.text }}
          </span>
        </div>

        <div class="order-items">
          <div v-for="item in order.items" :key="item.productId" class="order-item">
            <span class="item-name">{{ item.productName }}</span>
            <span class="item-qty">×{{ item.quantity }}</span>
            <span class="item-price">¥{{ ((item.unitPrice || 0) * (item.quantity || 0)).toLocaleString() }}</span>
          </div>
        </div>

        <div class="order-footer">
          <div class="order-info">
            <span>下单时间: {{ formatDate(order.createdAt) }}</span>
          </div>
          <div class="order-actions">
            <span class="order-total">合计: <strong>¥{{ order.totalAmount?.toLocaleString() }}</strong></span>
            <button v-if="order.status === 0" class="pay-btn" @click="pay(order.orderId)">立即支付</button>
            <button v-if="order.status < 3" class="cancel-btn" @click="cancel(order.orderId)">取消订单</button>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.orders-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  margin-bottom: 1.5rem;
}

.orders-header h1 {
  font-size: 1.5rem;
  font-weight: 700;
  margin-bottom: 0.25rem;
}

.orders-header p {
  color: var(--text-secondary);
}

.refresh-btn {
  width: 40px;
  height: 40px;
  border: 1px solid var(--border);
  background: var(--card);
  border-radius: 8px;
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
}

.refresh-btn:disabled { opacity: 0.5; }

.empty {
  text-align: center;
  padding: 3rem 1rem;
  background: var(--card);
  border-radius: 12px;
}

.empty h2 {
  margin: 1rem 0 0.5rem;
  font-size: 1.25rem;
}

.empty p {
  color: var(--text-secondary);
  margin-bottom: 1.5rem;
}

.primary-btn {
  display: inline-flex;
  align-items: center;
  gap: 0.5rem;
  padding: 0.75rem 1.5rem;
  background: var(--primary);
  color: white;
  border: none;
  border-radius: 8px;
  cursor: pointer;
  font-size: 1rem;
  font-weight: 500;
}

.order-list {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.order-card {
  background: var(--card);
  border-radius: 12px;
  overflow: hidden;
}

.order-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 1rem;
  border-bottom: 1px solid var(--border);
}

.order-id {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  color: var(--text-secondary);
  font-size: 0.875rem;
}

.order-id code {
  background: var(--bg);
  padding: 0.25rem 0.5rem;
  border-radius: 4px;
  font-family: monospace;
  color: var(--text);
}

.status {
  padding: 0.25rem 0.75rem;
  border-radius: 20px;
  font-size: 0.75rem;
  font-weight: 600;
}

.order-items {
  padding: 1rem;
}

.order-item {
  display: flex;
  align-items: center;
  padding: 0.5rem 0;
}

.order-item:not(:last-child) {
  border-bottom: 1px dashed var(--border);
}

.item-name {
  flex: 1;
}

.item-qty {
  color: var(--text-secondary);
  margin: 0 1rem;
}

.item-price {
  font-weight: 500;
  min-width: 80px;
  text-align: right;
}

.order-footer {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 1rem;
  background: var(--bg);
  flex-wrap: wrap;
  gap: 1rem;
}

.order-info {
  color: var(--text-secondary);
  font-size: 0.875rem;
}

.order-actions {
  display: flex;
  align-items: center;
  gap: 1rem;
}

.order-total strong {
  color: var(--danger);
  font-size: 1.1rem;
}

.pay-btn, .cancel-btn {
  padding: 0.5rem 1rem;
  border-radius: 6px;
  cursor: pointer;
  font-size: 0.875rem;
  font-weight: 500;
  border: none;
}

.pay-btn {
  background: var(--success);
  color: white;
}

.cancel-btn {
  background: transparent;
  border: 1px solid var(--border);
  color: var(--text-secondary);
}

.cancel-btn:hover {
  border-color: var(--danger);
  color: var(--danger);
}

.spin { animation: spin 1s linear infinite; }
@keyframes spin { to { transform: rotate(360deg); } }

@media (max-width: 640px) {
  .order-footer {
    flex-direction: column;
    align-items: stretch;
  }

  .order-actions {
    flex-direction: column;
    align-items: stretch;
  }

  .pay-btn, .cancel-btn {
    width: 100%;
    padding: 0.75rem;
  }
}
</style>
