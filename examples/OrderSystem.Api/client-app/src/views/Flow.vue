<template>
  <div class="flow-page">
    <va-card>
      <va-card-title>Flow / Saga Pattern Demo</va-card-title>
      <va-card-content>
        <va-alert color="info" class="mb-4">
          <template #icon><va-icon name="info" /></template>
          <strong>What is Flow/Saga?</strong>
          <p>Flow pattern implements distributed transactions with automatic compensation.
          If any step fails, previous steps are rolled back automatically.</p>
        </va-alert>

        <div class="flow-diagram">
          <div class="flow-step" :class="{ active: currentStep >= 0, success: currentStep > 0 }">
            <va-icon name="save" size="large" />
            <span>Save Order</span>
            <small>Compensation: Delete</small>
          </div>
          <va-icon name="arrow_forward" class="arrow" />
          <div class="flow-step" :class="{ active: currentStep >= 1, success: currentStep > 1 }">
            <va-icon name="inventory" size="large" />
            <span>Reserve Stock</span>
            <small>Compensation: Release</small>
          </div>
          <va-icon name="arrow_forward" class="arrow" />
          <div class="flow-step" :class="{ active: currentStep >= 2, success: currentStep > 2 }">
            <va-icon name="check_circle" size="large" />
            <span>Confirm Order</span>
            <small>Compensation: Mark Failed</small>
          </div>
          <va-icon name="arrow_forward" class="arrow" />
          <div class="flow-step" :class="{ active: currentStep >= 3, success: currentStep > 3 }">
            <va-icon name="notifications" size="large" />
            <span>Publish Event</span>
            <small>No compensation needed</small>
          </div>
        </div>

        <va-divider />

        <div class="demo-section">
          <h4>Try It Out</h4>
          <div class="demo-controls">
            <va-button color="primary" :loading="loading" @click="executeFlow">
              <va-icon name="play_arrow" class="mr-2" />
              Execute Flow
            </va-button>
            <va-button color="secondary" @click="reset">
              <va-icon name="refresh" class="mr-2" />
              Reset
            </va-button>
          </div>

          <div v-if="result" class="result-section">
            <va-alert :color="result.success ? 'success' : 'danger'">
              <template #icon>
                <va-icon :name="result.success ? 'check_circle' : 'error'" />
              </template>
              <strong>{{ result.success ? 'Flow Completed!' : 'Flow Failed' }}</strong>
              <p v-if="result.orderId">Order ID: {{ result.orderId }}</p>
              <p v-if="result.error">{{ result.error }}</p>
            </va-alert>
          </div>

          <div v-if="logs.length" class="logs-section">
            <h4>Execution Logs</h4>
            <div class="logs">
              <div v-for="(log, i) in logs" :key="i" :class="['log-entry', log.type]">
                <va-icon :name="log.icon" size="small" />
                <span>{{ log.message }}</span>
                <small>{{ log.time }}</small>
              </div>
            </div>
          </div>
        </div>
      </va-card-content>
    </va-card>

    <!-- Flow DSL Info -->
    <va-card class="mt-4">
      <va-card-title>Catga Flow DSL Features</va-card-title>
      <va-card-content>
        <div class="features-grid">
          <div class="feature">
            <va-icon name="account_tree" color="primary" />
            <h5>Declarative Syntax</h5>
            <p>Define flows with fluent builder API</p>
          </div>
          <div class="feature">
            <va-icon name="undo" color="warning" />
            <h5>Auto Compensation</h5>
            <p>Automatic rollback on failure</p>
          </div>
          <div class="feature">
            <va-icon name="timeline" color="success" />
            <h5>State Tracking</h5>
            <p>Track flow state through execution</p>
          </div>
          <div class="feature">
            <va-icon name="replay" color="secondary" />
            <h5>Retry & Recovery</h5>
            <p>Built-in retry and recovery</p>
          </div>
        </div>
      </va-card-content>
    </va-card>
  </div>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import api from '../api'

const loading = ref(false)
const currentStep = ref(-1)
const result = ref<{ success: boolean; orderId?: string; error?: string } | null>(null)
const logs = ref<{ type: string; icon: string; message: string; time: string }[]>([])

function addLog(type: string, icon: string, message: string) {
  logs.value.push({ type, icon, message, time: new Date().toLocaleTimeString() })
}

async function executeFlow() {
  loading.value = true
  result.value = null
  logs.value = []
  currentStep.value = -1

  try {
    // Simulate step-by-step execution with delays
    addLog('info', 'play_arrow', 'Starting Flow execution...')

    currentStep.value = 0
    addLog('info', 'save', 'Step 1: Saving order...')
    await new Promise(r => setTimeout(r, 500))
    addLog('success', 'check', 'Order saved successfully')

    currentStep.value = 1
    addLog('info', 'inventory', 'Step 2: Reserving stock...')
    await new Promise(r => setTimeout(r, 500))
    addLog('success', 'check', 'Stock reserved')

    currentStep.value = 2
    addLog('info', 'check_circle', 'Step 3: Confirming order...')
    await new Promise(r => setTimeout(r, 500))
    addLog('success', 'check', 'Order confirmed')

    currentStep.value = 3
    addLog('info', 'notifications', 'Step 4: Publishing event...')

    // Actually call the API
    const response = await api.createOrderWithFlow('FLOW-DEMO', [
      { productId: 'FLOW-001', productName: 'Flow Demo Product', quantity: 1, unitPrice: 99.99 }
    ])

    addLog('success', 'check', 'Event published')
    addLog('success', 'done_all', `Flow completed! Order: ${response.orderId}`)

    currentStep.value = 4
    result.value = { success: true, orderId: response.orderId }
  } catch (e: any) {
    addLog('error', 'error', `Flow failed: ${e.message}`)
    result.value = { success: false, error: e.message }
  } finally {
    loading.value = false
  }
}

function reset() {
  currentStep.value = -1
  result.value = null
  logs.value = []
}
</script>

<style scoped>
.flow-diagram {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 1rem;
  padding: 2rem;
  background: #f8fafc;
  border-radius: 8px;
  margin: 1rem 0;
  overflow-x: auto;
}

.flow-step {
  display: flex;
  flex-direction: column;
  align-items: center;
  padding: 1rem;
  background: white;
  border: 2px solid #e2e8f0;
  border-radius: 8px;
  min-width: 120px;
  transition: all 0.3s ease;
  opacity: 0.5;
}

.flow-step.active {
  opacity: 1;
  border-color: #3b82f6;
}

.flow-step.success {
  border-color: #10b981;
  background: #ecfdf5;
}

.flow-step span {
  margin-top: 0.5rem;
  font-weight: 500;
}

.flow-step small {
  font-size: 0.7rem;
  color: #666;
}

.arrow {
  color: #cbd5e1;
}

.demo-section {
  margin-top: 1rem;
}

.demo-controls {
  display: flex;
  gap: 1rem;
  margin: 1rem 0;
}

.result-section {
  margin: 1rem 0;
}

.logs-section {
  margin-top: 1rem;
}

.logs {
  background: #1e293b;
  color: #e2e8f0;
  border-radius: 8px;
  padding: 1rem;
  max-height: 300px;
  overflow-y: auto;
  font-family: monospace;
  font-size: 0.875rem;
}

.log-entry {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  padding: 0.25rem 0;
}

.log-entry.success { color: #10b981; }
.log-entry.error { color: #ef4444; }
.log-entry.info { color: #3b82f6; }

.log-entry small {
  margin-left: auto;
  opacity: 0.6;
}

.features-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
  gap: 1rem;
}

.feature {
  text-align: center;
  padding: 1rem;
}

.feature h5 {
  margin: 0.5rem 0;
}

.feature p {
  font-size: 0.875rem;
  color: #666;
  margin: 0;
}

.mt-4 { margin-top: 1rem; }
.mb-4 { margin-bottom: 1rem; }
.mr-2 { margin-right: 0.5rem; }
</style>
