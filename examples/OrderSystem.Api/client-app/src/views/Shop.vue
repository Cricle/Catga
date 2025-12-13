<script setup lang="ts">
import { ref, computed } from 'vue'
import { useRouter } from 'vue-router'
import { useOrderStore } from '../stores/order'

const router = useRouter()
const store = useOrderStore()

// Mock product data
const products = ref([
  { id: 'LAPTOP-001', name: '笔记本电脑', desc: '高性能轻薄本', price: 5999.00, image: 'laptop', stock: 50 },
  { id: 'PHONE-001', name: '智能手机', desc: '旗舰5G手机', price: 3999.00, image: 'smartphone', stock: 100 },
  { id: 'TABLET-001', name: '平板电脑', desc: '10寸高清屏', price: 2499.00, image: 'tablet', stock: 30 },
  { id: 'WATCH-001', name: '智能手表', desc: '健康监测', price: 1299.00, image: 'watch', stock: 80 },
  { id: 'EARPHONE-001', name: '无线耳机', desc: '主动降噪', price: 899.00, image: 'headphones', stock: 200 },
  { id: 'KEYBOARD-001', name: '机械键盘', desc: '青轴RGB', price: 399.00, image: 'keyboard', stock: 150 },
])

// Cart state
const cart = ref<{ productId: string; name: string; price: number; quantity: number }[]>([])

const cartTotal = computed(() => cart.value.reduce((sum, item) => sum + item.price * item.quantity, 0))
const cartCount = computed(() => cart.value.reduce((sum, item) => sum + item.quantity, 0))

const addToCart = (product: typeof products.value[0]) => {
  const existing = cart.value.find(item => item.productId === product.id)
  if (existing) {
    existing.quantity++
  } else {
    cart.value.push({
      productId: product.id,
      name: product.name,
      price: product.price,
      quantity: 1
    })
  }
}

const removeFromCart = (productId: string) => {
  const index = cart.value.findIndex(item => item.productId === productId)
  if (index > -1) {
    cart.value.splice(index, 1)
  }
}

const updateQuantity = (productId: string, delta: number) => {
  const item = cart.value.find(i => i.productId === productId)
  if (item) {
    item.quantity = Math.max(1, item.quantity + delta)
  }
}

const isSubmitting = ref(false)
const showCart = ref(false)

const checkout = async () => {
  if (cart.value.length === 0) return

  isSubmitting.value = true
  try {
    const order = await store.createOrder({
      customerId: 'USER-' + Date.now().toString(36),
      items: cart.value.map(item => ({
        productId: item.productId,
        productName: item.name,
        quantity: item.quantity,
        unitPrice: item.price
      }))
    })

    cart.value = []
    showCart.value = false
    router.push(`/my-orders/${order.orderId}`)
  } catch (e) {
    console.error('Checkout failed:', e)
  } finally {
    isSubmitting.value = false
  }
}
</script>

<template>
  <div class="shop-page">
    <!-- Header -->
    <div class="shop-header">
      <h1>商品列表</h1>
      <va-button @click="showCart = true" color="primary">
        <va-icon name="shopping_cart" class="mr-2" />
        购物车 ({{ cartCount }})
      </va-button>
    </div>

    <!-- Product Grid -->
    <div class="product-grid">
      <va-card v-for="product in products" :key="product.id" class="product-card">
        <div class="product-image">
          <va-icon :name="product.image" size="4rem" color="primary" />
        </div>
        <va-card-content>
          <h3 class="product-name">{{ product.name }}</h3>
          <p class="product-desc">{{ product.desc }}</p>
          <div class="product-footer">
            <span class="product-price">¥{{ product.price.toFixed(2) }}</span>
            <va-button size="small" @click="addToCart(product)">
              <va-icon name="add_shopping_cart" />
            </va-button>
          </div>
        </va-card-content>
      </va-card>
    </div>

    <!-- Cart Modal -->
    <va-modal v-model="showCart" title="购物车" size="large">
      <div v-if="cart.length === 0" class="empty-cart">
        <va-icon name="remove_shopping_cart" size="3rem" color="secondary" />
        <p>购物车是空的</p>
      </div>

      <div v-else class="cart-items">
        <div v-for="item in cart" :key="item.productId" class="cart-item">
          <div class="cart-item-info">
            <span class="cart-item-name">{{ item.name }}</span>
            <span class="cart-item-price">¥{{ item.price.toFixed(2) }}</span>
          </div>
          <div class="cart-item-actions">
            <va-button size="small" preset="secondary" @click="updateQuantity(item.productId, -1)">-</va-button>
            <span class="cart-item-quantity">{{ item.quantity }}</span>
            <va-button size="small" preset="secondary" @click="updateQuantity(item.productId, 1)">+</va-button>
            <va-button size="small" color="danger" preset="secondary" @click="removeFromCart(item.productId)">
              <va-icon name="delete" />
            </va-button>
          </div>
          <div class="cart-item-subtotal">
            ¥{{ (item.price * item.quantity).toFixed(2) }}
          </div>
        </div>

        <va-divider />

        <div class="cart-total">
          <span>合计:</span>
          <span class="total-amount">¥{{ cartTotal.toFixed(2) }}</span>
        </div>
      </div>

      <template #footer>
        <va-button preset="secondary" @click="showCart = false">继续购物</va-button>
        <va-button
          color="primary"
          :disabled="cart.length === 0"
          :loading="isSubmitting"
          @click="checkout"
        >
          立即结算
        </va-button>
      </template>
    </va-modal>
  </div>
</template>

<style scoped>
.shop-page {
  max-width: 1200px;
  margin: 0 auto;
}

.shop-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 2rem;
}

.shop-header h1 {
  font-size: 1.5rem;
  font-weight: 600;
  margin: 0;
}

.product-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(250px, 1fr));
  gap: 1.5rem;
}

.product-card {
  transition: transform 0.2s, box-shadow 0.2s;
}

.product-card:hover {
  transform: translateY(-4px);
  box-shadow: 0 8px 25px rgba(0,0,0,0.1);
}

.product-image {
  display: flex;
  justify-content: center;
  align-items: center;
  padding: 2rem;
  background: linear-gradient(135deg, #f5f7fa 0%, #e4e8eb 100%);
}

.product-name {
  font-size: 1.1rem;
  font-weight: 600;
  margin: 0 0 0.5rem;
}

.product-desc {
  color: #666;
  font-size: 0.9rem;
  margin: 0 0 1rem;
}

.product-footer {
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.product-price {
  font-size: 1.25rem;
  font-weight: 700;
  color: #e53e3e;
}

.empty-cart {
  text-align: center;
  padding: 3rem;
  color: #666;
}

.cart-items {
  min-width: 400px;
}

.cart-item {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 1rem 0;
  border-bottom: 1px solid #eee;
}

.cart-item-info {
  flex: 1;
}

.cart-item-name {
  display: block;
  font-weight: 500;
}

.cart-item-price {
  color: #666;
  font-size: 0.9rem;
}

.cart-item-actions {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.cart-item-quantity {
  min-width: 2rem;
  text-align: center;
}

.cart-item-subtotal {
  min-width: 100px;
  text-align: right;
  font-weight: 500;
}

.cart-total {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 1rem 0;
  font-size: 1.1rem;
}

.total-amount {
  font-size: 1.5rem;
  font-weight: 700;
  color: #e53e3e;
}

.mr-2 {
  margin-right: 0.5rem;
}
</style>
