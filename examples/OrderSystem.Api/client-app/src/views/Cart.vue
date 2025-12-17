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
        <va-icon name="shopping_cart" class="mr-2" />购物车
        <va-badge v-if="cart.count > 0" :text="String(cart.count)" color="primary" class="ml-2" />
      </va-card-title>
    </va-card>

    <va-card v-if="cart.isEmpty" class="text-center">
      <va-card-content class="pa-6">
        <va-icon name="remove_shopping_cart" size="4rem" color="secondary" />
        <h3 class="va-h5 mt-4">购物车是空的</h3>
        <p class="text-secondary mb-4">快去选购商品吧！</p>
        <va-button @click="router.push('/')">
          <va-icon name="store" class="mr-2" />去购物
        </va-button>
      </va-card-content>
    </va-card>

    <div v-else class="row">
      <div class="flex md8 xs12">
        <va-card class="mb-4">
          <va-card-content>
            <va-data-table :items="cart.items" :columns="[
              { key: 'name', label: '商品' },
              { key: 'price', label: '单价' },
              { key: 'quantity', label: '数量' },
              { key: 'subtotal', label: '小计' },
              { key: 'actions', label: '操作' }
            ]">
              <template #cell(name)="{ rowData }">
                <div class="d-flex align-center">
                  <va-avatar color="background-element" class="mr-2">
                    <va-icon :name="rowData.image" color="primary" />
                  </va-avatar>
                  {{ rowData.name }}
                </div>
              </template>
              <template #cell(price)="{ rowData }">
                ¥{{ rowData.price.toFixed(2) }}
              </template>
              <template #cell(quantity)="{ rowData }">
                <div class="d-flex align-center">
                  <va-button size="small" preset="secondary" @click="cart.updateQuantity(rowData.productId, rowData.quantity - 1)">
                    <va-icon name="remove" />
                  </va-button>
                  <span class="mx-2">{{ rowData.quantity }}</span>
                  <va-button size="small" preset="secondary" @click="cart.updateQuantity(rowData.productId, rowData.quantity + 1)">
                    <va-icon name="add" />
                  </va-button>
                </div>
              </template>
              <template #cell(subtotal)="{ rowData }">
                <strong class="va-text-danger">¥{{ (rowData.price * rowData.quantity).toFixed(2) }}</strong>
              </template>
              <template #cell(actions)="{ rowData }">
                <va-button preset="plain" color="danger" @click="cart.removeItem(rowData.productId)">
                  <va-icon name="delete" />
                </va-button>
              </template>
            </va-data-table>
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
              <span class="va-text-success">免运费</span>
            </div>
            <va-divider class="my-3" />
            <div class="d-flex justify-space-between mb-4">
              <strong>合计</strong>
              <strong class="va-text-danger va-h5">¥{{ cart.total.toFixed(2) }}</strong>
            </div>
            <va-button color="primary" block @click="router.push('/checkout')">
              <va-icon name="payment" class="mr-2" />去结算
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
