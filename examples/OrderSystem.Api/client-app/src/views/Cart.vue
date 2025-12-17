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
  <div class="cart">
    <div class="cart-header">
      <h1>购物车</h1>
      <p v-if="!cart.isEmpty">共 {{ cart.count }} 件商品</p>
    </div>

    <div v-if="cart.isEmpty" class="empty">
      <va-icon name="shopping_cart" size="4rem" color="secondary" />
      <h2>购物车是空的</h2>
      <p>快去选购心仪的商品吧</p>
      <button class="primary-btn" @click="router.push('/')">
        <va-icon name="store" />
        <span>去购物</span>
      </button>
    </div>

    <template v-else>
      <div class="cart-content">
        <div class="cart-items">
          <div v-for="item in cart.items" :key="item.productId" class="cart-item">
            <div class="item-icon">
              <va-icon :name="item.image" size="1.5rem" color="primary" />
            </div>
            <div class="item-info">
              <h4>{{ item.name }}</h4>
              <p>¥{{ item.price.toLocaleString() }}</p>
            </div>
            <div class="item-qty">
              <button class="qty-btn" @click="cart.updateQuantity(item.productId, item.quantity - 1)">−</button>
              <span>{{ item.quantity }}</span>
              <button class="qty-btn" @click="cart.updateQuantity(item.productId, item.quantity + 1)">+</button>
            </div>
            <div class="item-subtotal">
              ¥{{ (item.price * item.quantity).toLocaleString() }}
            </div>
            <button class="delete-btn" @click="cart.removeItem(item.productId)">
              <va-icon name="delete" />
            </button>
          </div>
        </div>

        <div class="cart-summary">
          <h3>订单摘要</h3>
          <div class="summary-row">
            <span>商品数量</span>
            <span>{{ cart.count }} 件</span>
          </div>
          <div class="summary-row">
            <span>商品金额</span>
            <span>¥{{ cart.total.toLocaleString() }}</span>
          </div>
          <div class="summary-row">
            <span>运费</span>
            <span class="free">免运费</span>
          </div>
          <div class="summary-total">
            <span>合计</span>
            <span class="total-price">¥{{ cart.total.toLocaleString() }}</span>
          </div>
          <button class="checkout-btn" :disabled="submitting" @click="checkout">
            <va-icon v-if="submitting" name="sync" class="spin" />
            <span>{{ submitting ? '提交中...' : '提交订单' }}</span>
          </button>
        </div>
      </div>
    </template>
  </div>
</template>

<style scoped>
.cart-header {
  margin-bottom: 1.5rem;
}

.cart-header h1 {
  font-size: 1.5rem;
  font-weight: 700;
  margin-bottom: 0.25rem;
}

.cart-header p {
  color: var(--text-secondary);
}

.empty {
  text-align: center;
  padding: 3rem 1rem;
  background: var(--card);
  border-radius: 12px;
}

.empty h2 {
  margin: 1rem 0 0.5rem;
  font-size: 1.25rem;
}

.empty p {
  color: var(--text-secondary);
  margin-bottom: 1.5rem;
}

.primary-btn {
  display: inline-flex;
  align-items: center;
  gap: 0.5rem;
  padding: 0.75rem 1.5rem;
  background: var(--primary);
  color: white;
  border: none;
  border-radius: 8px;
  cursor: pointer;
  font-size: 1rem;
  font-weight: 500;
}

.cart-content {
  display: grid;
  grid-template-columns: 1fr 320px;
  gap: 1.5rem;
}

.cart-items {
  background: var(--card);
  border-radius: 12px;
  overflow: hidden;
}

.cart-item {
  display: flex;
  align-items: center;
  gap: 1rem;
  padding: 1rem;
  border-bottom: 1px solid var(--border);
}

.cart-item:last-child { border-bottom: none; }

.item-icon {
  width: 48px;
  height: 48px;
  background: var(--bg);
  border-radius: 8px;
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
}

.item-info {
  flex: 1;
  min-width: 0;
}

.item-info h4 {
  font-weight: 600;
  margin-bottom: 0.25rem;
}

.item-info p {
  color: var(--text-secondary);
  font-size: 0.875rem;
}

.item-qty {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.qty-btn {
  width: 28px;
  height: 28px;
  border: 1px solid var(--border);
  background: var(--card);
  border-radius: 6px;
  cursor: pointer;
  font-size: 1rem;
}

.item-subtotal {
  font-weight: 600;
  color: var(--danger);
  min-width: 80px;
  text-align: right;
}

.delete-btn {
  background: none;
  border: none;
  cursor: pointer;
  padding: 0.5rem;
  color: var(--text-secondary);
}

.delete-btn:hover { color: var(--danger); }

.cart-summary {
  background: var(--card);
  border-radius: 12px;
  padding: 1.5rem;
  height: fit-content;
  position: sticky;
  top: 80px;
}

.cart-summary h3 {
  font-size: 1.1rem;
  margin-bottom: 1rem;
  padding-bottom: 1rem;
  border-bottom: 1px solid var(--border);
}

.summary-row {
  display: flex;
  justify-content: space-between;
  margin-bottom: 0.75rem;
  color: var(--text-secondary);
}

.free { color: var(--success); }

.summary-total {
  display: flex;
  justify-content: space-between;
  padding-top: 1rem;
  margin-top: 1rem;
  border-top: 1px solid var(--border);
  font-weight: 600;
}

.total-price {
  font-size: 1.25rem;
  color: var(--danger);
}

.checkout-btn {
  width: 100%;
  margin-top: 1.5rem;
  padding: 0.875rem;
  background: var(--primary);
  color: white;
  border: none;
  border-radius: 8px;
  cursor: pointer;
  font-size: 1rem;
  font-weight: 600;
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 0.5rem;
}

.checkout-btn:hover { background: var(--primary-dark); }
.checkout-btn:disabled { opacity: 0.7; cursor: not-allowed; }

.spin { animation: spin 1s linear infinite; }
@keyframes spin { to { transform: rotate(360deg); } }

@media (max-width: 768px) {
  .cart-content {
    grid-template-columns: 1fr;
  }

  .cart-item {
    flex-wrap: wrap;
  }

  .item-info { order: 1; width: calc(100% - 64px); }
  .item-icon { order: 0; }
  .item-qty { order: 2; }
  .item-subtotal { order: 3; }
  .delete-btn { order: 4; }

  .cart-summary { position: static; }
}
</style>
