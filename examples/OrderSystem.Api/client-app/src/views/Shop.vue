<script setup lang="ts">
import { ref } from 'vue'
import { useCartStore } from '../stores/cart'

const cart = useCartStore()
const addedId = ref<string | null>(null)

const products = [
  { id: 'P001', name: '笔记本电脑', desc: 'M2芯片 16GB内存', price: 5999, image: 'laptop' },
  { id: 'P002', name: '智能手机', desc: '5G旗舰 6.7寸屏', price: 3999, image: 'smartphone' },
  { id: 'P003', name: '平板电脑', desc: '10.9寸 A15芯片', price: 2499, image: 'tablet' },
  { id: 'P004', name: '智能手表', desc: '健康监测 心率血氧', price: 1299, image: 'watch' },
  { id: 'P005', name: '无线耳机', desc: '主动降噪 空间音频', price: 899, image: 'headphones' },
  { id: 'P006', name: '机械键盘', desc: 'RGB背光 青轴', price: 399, image: 'keyboard' },
]

const addToCart = (p: typeof products[0]) => {
  cart.addItem(p)
  addedId.value = p.id
  setTimeout(() => addedId.value = null, 1000)
}
</script>

<template>
  <div>
    <h2 class="mb-4">商品列表</h2>
    <div class="grid">
      <va-card v-for="p in products" :key="p.id">
        <va-card-content class="text-center">
          <va-icon :name="p.image" size="3rem" color="primary" />
        </va-card-content>
        <va-card-title>{{ p.name }}</va-card-title>
        <va-card-content>
          <p class="desc">{{ p.desc }}</p>
          <div class="footer">
            <span class="price">¥{{ p.price }}</span>
            <va-button size="small" :color="addedId === p.id ? 'success' : 'primary'" @click="addToCart(p)">
              <va-icon :name="addedId === p.id ? 'check' : 'add_shopping_cart'" />
            </va-button>
          </div>
        </va-card-content>
      </va-card>
    </div>
  </div>
</template>

<style scoped>
.mb-4 { margin-bottom: 1rem; }
.grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(220px, 1fr));
  gap: 1rem;
}
.desc { color: #666; font-size: 0.9rem; margin-bottom: 1rem; }
.footer { display: flex; justify-content: space-between; align-items: center; }
.price { font-size: 1.2rem; font-weight: bold; color: #e53935; }
</style>
