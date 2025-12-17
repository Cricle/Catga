<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useOrderStore } from '../stores/order'
import type { Order } from '../types'

const store = useOrderStore()
const orders = ref<Order[]>([])
const loading = ref(false)

const statusMap: Record<number, { text: string; color: string }> = {
  0: { text: '待支付', color: 'warning' },
  1: { text: '已支付', color: 'info' },
  2: { text: '处理中', color: 'primary' },
  3: { text: '已发货', color: 'secondary' },
  4: { text: '已完成', color: 'success' },
  5: { text: '已取消', color: 'danger' }
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

onMounted(load)
</script>

<template>
  <div>
    <div class="header">
      <h2>我的订单</h2>
      <va-button preset="secondary" @click="load" :loading="loading">
        <va-icon name="refresh" />
      </va-button>
    </div>

    <va-card v-if="orders.length === 0 && !loading">
      <va-card-content class="text-center pa-6">
        <va-icon name="inbox" size="3rem" color="secondary" />
        <p class="mt-3">暂无订单</p>
        <va-button class="mt-3" @click="$router.push('/')">去购物</va-button>
      </va-card-content>
    </va-card>

    <va-card v-for="order in orders" :key="order.orderId" class="mb-3">
      <va-card-content>
        <div class="order-header">
          <span>订单号: <code>{{ order.orderId }}</code></span>
          <va-chip :color="statusMap[order.status]?.color" size="small">
            {{ statusMap[order.status]?.text }}
          </va-chip>
        </div>

        <div class="items">
          <div v-for="item in order.items" :key="item.productId" class="item">
            <span>{{ item.productName }}</span>
            <span>×{{ item.quantity }}</span>
            <span>¥{{ ((item.unitPrice || 0) * (item.quantity || 0)).toFixed(2) }}</span>
          </div>
        </div>

        <div class="order-footer">
          <span class="total">合计: ¥{{ order.totalAmount?.toFixed(2) }}</span>
          <div class="actions">
            <va-button v-if="order.status === 0" size="small" color="success" @click="pay(order.orderId)">支付</va-button>
            <va-button v-if="order.status < 3" size="small" color="danger" preset="secondary" @click="cancel(order.orderId)">取消</va-button>
          </div>
        </div>
      </va-card-content>
    </va-card>
  </div>
</template>

<style scoped>
.header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 1rem; }
.mb-3 { margin-bottom: 0.75rem; }
.mt-3 { margin-top: 0.75rem; }
.pa-6 { padding: 2rem; }
.text-center { text-align: center; }
.order-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 0.75rem; }
.order-header code { background: #f5f5f5; padding: 0.2rem 0.5rem; border-radius: 4px; }
.items { border-top: 1px solid #eee; border-bottom: 1px solid #eee; padding: 0.5rem 0; }
.item { display: flex; justify-content: space-between; padding: 0.25rem 0; color: #666; }
.order-footer { display: flex; justify-content: space-between; align-items: center; margin-top: 0.75rem; }
.total { font-weight: bold; color: #e53935; }
.actions { display: flex; gap: 0.5rem; }
</style>
