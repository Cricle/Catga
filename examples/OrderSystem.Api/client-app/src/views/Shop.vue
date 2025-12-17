<script setup lang="ts">
import { ref } from 'vue'
import { useCartStore } from '../stores/cart'

const cart = useCartStore()
const addedProduct = ref<string | null>(null)

const products = [
  { id: 'LAPTOP-001', name: '笔记本电脑 Pro', desc: '高性能轻薄本，M2芯片，16GB内存', price: 5999.00, image: 'laptop', stock: 50, category: '电脑' },
  { id: 'PHONE-001', name: '智能手机 Ultra', desc: '旗舰5G手机，6.7寸OLED屏', price: 3999.00, image: 'smartphone', stock: 100, category: '手机' },
  { id: 'TABLET-001', name: '平板电脑 Air', desc: '10.9寸高清屏，A15芯片', price: 2499.00, image: 'tablet', stock: 30, category: '平板' },
  { id: 'WATCH-001', name: '智能手表 Series', desc: '健康监测，心率血氧', price: 1299.00, image: 'watch', stock: 80, category: '穿戴' },
  { id: 'EARPHONE-001', name: '无线耳机 Pro', desc: '主动降噪，空间音频', price: 899.00, image: 'headphones', stock: 200, category: '配件' },
  { id: 'KEYBOARD-001', name: '机械键盘 RGB', desc: '青轴手感，RGB背光', price: 399.00, image: 'keyboard', stock: 150, category: '配件' },
  { id: 'MOUSE-001', name: '无线鼠标', desc: '人体工学设计，静音按键', price: 199.00, image: 'mouse', stock: 300, category: '配件' },
  { id: 'CHARGER-001', name: '快充充电器', desc: '65W氮化镓，三口快充', price: 149.00, image: 'electrical_services', stock: 500, category: '配件' },
]

const addToCart = (product: typeof products[0]) => {
  cart.addItem(product)
  addedProduct.value = product.id
  setTimeout(() => {
    addedProduct.value = null
  }, 1500)
}
</script>

<template>
  <div>
    <!-- Hero Banner -->
    <va-card class="hero-banner mb-6">
      <va-card-content class="hero-content">
        <div class="hero-text">
          <h1>OrderSystem 商城</h1>
          <p>基于 Catga CQRS 框架构建的示例电商应用</p>
          <div class="hero-features">
            <va-chip color="primary" size="small"><va-icon name="bolt" class="mr-1" /> CQRS架构</va-chip>
            <va-chip color="success" size="small"><va-icon name="schema" class="mr-1" /> Flow DSL</va-chip>
            <va-chip color="warning" size="small"><va-icon name="monitoring" class="mr-1" /> 可观测性</va-chip>
          </div>
        </div>
      </va-card-content>
    </va-card>

    <!-- Products Grid -->
    <h2 class="section-title mb-4">
      <va-icon name="category" class="mr-2" /> 全部商品
    </h2>

    <div class="products-grid">
      <va-card v-for="product in products" :key="product.id" class="product-card">
        <div class="product-image">
          <va-icon :name="product.image" size="3rem" color="primary" />
          <va-chip size="small" class="product-category">{{ product.category }}</va-chip>
        </div>
        <va-card-content>
          <h3 class="product-name">{{ product.name }}</h3>
          <p class="product-desc">{{ product.desc }}</p>
          <div class="product-footer">
            <div class="product-price">
              <span class="price-symbol">¥</span>
              <span class="price-value">{{ product.price.toFixed(2) }}</span>
            </div>
            <va-button
              :color="addedProduct === product.id ? 'success' : 'primary'"
              size="small"
              @click="addToCart(product)"
            >
              <va-icon :name="addedProduct === product.id ? 'check' : 'add_shopping_cart'" />
            </va-button>
          </div>
        </va-card-content>
      </va-card>
    </div>

    <!-- Features Section -->
    <h2 class="section-title mb-4 mt-6">
      <va-icon name="stars" class="mr-2" /> Catga 特性演示
    </h2>

    <div class="row">
      <div class="flex md4 xs12">
        <va-card class="feature-card" @click="$router.push('/admin/observability')">
          <va-card-content class="text-center">
            <va-icon name="monitoring" size="2.5rem" color="primary" />
            <h4 class="mt-3">可观测性</h4>
            <p class="text-secondary">Metrics、Tracing、Logging</p>
          </va-card-content>
        </va-card>
      </div>
      <div class="flex md4 xs12">
        <va-card class="feature-card" @click="$router.push('/admin/hotreload')">
          <va-card-content class="text-center">
            <va-icon name="autorenew" size="2.5rem" color="success" />
            <h4 class="mt-3">热重载</h4>
            <p class="text-secondary">Flow动态注册与版本管理</p>
          </va-card-content>
        </va-card>
      </div>
      <div class="flex md4 xs12">
        <va-card class="feature-card" @click="$router.push('/admin/readmodelsync')">
          <va-card-content class="text-center">
            <va-icon name="sync" size="2.5rem" color="warning" />
            <h4 class="mt-3">读模型同步</h4>
            <p class="text-secondary">CQRS变更追踪与同步</p>
          </va-card-content>
        </va-card>
      </div>
    </div>
  </div>
</template>

<style scoped>
.hero-banner {
  background: linear-gradient(135deg, #512da8 0%, #7c4dff 100%);
  color: #fff;
  overflow: hidden;
}

.hero-content {
  padding: 3rem 2rem !important;
}

.hero-text h1 {
  font-size: 2rem;
  font-weight: 700;
  margin-bottom: 0.5rem;
}

.hero-text p {
  opacity: 0.9;
  margin-bottom: 1rem;
}

.hero-features {
  display: flex;
  gap: 0.5rem;
  flex-wrap: wrap;
}

.section-title {
  display: flex;
  align-items: center;
  font-size: 1.25rem;
  font-weight: 600;
  color: #333;
}

.products-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(260px, 1fr));
  gap: 1.5rem;
}

.product-card {
  transition: transform 0.2s, box-shadow 0.2s;
  cursor: pointer;
}

.product-card:hover {
  transform: translateY(-4px);
  box-shadow: 0 12px 24px rgba(0,0,0,0.1);
}

.product-image {
  position: relative;
  display: flex;
  justify-content: center;
  align-items: center;
  padding: 2rem;
  background: linear-gradient(135deg, #f5f7fa 0%, #e8eaf6 100%);
}

.product-category {
  position: absolute;
  top: 0.75rem;
  right: 0.75rem;
}

.product-name {
  font-size: 1rem;
  font-weight: 600;
  margin: 0 0 0.5rem;
  color: #333;
}

.product-desc {
  font-size: 0.875rem;
  color: #666;
  margin: 0 0 1rem;
  line-height: 1.4;
}

.product-footer {
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.product-price {
  color: #e53935;
}

.price-symbol {
  font-size: 0.875rem;
}

.price-value {
  font-size: 1.25rem;
  font-weight: 700;
}

.feature-card {
  cursor: pointer;
  transition: transform 0.2s, box-shadow 0.2s;
  margin-bottom: 1rem;
}

.feature-card:hover {
  transform: translateY(-4px);
  box-shadow: 0 8px 24px rgba(0,0,0,0.1);
}

.feature-card h4 {
  font-size: 1rem;
  font-weight: 600;
  margin: 0;
}

.feature-card p {
  font-size: 0.875rem;
  margin: 0.5rem 0 0;
}

.mt-3 {
  margin-top: 0.75rem;
}

.mt-6 {
  margin-top: 2rem;
}

.mb-4 {
  margin-bottom: 1rem;
}

.mb-6 {
  margin-bottom: 1.5rem;
}

.mr-1 {
  margin-right: 0.25rem;
}

.mr-2 {
  margin-right: 0.5rem;
}

.text-center {
  text-align: center;
}

.text-secondary {
  color: #666;
}
</style>
