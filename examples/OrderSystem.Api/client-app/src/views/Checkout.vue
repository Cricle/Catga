<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { useCartStore } from '../stores/cart'
import { useOrderStore } from '../stores/order'

const router = useRouter()
const cart = useCartStore()
const orderStore = useOrderStore()

const isSubmitting = ref(false)
const paymentMethod = ref('alipay')
const orderSuccess = ref(false)
const createdOrderId = ref('')

const paymentOptions = [
  { value: 'alipay', text: '支付宝' },
  { value: 'wechat', text: '微信支付' },
  { value: 'card', text: '银行卡' }
]

const submitOrder = async () => {
  if (cart.isEmpty) return

  isSubmitting.value = true
  try {
    const result = await orderStore.createOrder({
      customerId: 'USER-' + Date.now().toString(36),
      items: cart.items.map(item => ({
        productId: item.productId,
        productName: item.name,
        quantity: item.quantity,
        unitPrice: item.price
      }))
    })

    createdOrderId.value = result.orderId
    orderSuccess.value = true
    cart.clear()
  } catch (e) {
    console.error('Order failed:', e)
  } finally {
    isSubmitting.value = false
  }
}
</script>

<template>
  <div>
    <va-card class="mb-4">
      <va-card-title>
        <va-icon name="payment" class="mr-2" />结算
      </va-card-title>
    </va-card>

    <va-card v-if="orderSuccess" class="text-center">
      <va-card-content class="pa-6">
        <va-icon name="check_circle" size="4rem" color="success" />
        <h2 class="va-h4 mt-4">订单提交成功！</h2>
        <p class="text-secondary mb-4">订单号: {{ createdOrderId }}</p>
        <va-button color="primary" class="mr-2" @click="router.push('/my-orders')">
          <va-icon name="receipt_long" class="mr-2" />查看订单
        </va-button>
        <va-button preset="secondary" @click="router.push('/')">
          <va-icon name="store" class="mr-2" />继续购物
        </va-button>
      </va-card-content>
    </va-card>

    <va-card v-else-if="cart.isEmpty" class="text-center">
      <va-card-content class="pa-6">
        <va-icon name="remove_shopping_cart" size="4rem" color="secondary" />
        <h3 class="va-h5 mt-4">购物车是空的</h3>
        <va-button class="mt-4" @click="router.push('/')">
          <va-icon name="store" class="mr-2" />去购物
        </va-button>
      </va-card-content>
    </va-card>

    <div v-else class="row">
      <div class="flex md8 xs12">
        <va-card class="mb-4">
          <va-card-title>订单商品 ({{ cart.count }} 件)</va-card-title>
          <va-card-content>
            <va-list>
              <va-list-item v-for="item in cart.items" :key="item.productId">
                <va-list-item-section avatar>
                  <va-avatar color="background-element">
                    <va-icon :name="item.image" color="primary" />
                  </va-avatar>
                </va-list-item-section>
                <va-list-item-section>
                  <va-list-item-label>{{ item.name }}</va-list-item-label>
                  <va-list-item-label caption>¥{{ item.price.toFixed(2) }} × {{ item.quantity }}</va-list-item-label>
                </va-list-item-section>
                <va-list-item-section side>
                  <strong class="va-text-danger">¥{{ (item.price * item.quantity).toFixed(2) }}</strong>
                </va-list-item-section>
              </va-list-item>
            </va-list>
          </va-card-content>
        </va-card>

        <va-card class="mb-4">
          <va-card-title>支付方式</va-card-title>
          <va-card-content>
            <va-radio
              v-for="opt in paymentOptions"
              :key="opt.value"
              v-model="paymentMethod"
              :option="opt.value"
              :label="opt.text"
              class="mb-2"
            />
          </va-card-content>
        </va-card>
      </div>

      <div class="flex md4 xs12">
        <va-card>
          <va-card-title>订单摘要</va-card-title>
          <va-card-content>
            <div class="d-flex justify-space-between mb-2">
              <span>商品金额</span>
              <span>¥{{ cart.total.toFixed(2) }}</span>
            </div>
            <div class="d-flex justify-space-between mb-2">
              <span>运费</span>
              <span class="va-text-success">免运费</span>
            </div>
            <va-divider class="my-3" />
            <div class="d-flex justify-space-between mb-4">
              <strong>应付金额</strong>
              <strong class="va-text-danger va-h5">¥{{ cart.total.toFixed(2) }}</strong>
            </div>
            <va-button color="primary" block :loading="isSubmitting" @click="submitOrder">
              <va-icon name="check" class="mr-2" />提交订单
            </va-button>
            <va-button preset="secondary" block class="mt-2" @click="router.push('/cart')">
              <va-icon name="arrow_back" class="mr-2" />返回购物车
            </va-button>
          </va-card-content>
        </va-card>
      </div>
    </div>
  </div>
</template>
