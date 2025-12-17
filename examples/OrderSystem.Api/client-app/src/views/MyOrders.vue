<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useOrderStore } from '../stores/order'
import type { Order, OrderStatus } from '../types'

const store = useOrderStore()
const orders = ref<Order[]>([])
const loading = ref(false)
const selectedOrder = ref<Order | null>(null)
const showDetail = ref(false)

const statusConfig: Record<OrderStatus, { text: string; color: string; icon: string }> = {
  0: { text: '待支付', color: 'warning', icon: 'schedule' },
  1: { text: '已支付', color: 'info', icon: 'paid' },
  2: { text: '处理中', color: 'primary', icon: 'inventory' },
  3: { text: '已发货', color: 'secondary', icon: 'local_shipping' },
  4: { text: '已完成', color: 'success', icon: 'check_circle' },
  5: { text: '已取消', color: 'danger', icon: 'cancel' }
}

const loadOrders = async () => {
  loading.value = true
  try {
    orders.value = await store.fetchOrders()
  } finally {
    loading.value = false
  }
}

const viewOrder = (order: Order) => {
  selectedOrder.value = order
  showDetail.value = true
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
  await store.deliverOrder(orderId)
  await loadOrders()
  if (selectedOrder.value?.orderId === orderId) {
    selectedOrder.value = orders.value.find(o => o.orderId === orderId) || null
  }
}

const formatDate = (date?: string | Date) => date ? new Date(date).toLocaleString('zh-CN') : '-'

onMounted(loadOrders)
</script>

<template>
  <div>
    <va-card class="mb-4">
      <va-card-title>
        <va-icon name="receipt_long" class="mr-2" />我的订单
        <va-button size="small" preset="secondary" class="ml-auto" @click="loadOrders" :loading="loading">
          <va-icon name="refresh" />
        </va-button>
      </va-card-title>
    </va-card>

    <va-card v-if="orders.length === 0 && !loading" class="text-center">
      <va-card-content class="pa-6">
        <va-icon name="inbox" size="4rem" color="secondary" />
        <h3 class="va-h5 mt-4">暂无订单</h3>
        <p class="text-secondary mb-4">快去选购商品吧！</p>
        <va-button @click="$router.push('/')">
          <va-icon name="store" class="mr-2" />去购物
        </va-button>
      </va-card-content>
    </va-card>

    <va-inner-loading :loading="loading">
      <va-card v-for="order in orders" :key="order.orderId" class="mb-3">
        <va-card-content>
          <div class="d-flex justify-space-between align-center mb-3">
            <div>
              <span class="text-secondary mr-2">订单号:</span>
              <code>{{ order.orderId }}</code>
            </div>
            <va-chip :color="statusConfig[order.status]?.color" size="small">
              <va-icon :name="statusConfig[order.status]?.icon" size="small" class="mr-1" />
              {{ statusConfig[order.status]?.text }}
            </va-chip>
          </div>

          <va-list>
            <va-list-item v-for="item in order.items?.slice(0, 2)" :key="item.productId">
              <va-list-item-section avatar>
                <va-avatar color="background-element" size="small">
                  <va-icon name="inventory_2" color="primary" size="small" />
                </va-avatar>
              </va-list-item-section>
              <va-list-item-section>
                <va-list-item-label>{{ item.productName }}</va-list-item-label>
                <va-list-item-label caption>¥{{ item.unitPrice?.toFixed(2) }} × {{ item.quantity }}</va-list-item-label>
              </va-list-item-section>
              <va-list-item-section side>
                ¥{{ ((item.unitPrice || 0) * (item.quantity || 0)).toFixed(2) }}
              </va-list-item-section>
            </va-list-item>
            <va-list-item v-if="(order.items?.length || 0) > 2">
              <va-list-item-section>
                <va-list-item-label caption>...还有 {{ (order.items?.length || 0) - 2 }} 件商品</va-list-item-label>
              </va-list-item-section>
            </va-list-item>
          </va-list>

          <va-divider class="my-3" />

          <div class="d-flex justify-space-between align-center">
            <div>
              <span class="text-secondary">下单时间: {{ formatDate(order.createdAt) }}</span>
            </div>
            <div class="d-flex align-center">
              <span class="mr-3">合计: <strong class="va-text-danger va-h6">¥{{ order.totalAmount?.toFixed(2) }}</strong></span>
              <va-button size="small" preset="secondary" class="mr-2" @click="viewOrder(order)">
                <va-icon name="visibility" class="mr-1" />详情
              </va-button>
              <va-button v-if="order.status === 0" size="small" color="success" class="mr-2" @click="payOrder(order.orderId)">
                <va-icon name="payment" class="mr-1" />支付
              </va-button>
              <va-button v-if="order.status === 3" size="small" color="primary" class="mr-2" @click="confirmReceipt(order.orderId)">
                <va-icon name="check" class="mr-1" />确认收货
              </va-button>
              <va-button v-if="order.status < 3" size="small" preset="secondary" color="danger" @click="cancelOrder(order.orderId)">
                <va-icon name="close" class="mr-1" />取消
              </va-button>
            </div>
          </div>
        </va-card-content>
      </va-card>
    </va-inner-loading>

    <va-modal v-model="showDetail" title="订单详情" size="large" hide-default-actions>
      <template v-if="selectedOrder">
        <va-card flat>
          <va-card-content>
            <div class="d-flex justify-space-between align-center mb-4">
              <div>
                <span class="text-secondary">订单号: </span>
                <code>{{ selectedOrder.orderId }}</code>
              </div>
              <va-chip :color="statusConfig[selectedOrder.status]?.color">
                <va-icon :name="statusConfig[selectedOrder.status]?.icon" size="small" class="mr-1" />
                {{ statusConfig[selectedOrder.status]?.text }}
              </va-chip>
            </div>

            <va-timeline>
              <va-timeline-item color="success" v-if="selectedOrder.createdAt">
                <template #before>{{ formatDate(selectedOrder.createdAt) }}</template>
                <va-icon name="add_shopping_cart" class="mr-2" />订单创建
              </va-timeline-item>
              <va-timeline-item color="info" v-if="selectedOrder.paidAt">
                <template #before>{{ formatDate(selectedOrder.paidAt) }}</template>
                <va-icon name="paid" class="mr-2" />支付成功
              </va-timeline-item>
              <va-timeline-item color="primary" v-if="selectedOrder.status >= 2 && selectedOrder.status !== 5">
                <template #before>处理中</template>
                <va-icon name="inventory" class="mr-2" />商家处理
              </va-timeline-item>
              <va-timeline-item color="secondary" v-if="selectedOrder.shippedAt">
                <template #before>{{ formatDate(selectedOrder.shippedAt) }}</template>
                <va-icon name="local_shipping" class="mr-2" />已发货
                <span v-if="selectedOrder.trackingNumber" class="ml-2">
                  物流单号: <code>{{ selectedOrder.trackingNumber }}</code>
                </span>
              </va-timeline-item>
              <va-timeline-item color="success" v-if="selectedOrder.deliveredAt">
                <template #before>{{ formatDate(selectedOrder.deliveredAt) }}</template>
                <va-icon name="check_circle" class="mr-2" />已完成
              </va-timeline-item>
              <va-timeline-item color="danger" v-if="selectedOrder.cancelledAt">
                <template #before>{{ formatDate(selectedOrder.cancelledAt) }}</template>
                <va-icon name="cancel" class="mr-2" />已取消
                <span v-if="selectedOrder.cancellationReason" class="ml-2 text-secondary">
                  原因: {{ selectedOrder.cancellationReason }}
                </span>
              </va-timeline-item>
            </va-timeline>

            <va-divider class="my-4" />

            <h4 class="va-h6 mb-3">商品列表</h4>
            <va-list>
              <va-list-item v-for="item in selectedOrder.items" :key="item.productId">
                <va-list-item-section avatar>
                  <va-avatar color="background-element">
                    <va-icon name="inventory_2" color="primary" />
                  </va-avatar>
                </va-list-item-section>
                <va-list-item-section>
                  <va-list-item-label>{{ item.productName }}</va-list-item-label>
                  <va-list-item-label caption>¥{{ item.unitPrice?.toFixed(2) }} × {{ item.quantity }}</va-list-item-label>
                </va-list-item-section>
                <va-list-item-section side>
                  <strong>¥{{ ((item.unitPrice || 0) * (item.quantity || 0)).toFixed(2) }}</strong>
                </va-list-item-section>
              </va-list-item>
            </va-list>

            <va-divider class="my-4" />

            <div class="d-flex justify-space-between">
              <span class="va-h6">订单总额</span>
              <span class="va-text-danger va-h5">¥{{ selectedOrder.totalAmount?.toFixed(2) }}</span>
            </div>
          </va-card-content>
        </va-card>
      </template>
      <template #footer>
        <va-button v-if="selectedOrder?.status === 0" color="success" class="mr-2" @click="payOrder(selectedOrder.orderId); showDetail = false">
          <va-icon name="payment" class="mr-1" />立即支付
        </va-button>
        <va-button v-if="selectedOrder?.status === 3" color="primary" class="mr-2" @click="confirmReceipt(selectedOrder.orderId); showDetail = false">
          <va-icon name="check" class="mr-1" />确认收货
        </va-button>
        <va-button preset="secondary" @click="showDetail = false">关闭</va-button>
      </template>
    </va-modal>
  </div>
</template>
