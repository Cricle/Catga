<template>
  <div class="create-order-form">
    <va-form @submit.prevent="submit">
      <va-input
        v-model="form.customerId"
        label="Customer ID"
        placeholder="CUST-001"
        :rules="[v => !!v || 'Required']"
        class="mb-3"
      />

      <va-input
        v-model="form.customerName"
        label="Customer Name (Optional)"
        placeholder="John Doe"
        class="mb-3"
      />

      <va-input
        v-model="form.customerEmail"
        label="Customer Email (Optional)"
        placeholder="john@example.com"
        class="mb-3"
      />

      <va-divider />

      <h4>Order Items</h4>

      <div v-for="(item, index) in form.items" :key="index" class="item-row">
        <va-select
          v-model="item.productId"
          :options="productOptions"
          label="Product"
          class="product-select"
          @update:modelValue="updateProduct(index)"
        />
        <va-input
          v-model.number="item.quantity"
          type="number"
          label="Qty"
          :min="1"
          :max="99"
          class="qty-input"
        />
        <va-button
          v-if="form.items.length > 1"
          preset="secondary"
          size="small"
          @click="removeItem(index)"
        >
          <va-icon name="remove" />
        </va-button>
      </div>

      <va-button preset="secondary" size="small" @click="addItem" class="mb-3">
        <va-icon name="add" class="mr-1" />
        Add Item
      </va-button>

      <va-divider />

      <div class="total-section">
        <span>Total:</span>
        <strong>${{ calculateTotal.toFixed(2) }}</strong>
      </div>

      <div class="form-actions">
        <va-button @click="$emit('cancel')">Cancel</va-button>
        <va-button color="primary" type="submit" :loading="loading">
          Create Order
        </va-button>
        <va-button color="secondary" @click="submitWithFlow" :loading="loading">
          Create with Flow
        </va-button>
      </div>
    </va-form>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, computed } from 'vue'
import { useOrderStore } from '../stores/order'

const emit = defineEmits(['created', 'cancel'])
const store = useOrderStore()
const loading = ref(false)

const products = {
  'laptop': { name: 'Laptop Pro', price: 999.99 },
  'phone': { name: 'SmartPhone X', price: 699.99 },
  'tablet': { name: 'Tablet Air', price: 499.99 },
  'headphones': { name: 'Wireless Headphones', price: 199.99 },
  'watch': { name: 'Smart Watch', price: 299.99 },
  'keyboard': { name: 'Mechanical Keyboard', price: 149.99 },
}

const productOptions = Object.entries(products).map(([id, p]) => ({
  value: id,
  text: `${p.name} ($${p.price})`
}))

const form = reactive({
  customerId: 'CUST-001',
  customerName: '',
  customerEmail: '',
  items: [{ productId: 'laptop', productName: 'Laptop Pro', quantity: 1, unitPrice: 999.99 }]
})

const calculateTotal = computed(() => {
  return form.items.reduce((sum, item) => sum + item.quantity * item.unitPrice, 0)
})

function updateProduct(index: number) {
  const product = products[form.items[index].productId as keyof typeof products]
  if (product) {
    form.items[index].productName = product.name
    form.items[index].unitPrice = product.price
  }
}

function addItem() {
  form.items.push({ productId: 'phone', productName: 'SmartPhone X', quantity: 1, unitPrice: 699.99 })
}

function removeItem(index: number) {
  form.items.splice(index, 1)
}

async function submit() {
  loading.value = true
  try {
    await store.createOrder(form.customerId, form.items)
    emit('created')
  } catch (e: any) {
    alert('Failed to create order: ' + e.message)
  } finally {
    loading.value = false
  }
}

async function submitWithFlow() {
  loading.value = true
  try {
    await store.createOrderWithFlow(form.customerId, form.items)
    emit('created')
  } catch (e: any) {
    alert('Failed to create order: ' + e.message)
  } finally {
    loading.value = false
  }
}
</script>

<style scoped>
.item-row {
  display: flex;
  gap: 0.5rem;
  align-items: flex-end;
  margin-bottom: 0.5rem;
}

.product-select {
  flex: 1;
}

.qty-input {
  width: 80px;
}

.total-section {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 1rem;
  background: #f8fafc;
  border-radius: 8px;
  margin-bottom: 1rem;
}

.total-section strong {
  font-size: 1.5rem;
  color: #10b981;
}

.form-actions {
  display: flex;
  justify-content: flex-end;
  gap: 0.5rem;
}

.mb-3 { margin-bottom: 0.75rem; }
.mr-1 { margin-right: 0.25rem; }
</style>
