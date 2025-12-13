import axios from 'axios'
import type { Order, OrderStats, OrderStatus, OrderCreatedResult, OrderItem } from '../types'

const client = axios.create({
  baseURL: import.meta.env.VITE_API_URL || '/api',
  headers: { 'Content-Type': 'application/json' }
})

export default {
  // Orders
  async getOrders(status?: OrderStatus, limit = 100): Promise<Order[]> {
    const params = new URLSearchParams()
    if (status !== undefined) params.append('status', String(status))
    if (limit) params.append('limit', String(limit))
    const { data } = await client.get(`/orders?${params}`)
    return data
  },

  async getOrder(orderId: string): Promise<Order> {
    const { data } = await client.get(`/orders/${orderId}`)
    return data
  },

  async getCustomerOrders(customerId: string): Promise<Order[]> {
    const { data } = await client.get(`/orders/customer/${customerId}`)
    return data
  },

  async getStats(): Promise<OrderStats> {
    const { data } = await client.get('/orders/stats')
    return data
  },

  // Create
  async createOrder(customerId: string, items: OrderItem[]): Promise<OrderCreatedResult> {
    const { data } = await client.post('/orders', { customerId, items })
    return data
  },

  async createOrderWithFlow(customerId: string, items: OrderItem[]): Promise<OrderCreatedResult> {
    const { data } = await client.post('/orders/flow', { customerId, items })
    return data
  },

  // Lifecycle
  async payOrder(orderId: string, paymentMethod: string, transactionId?: string): Promise<void> {
    await client.post(`/orders/${orderId}/pay`, { paymentMethod, transactionId })
  },

  async processOrder(orderId: string): Promise<void> {
    await client.post(`/orders/${orderId}/process`, {})
  },

  async shipOrder(orderId: string, trackingNumber: string): Promise<void> {
    await client.post(`/orders/${orderId}/ship`, { trackingNumber })
  },

  async deliverOrder(orderId: string): Promise<void> {
    await client.post(`/orders/${orderId}/deliver`, {})
  },

  async cancelOrder(orderId: string, reason?: string): Promise<void> {
    await client.post(`/orders/${orderId}/cancel`, { reason })
  },

  // Health
  async getHealth(): Promise<any> {
    const { data } = await client.get('/health')
    return data
  },

  // System Info
  async getSystemInfo(): Promise<any> {
    const { data } = await client.get('/system/info')
    return data
  }
}
