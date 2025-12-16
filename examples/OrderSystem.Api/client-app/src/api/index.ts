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
  },

  // ============ Observability ============
  async getObservabilityMetrics(): Promise<any> {
    const { data } = await client.get('/observability/metrics')
    return data
  },

  async recordFlowDemo(flowName: string, durationMs: number): Promise<any> {
    const { data } = await client.post(`/observability/demo/record-flow?flowName=${flowName}&durationMs=${durationMs}`)
    return data
  },

  async recordFailureDemo(flowName: string, error: string): Promise<any> {
    const { data } = await client.post(`/observability/demo/record-failure?flowName=${flowName}&error=${encodeURIComponent(error)}`)
    return data
  },

  // ============ Hot Reload ============
  async getRegisteredFlows(): Promise<any> {
    const { data } = await client.get('/hotreload/flows')
    return data
  },

  async getFlowDetails(flowName: string): Promise<any> {
    const { data } = await client.get(`/hotreload/flows/${flowName}`)
    return data
  },

  async registerFlow(flowName: string): Promise<any> {
    const { data } = await client.post(`/hotreload/flows/${flowName}`)
    return data
  },

  async reloadFlow(flowName: string): Promise<any> {
    const { data } = await client.put(`/hotreload/flows/${flowName}/reload`)
    return data
  },

  async unregisterFlow(flowName: string): Promise<any> {
    const { data } = await client.delete(`/hotreload/flows/${flowName}`)
    return data
  },

  async getReloadEventInfo(): Promise<any> {
    const { data } = await client.get('/hotreload/events/info')
    return data
  },

  // ============ Read Model Sync ============
  async getSyncStatus(): Promise<any> {
    const { data } = await client.get('/readmodelsync/status')
    return data
  },

  async getPendingChanges(): Promise<any> {
    const { data } = await client.get('/readmodelsync/pending')
    return data
  },

  async triggerSync(): Promise<any> {
    const { data } = await client.post('/readmodelsync/sync')
    return data
  },

  async trackChange(entityType: string, entityId: string, changeType: number): Promise<any> {
    const { data } = await client.post(`/readmodelsync/demo/track?entityType=${entityType}&entityId=${entityId}&changeType=${changeType}`)
    return data
  },

  async markChangesSynced(changeIds: string[]): Promise<any> {
    const { data } = await client.post('/readmodelsync/mark-synced', changeIds)
    return data
  },

  async getSyncStrategies(): Promise<any> {
    const { data } = await client.get('/readmodelsync/strategies')
    return data
  }
}
