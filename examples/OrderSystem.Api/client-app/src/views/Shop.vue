<script setup lang="ts">
import { ref } from 'vue'
import { useCartStore } from '../stores/cart'

const cart = useCartStore()
const addedId = ref<string | null>(null)

const products = [
  { id: 'P001', name: '笔记本电脑', desc: 'M2芯片 16GB内存 512GB存储', price: 5999, image: 'laptop' },
  { id: 'P002', name: '智能手机', desc: '5G旗舰 6.7寸OLED 128GB', price: 3999, image: 'smartphone' },
  { id: 'P003', name: '平板电脑', desc: '10.9寸 A15芯片 256GB', price: 2499, image: 'tablet' },
  { id: 'P004', name: '智能手表', desc: '健康监测 心率血氧 GPS', price: 1299, image: 'watch' },
  { id: 'P005', name: '无线耳机', desc: '主动降噪 空间音频 30小时续航', price: 899, image: 'headphones' },
  { id: 'P006', name: '机械键盘', desc: 'RGB背光 青轴 热插拔', price: 399, image: 'keyboard' },
]

const addToCart = (p: typeof products[0]) => {
  cart.addItem(p)
  addedId.value = p.id
  setTimeout(() => addedId.value = null, 1200)
}
</script>

<template>
  <div class="shop">
    <div class="shop-header">
      <h1>商品列表</h1>
      <p>共 {{ products.length }} 件商品</p>
    </div>

    <div class="products">
      <div v-for="p in products" :key="p.id" class="product-card">
        <div class="product-image">
          <va-icon :name="p.image" size="3rem" color="primary" />
        </div>
        <div class="product-info">
          <h3>{{ p.name }}</h3>
          <p>{{ p.desc }}</p>
          <div class="product-footer">
            <span class="price">¥{{ p.price.toLocaleString() }}</span>
            <button :class="['add-btn', { added: addedId === p.id }]" @click="addToCart(p)">
              <va-icon :name="addedId === p.id ? 'check' : 'add_shopping_cart'" size="1.25rem" />
              <span>{{ addedId === p.id ? '已添加' : '加入购物车' }}</span>
            </button>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.shop-header {
  margin-bottom: 1.5rem;
}

.shop-header h1 {
  font-size: 1.5rem;
  font-weight: 700;
  margin-bottom: 0.25rem;
}

.shop-header p {
  color: var(--text-secondary);
}

.products {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(280px, 1fr));
  gap: 1.5rem;
}

.product-card {
  background: var(--card);
  border-radius: 12px;
  overflow: hidden;
  box-shadow: 0 1px 3px rgba(0,0,0,0.1);
  transition: transform 0.2s, box-shadow 0.2s;
}

.product-card:hover {
  transform: translateY(-4px);
  box-shadow: 0 8px 25px rgba(0,0,0,0.1);
}

.product-image {
  background: linear-gradient(135deg, #f0f4ff 0%, #e8ecff 100%);
  padding: 2rem;
  display: flex;
  justify-content: center;
  align-items: center;
}

.product-info {
  padding: 1rem;
}

.product-info h3 {
  font-size: 1.1rem;
  font-weight: 600;
  margin-bottom: 0.5rem;
}

.product-info p {
  color: var(--text-secondary);
  font-size: 0.875rem;
  margin-bottom: 1rem;
  line-height: 1.5;
}

.product-footer {
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.price {
  font-size: 1.25rem;
  font-weight: 700;
  color: var(--danger);
}

.add-btn {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  padding: 0.5rem 1rem;
  background: var(--primary);
  color: white;
  border: none;
  border-radius: 8px;
  cursor: pointer;
  font-size: 0.875rem;
  font-weight: 500;
  transition: background 0.2s;
}

.add-btn:hover { background: var(--primary-dark); }
.add-btn.added { background: var(--success); }

@media (max-width: 480px) {
  .products { grid-template-columns: 1fr; }
  .product-footer { flex-direction: column; gap: 0.75rem; align-items: stretch; }
  .add-btn { justify-content: center; }
}
</style>
