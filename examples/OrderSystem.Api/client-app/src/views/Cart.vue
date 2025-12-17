<script setup lang="ts">
import { useRouter } from 'vue-router'
import { useCartStore } from '../stores/cart'

const router = useRouter()
const cart = useCartStore()
</script>

<template>
  <div>
    <va-card class="mb-4">
      <va-card-title>
        <va-icon name="shopping_cart" class="mr-2" /> 购物车
        <va-badge v-if="cart.count > 0" :text="String(cart.count)" color="primary" class="ml-2" />
      </va-card-title>
    </va-card>

    <div v-if="cart.isEmpty" class="text-center pa-6">
      <va-icon name="remove_shopping_cart" size="4rem" color="secondary" />
      <h3 class="mt-4">购物车是空的</h3>
      <p class="text-secondary">快去选购商品吧！</p>
      <va-button class="mt-4" @click="router.push('/')">
        <va-icon name="store" class="mr-2" /> 去购物
      </va-button>
    </div>

    <div v-else class="row">
      <div class="flex md8 xs12">
        <va-card class="mb-4">
          <va-card-content>
            <va-list>
              <va-list-item v-for="item in cart.items" :key="item.productId" class="py-3">
                <va-list-item-section avatar>
                  <va-avatar color="backgroundSecondary" size="large">
                    <va-icon :name="item.image" color="primary" />
                  </va-avatar>
                </va-list-item-section>
                <va-list-item-section>
                  <va-list-item-label>{{ item.name }}</va-list-item-label>
                  <va-list-item-label caption>¥{{ item.price.toFixed(2) }}</va-list-item-label>
                </va-list-item-section>
                <va-list-item-section>
                  <div class="d-flex align-center">
                    <va-button size="small" preset="secondary" @click="cart.updateQuantity(item.productId, item.quantity - 1)">
                      <va-icon name="remove" />
                    </va-button>
                    <span class="mx-3">{{ item.quantity }}</span>
                    <va-button size="small" preset="secondary" @click="cart.updateQuantity(item.productId, item.quantity + 1)">
                      <va-icon name="add" />
                    </va-button>
                  </div>
                </va-list-item-section>
                <va-list-item-section>
                  <strong>¥{{ (item.price * item.quantity).toFixed(2) }}</strong>
                </va-list-item-section>
                <va-list-item-section icon>
                  <va-button preset="plain" color="danger" @click="cart.removeItem(item.productId)">
                    <va-icon name="delete" />
                  </va-button>
                </va-list-item-section>
              </va-list-item>
            </va-list>
          </va-card-content>
        </va-card>
      </div>

      <div class="flex md4 xs12">
        <va-card>
          <va-card-title>订单摘要</va-card-title>
          <va-card-content>
            <div class="d-flex justify-space-between mb-2">
              <span>商品数量</span>
              <span>{{ cart.count }} 件</span>
            </div>
            <div class="d-flex justify-space-between mb-2">
              <span>商品金额</span>
              <span>¥{{ cart.total.toFixed(2) }}</span>
            </div>
            <div class="d-flex justify-space-between mb-2">
              <span>运费</span>
              <span class="text-success">免运费</span>
            </div>
            <va-divider class="my-3" />
            <div class="d-flex justify-space-between">
              <strong>合计</strong>
              <strong class="text-danger" style="font-size: 1.5rem;">¥{{ cart.total.toFixed(2) }}</strong>
            </div>
            <va-button color="primary" block class="mt-4" @click="router.push('/checkout')">
              <va-icon name="payment" class="mr-2" /> 去结算
            </va-button>
            <va-button preset="secondary" block class="mt-2" @click="router.push('/')">
              继续购物
            </va-button>
          </va-card-content>
        </va-card>
      </div>
    </div>
  </div>
</template>
