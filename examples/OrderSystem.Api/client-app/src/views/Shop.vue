<script setup lang="ts">
import { ref } from 'vue'
import { useCartStore } from '../stores/cart'

const cart = useCartStore()
const addedProduct = ref<string | null>(null)

const products = [
  { id: 'LAPTOP-001', name: '笔记本电脑 Pro', desc: '高性能轻薄本，M2芯片', price: 5999.00, image: 'laptop', category: '电脑' },
  { id: 'PHONE-001', name: '智能手机 Ultra', desc: '旗舰5G手机，6.7寸OLED', price: 3999.00, image: 'smartphone', category: '手机' },
  { id: 'TABLET-001', name: '平板电脑 Air', desc: '10.9寸高清屏，A15芯片', price: 2499.00, image: 'tablet', category: '平板' },
  { id: 'WATCH-001', name: '智能手表 Series', desc: '健康监测，心率血氧', price: 1299.00, image: 'watch', category: '穿戴' },
  { id: 'EARPHONE-001', name: '无线耳机 Pro', desc: '主动降噪，空间音频', price: 899.00, image: 'headphones', category: '配件' },
  { id: 'KEYBOARD-001', name: '机械键盘 RGB', desc: '青轴手感，RGB背光', price: 399.00, image: 'keyboard', category: '配件' },
]

const addToCart = (product: typeof products[0]) => {
  cart.addItem(product)
  addedProduct.value = product.id
  setTimeout(() => addedProduct.value = null, 1500)
}
</script>

<template>
  <div>
    <va-card class="mb-4" color="primary" gradient>
      <va-card-content>
        <h1 class="va-h4 mb-2">OrderSystem 商城</h1>
        <p class="mb-3">基于 Catga CQRS 框架构建的示例电商应用</p>
        <va-chip color="background-element" class="mr-2">CQRS架构</va-chip>
        <va-chip color="background-element" class="mr-2">Flow DSL</va-chip>
        <va-chip color="background-element">可观测性</va-chip>
      </va-card-content>
    </va-card>

    <h2 class="va-h5 mb-3">
      <va-icon name="category" class="mr-2" />全部商品
    </h2>

    <div class="row">
      <div class="flex md4 sm6 xs12" v-for="product in products" :key="product.id">
        <va-card class="mb-4">
          <va-card-content class="text-center pa-4">
            <va-icon :name="product.image" size="3rem" color="primary" />
            <va-chip size="small" class="mt-2">{{ product.category }}</va-chip>
          </va-card-content>
          <va-card-title>{{ product.name }}</va-card-title>
          <va-card-content>
            <p class="text-secondary mb-3">{{ product.desc }}</p>
            <div class="d-flex justify-space-between align-center">
              <span class="va-text-danger"><strong>¥{{ product.price.toFixed(2) }}</strong></span>
              <va-button
                :color="addedProduct === product.id ? 'success' : 'primary'"
                size="small"
                @click="addToCart(product)"
              >
                <va-icon :name="addedProduct === product.id ? 'check' : 'add_shopping_cart'" class="mr-1" />
                {{ addedProduct === product.id ? '已添加' : '加入购物车' }}
              </va-button>
            </div>
          </va-card-content>
        </va-card>
      </div>
    </div>

    <va-divider class="my-4" />

    <h2 class="va-h5 mb-3">
      <va-icon name="stars" class="mr-2" />Catga 特性演示
    </h2>

    <div class="row">
      <div class="flex md4 xs12">
        <va-card class="mb-4" @click="$router.push('/admin/observability')" style="cursor: pointer;">
          <va-card-content class="text-center">
            <va-icon name="monitoring" size="2rem" color="primary" />
            <h4 class="va-h6 mt-2">可观测性</h4>
            <p class="text-secondary">Metrics、Tracing、Logging</p>
          </va-card-content>
        </va-card>
      </div>
      <div class="flex md4 xs12">
        <va-card class="mb-4" @click="$router.push('/admin/hotreload')" style="cursor: pointer;">
          <va-card-content class="text-center">
            <va-icon name="autorenew" size="2rem" color="success" />
            <h4 class="va-h6 mt-2">热重载</h4>
            <p class="text-secondary">Flow动态注册与版本管理</p>
          </va-card-content>
        </va-card>
      </div>
      <div class="flex md4 xs12">
        <va-card class="mb-4" @click="$router.push('/admin/readmodelsync')" style="cursor: pointer;">
          <va-card-content class="text-center">
            <va-icon name="sync" size="2rem" color="warning" />
            <h4 class="va-h6 mt-2">读模型同步</h4>
            <p class="text-secondary">CQRS变更追踪与同步</p>
          </va-card-content>
        </va-card>
      </div>
    </div>
  </div>
</template>
