export type OrderStatus = 0 | 1 | 2 | 3 | 4 | 5 | 6 | 7

export interface OrderItem {
  productId: string
  productName: string
  sku?: string
  quantity: number
  unitPrice: number
}

export interface ShippingAddress {
  name: string
  street: string
  city: string
  state: string
  postalCode: string
  country: string
  phone?: string
}

export interface Order {
  orderId: string
  customerId: string
  customerName?: string
  customerEmail?: string
  items: OrderItem[]
  subtotal: number
  tax: number
  shipping: number
  discount: number
  totalAmount: number
  status: OrderStatus
  createdAt: string
  updatedAt?: string
  paidAt?: string
  shippedAt?: string
  deliveredAt?: string
  cancelledAt?: string
  cancellationReason?: string
  trackingNumber?: string
  paymentMethod?: string
  paymentTransactionId?: string
  shippingAddress?: ShippingAddress
  notes?: string
}

export interface OrderStats {
  total: number
  pending: number
  paid: number
  processing: number
  shipped: number
  delivered: number
  cancelled: number
  totalRevenue: number
}

export interface OrderCreatedResult {
  orderId: string
  totalAmount: number
  createdAt: string
}

export const StatusNames: Record<number, string> = {
  0: 'Pending',
  1: 'Paid',
  2: 'Processing',
  3: 'Shipped',
  4: 'Delivered',
  5: 'Cancelled',
  6: 'Refunded',
  7: 'Failed'
}

export const StatusColors: Record<number, string> = {
  0: 'warning',
  1: 'info',
  2: 'secondary',
  3: 'primary',
  4: 'success',
  5: 'danger',
  6: 'danger',
  7: 'danger'
}
