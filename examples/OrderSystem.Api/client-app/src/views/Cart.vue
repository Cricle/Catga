<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { useCartStore } from '../stores/cart'
import { useOrderStore } from '../stores/order'

const router = useRouter()
const cart = useCartStore()
const orderStore = useOrderStore()
const submitting = ref(false)

const checkout = async () => {
  if (cart.isEmpty) return
  submitting.value = true
  try {
    await orderStore.createOrder({
      customerId: 'CUST-' + Date.now().toString(36),
      items: cart.items.map(i => ({
        productId: i.productId,
        productName: i.name,
        quantity: i.quantity,
        unitPrice: i.price
      }))
    })
    cart.clear()
    router.push('/orders')
  } finally {
    submitting.value = false
  }
}
</script>

<template>
  <div>
    <h2 class="mb-4">购物车</h2>

    <va-card v-if="cart.isEmpty">
      <va-card-content class="text-center pa-6">
        <va-icon name="shopping_cart" size="3rem" color="secondary" />
        <p class="mt-3">购物车是空的</p>
        <va-button class="mt-3" @click="router.push('/')">去购物</va-button>
      </va-card-content>
    </va-card>

    <template v-else>
      <va-card class="mb-4">
        <va-card-content>
          <table class="cart-table">
            <thead>
              <tr><th>商品</th><th>单价</th><th>数量</th><th>小计</th><th></th></tr>
            </thead>
            <tbody>
              <tr v-for="item in cart.items" :key="item.productId">
                <td>{{ item.name }}</td>
                <td>¥{{ item.price }}</td>
                <td>
                  <va-button size="small" preset="secondary" @click="cart.updateQuantity(item.productId, item.quantity - 1)">-</va-button>
                  <span class="qty">{{ item.quantity }}</span>
                  <va-button size="small" preset="secondary" @click="cart.updateQuantity(item.productId, item.quantity + 1)">+</va-button>
                </td>
                <td class="price">¥{{ (item.price * item.quantity).toFixed(2) }}</td>
                <td><va-button size="small" color="danger" preset="plain" @click="cart.removeItem(item.productId)"><va-icon name="delete" /></va-button></td>
              </tr>
            </tbody>
          </table>
        </va-card-content>
      </va-card>

      <va-card>
        <va-card-content>
          <div class="summary">
            <span>共 {{ cart.count }} 件商品</span>
            <span class="total">合计: <strong>¥{{ cart.total.toFixed(2) }}</strong></span>
          </div>
          <va-button color="primary" block :loading="submitting" @click="checkout">提交订单</va-button>
        </va-card-content>
      </va-card>
    </template>
  </div>
</template>

<style scoped>
.mb-4 { margin-bottom: 1rem; }
.mt-3 { margin-top: 0.75rem; }
.pa-6 { padding: 2rem; }
.text-center { text-align: center; }
.cart-table { width: 100%; border-collapse: collapse; }
.cart-table th, .cart-table td { padding: 0.75rem; text-align: left; border-bottom: 1px solid #eee; }
.qty { display: inline-block; min-width: 2rem; text-align: center; }
.price { color: #e53935; font-weight: bold; }
.summary { display: flex; justify-content: space-between; margin-bottom: 1rem; }
.total strong { font-size: 1.2rem; color: #e53935; }
</style>
