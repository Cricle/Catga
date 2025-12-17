import { defineStore } from 'pinia'
import { ref, computed } from 'vue'

export interface CartItem {
  productId: string
  name: string
  price: number
  quantity: number
  image: string
}

export const useCartStore = defineStore('cart', () => {
  const items = ref<CartItem[]>([])

  const count = computed(() => items.value.reduce((sum, item) => sum + item.quantity, 0))
  const total = computed(() => items.value.reduce((sum, item) => sum + item.price * item.quantity, 0))
  const isEmpty = computed(() => items.value.length === 0)

  function addItem(product: { id: string; name: string; price: number; image: string }) {
    const existing = items.value.find(item => item.productId === product.id)
    if (existing) {
      existing.quantity++
    } else {
      items.value.push({
        productId: product.id,
        name: product.name,
        price: product.price,
        quantity: 1,
        image: product.image
      })
    }
  }

  function removeItem(productId: string) {
    const index = items.value.findIndex(item => item.productId === productId)
    if (index > -1) {
      items.value.splice(index, 1)
    }
  }

  function updateQuantity(productId: string, quantity: number) {
    const item = items.value.find(i => i.productId === productId)
    if (item) {
      if (quantity <= 0) {
        removeItem(productId)
      } else {
        item.quantity = quantity
      }
    }
  }

  function clear() {
    items.value = []
  }

  return {
    items,
    count,
    total,
    isEmpty,
    addItem,
    removeItem,
    updateQuantity,
    clear
  }
})
