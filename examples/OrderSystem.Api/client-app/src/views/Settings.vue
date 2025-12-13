<template>
  <div class="settings-page">
    <va-card>
      <va-card-title>System Configuration</va-card-title>
      <va-card-content>
        <!-- Health Status -->
        <div class="health-section">
          <h4>System Health</h4>
          <div class="health-status" v-if="health">
            <va-badge
              :text="health.status"
              :color="health.status === 'Healthy' ? 'success' : 'danger'"
            />
            <span class="uptime">Checked: {{ new Date().toLocaleTimeString() }}</span>
          </div>
          <va-button size="small" @click="checkHealth">
            <va-icon name="refresh" class="mr-2" />
            Check Health
          </va-button>
        </div>

        <va-divider />

        <!-- Transport Mode -->
        <div class="config-section">
          <h4>Transport Mode</h4>
          <p class="description">Configure how messages are transported between services</p>
          <va-radio
            v-for="option in transportOptions"
            :key="option.value"
            v-model="config.transport"
            :option="option"
            class="config-option"
          />
        </div>

        <va-divider />

        <!-- Persistence Mode -->
        <div class="config-section">
          <h4>Persistence Mode</h4>
          <p class="description">Configure how data is persisted</p>
          <va-radio
            v-for="option in persistenceOptions"
            :key="option.value"
            v-model="config.persistence"
            :option="option"
            class="config-option"
          />
        </div>

        <va-divider />

        <!-- Connection Strings -->
        <div class="config-section" v-if="config.transport !== 'InMemory' || config.persistence !== 'InMemory'">
          <h4>Connection Settings</h4>
          <va-input
            v-if="config.transport === 'Redis' || config.persistence === 'Redis'"
            v-model="config.redisConnection"
            label="Redis Connection"
            placeholder="localhost:6379"
            class="config-input"
          />
          <va-input
            v-if="config.transport === 'NATS' || config.persistence === 'NATS'"
            v-model="config.natsUrl"
            label="NATS URL"
            placeholder="nats://localhost:4222"
            class="config-input"
          />
          <va-input
            v-if="config.persistence === 'SQLite'"
            v-model="config.sqliteConnection"
            label="SQLite Database"
            placeholder="Data Source=orders.db"
            class="config-input"
          />
        </div>

        <va-divider />

        <!-- Cluster Settings -->
        <div class="config-section">
          <h4>Cluster Mode</h4>
          <va-switch v-model="config.clusterEnabled" label="Enable Cluster" />
          <div v-if="config.clusterEnabled" class="cluster-settings">
            <va-input
              v-model="config.clusterNodes"
              label="Cluster Nodes"
              placeholder="node1:5000,node2:5000,node3:5000"
            />
            <va-alert color="info" class="mt-2">
              <template #icon><va-icon name="info" /></template>
              Cluster mode uses Raft consensus for leader election and state replication.
            </va-alert>
          </div>
        </div>

        <va-divider />

        <!-- Actions -->
        <div class="actions">
          <va-button color="primary" @click="saveConfig" :loading="saving">
            <va-icon name="save" class="mr-2" />
            Save Configuration
          </va-button>
          <va-button @click="resetConfig">
            <va-icon name="restore" class="mr-2" />
            Reset to Defaults
          </va-button>
        </div>
      </va-card-content>
    </va-card>

    <!-- Environment Info -->
    <va-card class="mt-4">
      <va-card-title>Environment Information</va-card-title>
      <va-card-content>
        <div class="env-grid">
          <div class="env-item">
            <label>Framework</label>
            <span>Catga CQRS</span>
          </div>
          <div class="env-item">
            <label>Runtime</label>
            <span>.NET 9 (AOT Compatible)</span>
          </div>
          <div class="env-item">
            <label>Frontend</label>
            <span>Vue 3 + Vuestic UI</span>
          </div>
          <div class="env-item">
            <label>API</label>
            <span>{{ apiUrl || 'Same Origin' }}</span>
          </div>
        </div>
      </va-card-content>
    </va-card>

    <!-- Deployment Guide -->
    <va-card class="mt-4">
      <va-card-title>Deployment Guide</va-card-title>
      <va-card-content>
        <va-collapse>
          <va-collapse-item header="Docker Deployment">
            <pre class="code-block">
# Build and run with Docker
docker build -t ordersystem .
docker run -p 5275:5275 ordersystem

# With Redis
docker run -p 5275:5275 \
  -e TRANSPORT=Redis \
  -e PERSISTENCE=Redis \
  -e REDIS_CONNECTION=redis:6379 \
  ordersystem
            </pre>
          </va-collapse-item>
          <va-collapse-item header="Kubernetes Deployment">
            <pre class="code-block">
# Apply Kubernetes manifests
kubectl apply -f k8s/deployment.yaml
kubectl apply -f k8s/service.yaml
kubectl apply -f k8s/configmap.yaml

# Scale replicas
kubectl scale deployment ordersystem --replicas=3
            </pre>
          </va-collapse-item>
          <va-collapse-item header="Environment Variables">
            <pre class="code-block">
TRANSPORT=InMemory|Redis|NATS
PERSISTENCE=InMemory|Redis|NATS|SQLite
REDIS_CONNECTION=localhost:6379
NATS_URL=nats://localhost:4222
SQLITE_CONNECTION=Data Source=orders.db
CLUSTER_ENABLED=true|false
CLUSTER_NODES=node1:5000,node2:5000
            </pre>
          </va-collapse-item>
        </va-collapse>
      </va-card-content>
    </va-card>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, onMounted } from 'vue'
import api from '../api'

const health = ref<{ status: string } | null>(null)
const saving = ref(false)
const apiUrl = import.meta.env.VITE_API_URL

const config = reactive({
  transport: 'InMemory',
  persistence: 'InMemory',
  redisConnection: 'localhost:6379',
  natsUrl: 'nats://localhost:4222',
  sqliteConnection: 'Data Source=orders.db',
  clusterEnabled: false,
  clusterNodes: '',
})

const transportOptions = [
  { value: 'InMemory', text: 'In-Memory (Development)' },
  { value: 'Redis', text: 'Redis (Production)' },
  { value: 'NATS', text: 'NATS (High Performance)' },
]

const persistenceOptions = [
  { value: 'InMemory', text: 'In-Memory (Development)' },
  { value: 'Redis', text: 'Redis (Distributed)' },
  { value: 'NATS', text: 'NATS JetStream' },
  { value: 'SQLite', text: 'SQLite (AOT Compatible)' },
]

async function checkHealth() {
  try {
    const data = await api.getHealth()
    health.value = data
  } catch (e) {
    health.value = { status: 'Unhealthy' }
  }
}

function saveConfig() {
  saving.value = true
  // In a real app, this would call an API to update server config
  setTimeout(() => {
    saving.value = false
    alert('Configuration saved! Restart the server to apply changes.')
  }, 1000)
}

function resetConfig() {
  config.transport = 'InMemory'
  config.persistence = 'InMemory'
  config.redisConnection = 'localhost:6379'
  config.natsUrl = 'nats://localhost:4222'
  config.sqliteConnection = 'Data Source=orders.db'
  config.clusterEnabled = false
  config.clusterNodes = ''
}

onMounted(() => {
  checkHealth()
})
</script>

<style scoped>
.health-section {
  display: flex;
  align-items: center;
  gap: 1rem;
  margin-bottom: 1rem;
}

.health-status {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.uptime {
  font-size: 0.875rem;
  color: #666;
}

.config-section {
  margin: 1rem 0;
}

.config-section h4 {
  margin-bottom: 0.5rem;
}

.description {
  font-size: 0.875rem;
  color: #666;
  margin-bottom: 1rem;
}

.config-option {
  display: block;
  margin: 0.5rem 0;
}

.config-input {
  margin: 0.5rem 0;
  max-width: 400px;
}

.cluster-settings {
  margin-top: 1rem;
  padding: 1rem;
  background: #f8fafc;
  border-radius: 8px;
}

.actions {
  display: flex;
  gap: 1rem;
  margin-top: 1rem;
}

.env-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
  gap: 1rem;
}

.env-item label {
  display: block;
  font-size: 0.75rem;
  color: #666;
}

.env-item span {
  font-weight: 500;
}

.code-block {
  background: #1e293b;
  color: #e2e8f0;
  padding: 1rem;
  border-radius: 8px;
  font-family: monospace;
  font-size: 0.875rem;
  overflow-x: auto;
  white-space: pre;
}

.mt-2 { margin-top: 0.5rem; }
.mt-4 { margin-top: 1rem; }
.mr-2 { margin-right: 0.5rem; }
</style>
