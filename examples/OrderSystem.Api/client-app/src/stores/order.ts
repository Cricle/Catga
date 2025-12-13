import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import api from '../api'
import type { Order, OrderStats, OrderStatus } from '../types'

export const useOrderStore = defineStore('order', () => {
  const orders = ref<Order[]>([])
  const stats = ref<OrderStats | null>(null)
  const currentOrder = ref<Order | null>(null)
  const loading = ref(false)
  const error = ref<string | null>(null)

  const pendingOrders = computed(() => orders.value.filter(o => o.status === 0))
  const activeOrders = computed(() => orders.value.filter(o => o.status >= 1 && o.status <= 3))

  async function fetchOrders(limit = 100, status?: OrderStatus): Promise<Order[]> {
    loading.value = true
    error.value = null
    try {
      orders.value = await api.getOrders(status, limit)
      return orders.value
    } catch (e: any) {
      error.value = e.message
      return []
    } finally {
      loading.value = false
    }
  }

  async function fetchStats(): Promise<OrderStats | null> {
    try {
      stats.value = await api.getStats()
      return stats.value
    } catch (e: any) {
      console.error('Failed to fetch stats:', e)
      return null
    }
  }

  async function fetchOrder(orderId: string) {
    loading.value = true
    try {
      currentOrder.value = await api.getOrder(orderId)
    } catch (e: any) {
      error.value = e.message
    } finally {
      loading.value = false
    }
  }

  async function createOrder(data: { customerId: string; items: any[] }) {
    loading.value = true
    try {
      const result = await api.createOrder(data.customerId, data.items)
      await fetchOrders()
      await fetchStats()
      return result
    } catch (e: any) {
      error.value = e.message
      throw e
    } finally {
      loading.value = false
    }
  }

  async function createOrderWithFlow(customerId: string, items: any[]) {
    loading.value = true
    try {
      const result = await api.createOrderWithFlow(customerId, items)
      await fetchOrders()
      await fetchStats()
      return result
    } catch (e: any) {
      error.value = e.message
      throw e
    } finally {
      loading.value = false
    }
  }

  async function payOrder(orderId: string, paymentMethod: string, transactionId?: string) {
    await api.payOrder(orderId, paymentMethod, transactionId)
    await fetchOrder(orderId)
    await fetchStats()
  }

  async function processOrder(orderId: string) {
    await api.processOrder(orderId)
    await fetchOrder(orderId)
    await fetchStats()
  }

  async function shipOrder(orderId: string, trackingNumber: string) {
    await api.shipOrder(orderId, trackingNumber)
    await fetchOrder(orderId)
    await fetchStats()
  }

  async function deliverOrder(orderId: string) {
    await api.deliverOrder(orderId)
    await fetchOrder(orderId)
    await fetchStats()
  }

  async function cancelOrder(orderId: string, reason?: string) {
    await api.cancelOrder(orderId, reason)
    await fetchOrder(orderId)
    await fetchStats()
  }

  return {
    orders, stats, currentOrder, loading, error,
    pendingOrders, activeOrders,
    fetchOrders, fetchStats, fetchOrder,
    createOrder, createOrderWithFlow,
    payOrder, processOrder, shipOrder, deliverOrder, cancelOrder
  }
})
