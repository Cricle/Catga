<script setup lang="ts">
import { ref, onMounted } from 'vue'
import api from '../../api'

const syncStatus = ref<any>(null)
const pendingChanges = ref<any[]>([])
const strategies = ref<any>(null)
const loading = ref(false)
const syncing = ref(false)

// Demo form
const demoEntityType = ref('Order')
const demoEntityId = ref('')
const demoChangeType = ref(0)

const changeTypeOptions = [
  { value: 0, text: 'Created (创建)' },
  { value: 1, text: 'Updated (更新)' },
  { value: 2, text: 'Deleted (删除)' }
]

const loadSyncStatus = async () => {
  loading.value = true
  try {
    syncStatus.value = await api.getSyncStatus()
    const result = await api.getPendingChanges()
    pendingChanges.value = result.Changes || []
  } catch (e) {
    console.error('Failed to load sync status:', e)
  } finally {
    loading.value = false
  }
}

const loadStrategies = async () => {
  try {
    strategies.value = await api.getSyncStrategies()
  } catch (e) {
    console.error('Failed to load strategies:', e)
  }
}

const triggerSync = async () => {
  syncing.value = true
  try {
    const result = await api.triggerSync()
    syncStatus.value = { LastSyncTime: result.SyncedAt, Status: 'Active' }
    await loadSyncStatus()
  } catch (e) {
    console.error('Failed to sync:', e)
  } finally {
    syncing.value = false
  }
}

const trackChange = async () => {
  if (!demoEntityId.value) {
    demoEntityId.value = 'ORD-' + Date.now().toString(36).toUpperCase()
  }
  try {
    await api.trackChange(demoEntityType.value, demoEntityId.value, demoChangeType.value)
    demoEntityId.value = ''
    await loadSyncStatus()
  } catch (e) {
    console.error('Failed to track change:', e)
  }
}

const markSynced = async (changeIds: string[]) => {
  try {
    await api.markChangesSynced(changeIds)
    await loadSyncStatus()
  } catch (e) {
    console.error('Failed to mark synced:', e)
  }
}

const formatDate = (date: string) => date ? new Date(date).toLocaleString('zh-CN') : '-'

onMounted(() => {
  loadSyncStatus()
  loadStrategies()
})
</script>

<template>
  <div class="readmodelsync-page">
    <div class="page-header">
      <h1>
        <va-icon name="sync" color="primary" size="large" />
        读模型同步 (Read Model Sync)
      </h1>
      <p class="page-desc">CQRS 读模型同步策略和变更追踪演示</p>
    </div>

    <!-- Status & Actions -->
    <div class="status-grid">
      <va-card class="status-card">
        <va-card-content>
          <div class="status-header">
            <va-icon name="sync_alt" size="2rem" :color="syncStatus?.Status === 'Active' ? 'success' : 'warning'" />
            <div class="status-info">
              <span class="status-label">同步状态</span>
              <span class="status-value">{{ syncStatus?.Status || '未知' }}</span>
            </div>
          </div>
        </va-card-content>
      </va-card>

      <va-card class="status-card">
        <va-card-content>
          <div class="status-header">
            <va-icon name="schedule" size="2rem" color="primary" />
            <div class="status-info">
              <span class="status-label">上次同步时间</span>
              <span class="status-value">{{ formatDate(syncStatus?.LastSyncTime) }}</span>
            </div>
          </div>
        </va-card-content>
      </va-card>

      <va-card class="status-card">
        <va-card-content>
          <div class="status-header">
            <va-icon name="pending_actions" size="2rem" color="warning" />
            <div class="status-info">
              <span class="status-label">待同步变更</span>
              <span class="status-value">{{ pendingChanges.length }}</span>
            </div>
          </div>
        </va-card-content>
      </va-card>

      <va-card class="action-card">
        <va-card-content>
          <va-button color="primary" block @click="triggerSync" :loading="syncing">
            <va-icon name="sync" class="btn-icon" />
            立即同步
          </va-button>
        </va-card-content>
      </va-card>
    </div>

    <div class="main-grid">
      <!-- Track Change Demo -->
      <va-card>
        <va-card-title>
          <va-icon name="add_circle" class="section-icon" />
          追踪变更演示
        </va-card-title>
        <va-card-content>
          <div class="track-form">
            <va-input v-model="demoEntityType" label="实体类型" placeholder="Order" />
            <va-input v-model="demoEntityId" label="实体 ID" placeholder="自动生成" />
            <va-select v-model="demoChangeType" label="变更类型" :options="changeTypeOptions" />
          </div>
          <va-button color="success" @click="trackChange" class="track-btn">
            <va-icon name="add" class="btn-icon" />
            追踪变更
          </va-button>
        </va-card-content>
      </va-card>

      <!-- Pending Changes -->
      <va-card>
        <va-card-title>
          <va-icon name="list" class="section-icon" />
          待同步变更
          <va-button size="small" preset="secondary" @click="loadSyncStatus" :loading="loading" class="refresh-btn">
            <va-icon name="refresh" />
          </va-button>
        </va-card-title>
        <va-card-content>
          <div v-if="pendingChanges.length === 0" class="empty-state">
            <va-icon name="check_circle" size="2rem" color="success" />
            <p>所有变更已同步</p>
          </div>

          <div v-else class="changes-list">
            <div v-for="change in pendingChanges" :key="change.Id" class="change-item">
              <div class="change-info">
                <va-badge
                  :text="['Created', 'Updated', 'Deleted'][change.Type]"
                  :color="['success', 'primary', 'danger'][change.Type]"
                  size="small"
                />
                <span class="change-entity">{{ change.EntityType }}</span>
                <code class="change-id">{{ change.EntityId }}</code>
              </div>
              <div class="change-actions">
                <span class="change-time">{{ formatDate(change.Timestamp) }}</span>
                <va-button size="small" preset="secondary" @click="markSynced([change.Id])">
                  <va-icon name="check" />
                </va-button>
              </div>
            </div>
          </div>
        </va-card-content>
      </va-card>
    </div>

    <!-- Sync Strategies -->
    <va-card>
      <va-card-title>
        <va-icon name="settings_suggest" class="section-icon" />
        同步策略 (Sync Strategies)
      </va-card-title>
      <va-card-content>
        <div v-if="strategies?.Available" class="strategies-grid">
          <div v-for="strategy in strategies.Available" :key="strategy.Name" class="strategy-card">
            <div class="strategy-header">
              <va-icon
                :name="strategy.Name === 'Realtime' ? 'bolt' : strategy.Name === 'Batch' ? 'layers' : 'schedule'"
                size="1.5rem"
                color="primary"
              />
              <h4>{{ strategy.Name }}</h4>
            </div>
            <p class="strategy-desc">{{ strategy.Description }}</p>
            <div class="strategy-usecase">
              <strong>适用场景:</strong> {{ strategy.UseCase }}
            </div>
          </div>
        </div>
      </va-card-content>
    </va-card>

    <!-- Registration Examples -->
    <va-card>
      <va-card-title>
        <va-icon name="code" class="section-icon" />
        注册示例
      </va-card-title>
      <va-card-content>
        <div v-if="strategies?.Registration" class="code-examples">
          <div class="code-block">
            <h4>Realtime (实时)</h4>
            <pre><code>{{ strategies.Registration.Realtime }}</code></pre>
          </div>
          <div class="code-block">
            <h4>Batch (批处理)</h4>
            <pre><code>{{ strategies.Registration.Batch }}</code></pre>
          </div>
          <div class="code-block">
            <h4>Scheduled (定时)</h4>
            <pre><code>{{ strategies.Registration.Scheduled }}</code></pre>
          </div>
        </div>
      </va-card-content>
    </va-card>
  </div>
</template>

<style scoped>
.readmodelsync-page {
  max-width: 1200px;
  margin: 0 auto;
}

.page-header {
  margin-bottom: 2rem;
}

.page-header h1 {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  font-size: 1.75rem;
  margin: 0 0 0.5rem;
}

.page-desc {
  color: #666;
  margin: 0;
}

.status-grid {
  display: grid;
  grid-template-columns: repeat(4, 1fr);
  gap: 1rem;
  margin-bottom: 1.5rem;
}

@media (max-width: 900px) {
  .status-grid {
    grid-template-columns: repeat(2, 1fr);
  }
}

.status-card .va-card-content {
  padding: 1rem;
}

.status-header {
  display: flex;
  align-items: center;
  gap: 1rem;
}

.status-info {
  display: flex;
  flex-direction: column;
}

.status-label {
  font-size: 0.75rem;
  color: #666;
}

.status-value {
  font-size: 1.125rem;
  font-weight: 600;
}

.action-card .va-card-content {
  display: flex;
  align-items: center;
  justify-content: center;
  height: 100%;
}

.main-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 1.5rem;
  margin-bottom: 1.5rem;
}

@media (max-width: 900px) {
  .main-grid {
    grid-template-columns: 1fr;
  }
}

.section-icon {
  margin-right: 0.5rem;
  color: var(--va-primary);
}

.refresh-btn {
  margin-left: auto;
}

.btn-icon {
  margin-right: 0.25rem;
}

.track-form {
  display: flex;
  flex-direction: column;
  gap: 1rem;
  margin-bottom: 1rem;
}

.track-btn {
  width: 100%;
}

.empty-state {
  text-align: center;
  padding: 2rem;
  color: #666;
}

.empty-state p {
  margin: 0.5rem 0 0;
}

.changes-list {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
  max-height: 300px;
  overflow-y: auto;
}

.change-item {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 0.75rem;
  background: #f8fafc;
  border-radius: 8px;
}

.change-info {
  display: flex;
  align-items: center;
  gap: 0.75rem;
}

.change-entity {
  font-weight: 500;
}

.change-id {
  background: #e2e8f0;
  padding: 0.125rem 0.5rem;
  border-radius: 4px;
  font-size: 0.75rem;
}

.change-actions {
  display: flex;
  align-items: center;
  gap: 0.75rem;
}

.change-time {
  font-size: 0.75rem;
  color: #666;
}

.strategies-grid {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 1.5rem;
}

@media (max-width: 900px) {
  .strategies-grid {
    grid-template-columns: 1fr;
  }
}

.strategy-card {
  padding: 1.5rem;
  background: #f8fafc;
  border-radius: 12px;
  border: 1px solid #e2e8f0;
}

.strategy-header {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  margin-bottom: 0.75rem;
}

.strategy-header h4 {
  margin: 0;
  font-size: 1.125rem;
}

.strategy-desc {
  color: #666;
  margin: 0 0 0.75rem;
  font-size: 0.875rem;
}

.strategy-usecase {
  font-size: 0.8rem;
  color: #64748b;
}

.code-examples {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.code-block h4 {
  margin: 0 0 0.5rem;
  font-size: 0.875rem;
  color: #666;
}

.code-block pre {
  background: #1e293b;
  color: #e2e8f0;
  padding: 1rem;
  border-radius: 8px;
  overflow-x: auto;
  font-size: 0.75rem;
  margin: 0;
}
</style>
