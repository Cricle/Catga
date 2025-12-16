<script setup lang="ts">
import { ref, onMounted } from 'vue'
import api from '../../api'

const syncStatus = ref<any>(null)
const pendingChanges = ref<any[]>([])
const strategies = ref<any>(null)
const loading = ref(false)
const syncing = ref(false)

const demoEntityType = ref('Order')
const demoEntityId = ref('')
const demoChangeType = ref(0)

const changeTypeOptions = [
  { value: 0, text: 'Created' },
  { value: 1, text: 'Updated' },
  { value: 2, text: 'Deleted' }
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
    await api.triggerSync()
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
  <div>
    <va-card class="mb-4">
      <va-card-title>
        <va-icon name="sync" class="mr-2" /> 读模型同步 (Read Model Sync)
      </va-card-title>
      <va-card-content>
        <p>CQRS 读模型同步策略和变更追踪演示</p>
      </va-card-content>
    </va-card>

    <div class="row mb-4">
      <div class="flex md3 xs6">
        <va-card>
          <va-card-content class="text-center">
            <va-icon name="sync_alt" size="2rem" :color="syncStatus?.Status === 'Active' ? 'success' : 'warning'" />
            <p class="mb-0 mt-2"><strong>{{ syncStatus?.Status || '未知' }}</strong></p>
            <small>同步状态</small>
          </va-card-content>
        </va-card>
      </div>
      <div class="flex md3 xs6">
        <va-card>
          <va-card-content class="text-center">
            <va-icon name="schedule" size="2rem" color="primary" />
            <p class="mb-0 mt-2"><strong>{{ formatDate(syncStatus?.LastSyncTime) }}</strong></p>
            <small>上次同步</small>
          </va-card-content>
        </va-card>
      </div>
      <div class="flex md3 xs6">
        <va-card>
          <va-card-content class="text-center">
            <va-icon name="pending_actions" size="2rem" color="warning" />
            <p class="mb-0 mt-2"><strong>{{ pendingChanges.length }}</strong></p>
            <small>待同步变更</small>
          </va-card-content>
        </va-card>
      </div>
      <div class="flex md3 xs6">
        <va-card>
          <va-card-content class="text-center">
            <va-button color="primary" @click="triggerSync" :loading="syncing">
              <va-icon name="sync" class="mr-1" /> 立即同步
            </va-button>
          </va-card-content>
        </va-card>
      </div>
    </div>

    <div class="row">
      <div class="flex md6 xs12">
        <va-card class="mb-4">
          <va-card-title><va-icon name="add_circle" class="mr-2" /> 追踪变更演示</va-card-title>
          <va-card-content>
            <va-input v-model="demoEntityType" label="实体类型" class="mb-3" />
            <va-input v-model="demoEntityId" label="实体 ID (可留空自动生成)" class="mb-3" />
            <va-select v-model="demoChangeType" label="变更类型" :options="changeTypeOptions" class="mb-3" />
            <va-button color="success" block @click="trackChange">
              <va-icon name="add" class="mr-1" /> 追踪变更
            </va-button>
          </va-card-content>
        </va-card>
      </div>

      <div class="flex md6 xs12">
        <va-card class="mb-4">
          <va-card-title>
            <va-icon name="list" class="mr-2" /> 待同步变更
            <va-button size="small" preset="secondary" class="ml-auto" @click="loadSyncStatus" :loading="loading">
              <va-icon name="refresh" />
            </va-button>
          </va-card-title>
          <va-card-content>
            <va-inner-loading :loading="loading">
              <div v-if="pendingChanges.length === 0" class="text-center pa-4">
                <va-icon name="check_circle" size="2rem" color="success" />
                <p class="mt-2">所有变更已同步</p>
              </div>
              <va-list v-else>
                <va-list-item v-for="change in pendingChanges" :key="change.Id">
                  <va-list-item-section avatar>
                    <va-badge :text="['C', 'U', 'D'][change.Type]" :color="['success', 'primary', 'danger'][change.Type]" />
                  </va-list-item-section>
                  <va-list-item-section>
                    <va-list-item-label>{{ change.EntityType }} - {{ change.EntityId }}</va-list-item-label>
                    <va-list-item-label caption>{{ formatDate(change.Timestamp) }}</va-list-item-label>
                  </va-list-item-section>
                  <va-list-item-section icon>
                    <va-button size="small" preset="secondary" @click="markSynced([change.Id])">
                      <va-icon name="check" />
                    </va-button>
                  </va-list-item-section>
                </va-list-item>
              </va-list>
            </va-inner-loading>
          </va-card-content>
        </va-card>
      </div>
    </div>

    <va-card class="mb-4">
      <va-card-title><va-icon name="settings_suggest" class="mr-2" /> 同步策略</va-card-title>
      <va-card-content>
        <div class="row" v-if="strategies?.Available">
          <div class="flex md4 xs12" v-for="strategy in strategies.Available" :key="strategy.Name">
            <va-card outlined class="mb-3">
              <va-card-content>
                <div class="d-flex align-center mb-2">
                  <va-icon :name="strategy.Name === 'Realtime' ? 'bolt' : strategy.Name === 'Batch' ? 'layers' : 'schedule'" color="primary" class="mr-2" />
                  <strong>{{ strategy.Name }}</strong>
                </div>
                <p class="mb-2">{{ strategy.Description }}</p>
                <small class="text-secondary">适用: {{ strategy.UseCase }}</small>
              </va-card-content>
            </va-card>
          </div>
        </div>
      </va-card-content>
    </va-card>

    <va-card>
      <va-card-title><va-icon name="code" class="mr-2" /> 注册示例</va-card-title>
      <va-card-content>
        <div v-if="strategies?.Registration">
          <h4 class="mb-2">Realtime</h4>
          <va-code-block language="csharp" class="mb-3">{{ strategies.Registration.Realtime }}</va-code-block>
          <h4 class="mb-2">Batch</h4>
          <va-code-block language="csharp" class="mb-3">{{ strategies.Registration.Batch }}</va-code-block>
          <h4 class="mb-2">Scheduled</h4>
          <va-code-block language="csharp">{{ strategies.Registration.Scheduled }}</va-code-block>
        </div>
      </va-card-content>
    </va-card>
  </div>
</template>
