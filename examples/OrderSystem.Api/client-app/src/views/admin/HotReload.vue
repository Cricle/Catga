<script setup lang="ts">
import { ref, onMounted } from 'vue'
import api from '../../api'

const flows = ref<string[]>([])
const loading = ref(false)
const newFlowName = ref('')
const selectedFlow = ref<any>(null)
const reloadResult = ref<any>(null)
const eventInfo = ref<any>(null)

const loadFlows = async () => {
  loading.value = true
  try {
    const result = await api.getRegisteredFlows()
    flows.value = result.Flows || []
  } catch (e) {
    console.error('Failed to load flows:', e)
  } finally {
    loading.value = false
  }
}

const loadFlowDetails = async (flowName: string) => {
  try {
    selectedFlow.value = await api.getFlowDetails(flowName)
  } catch (e) {
    selectedFlow.value = null
  }
}

const registerFlow = async () => {
  if (!newFlowName.value) return
  try {
    await api.registerFlow(newFlowName.value)
    newFlowName.value = ''
    await loadFlows()
  } catch (e) {
    console.error('Failed to register flow:', e)
  }
}

const reloadFlow = async (flowName: string) => {
  try {
    reloadResult.value = await api.reloadFlow(flowName)
    await loadFlowDetails(flowName)
  } catch (e) {
    console.error('Failed to reload flow:', e)
  }
}

const unregisterFlow = async (flowName: string) => {
  try {
    await api.unregisterFlow(flowName)
    selectedFlow.value = null
    await loadFlows()
  } catch (e) {
    console.error('Failed to unregister flow:', e)
  }
}

const loadEventInfo = async () => {
  try {
    eventInfo.value = await api.getReloadEventInfo()
  } catch (e) {
    console.error('Failed to load event info:', e)
  }
}

onMounted(() => {
  loadFlows()
  loadEventInfo()
})
</script>

<template>
  <div class="hotreload-page">
    <div class="page-header">
      <h1>
        <va-icon name="autorenew" color="primary" size="large" />
        Flow 热重载 (Hot Reload)
      </h1>
      <p class="page-desc">动态注册、版本管理和 Flow 重载演示</p>
    </div>

    <div class="main-grid">
      <!-- Flow Registry -->
      <va-card>
        <va-card-title>
          <va-icon name="folder_special" class="section-icon" />
          已注册的 Flows
          <va-button size="small" preset="secondary" @click="loadFlows" :loading="loading" class="refresh-btn">
            <va-icon name="refresh" />
          </va-button>
        </va-card-title>
        <va-card-content>
          <div class="register-form">
            <va-input v-model="newFlowName" placeholder="输入 Flow 名称" size="small" />
            <va-button size="small" @click="registerFlow" :disabled="!newFlowName">
              <va-icon name="add" class="btn-icon" />
              注册
            </va-button>
          </div>

          <va-divider />

          <div v-if="flows.length === 0" class="empty-state">
            <va-icon name="inbox" size="2rem" color="secondary" />
            <p>暂无注册的 Flow</p>
          </div>

          <div v-else class="flow-list">
            <div
              v-for="flow in flows"
              :key="flow"
              class="flow-item"
              :class="{ active: selectedFlow?.FlowName === flow }"
              @click="loadFlowDetails(flow)"
            >
              <va-icon name="schema" class="flow-icon" />
              <span class="flow-name">{{ flow }}</span>
              <va-icon name="chevron_right" class="chevron" />
            </div>
          </div>
        </va-card-content>
      </va-card>

      <!-- Flow Details -->
      <va-card>
        <va-card-title>
          <va-icon name="info" class="section-icon" />
          Flow 详情
        </va-card-title>
        <va-card-content>
          <div v-if="!selectedFlow" class="empty-state">
            <va-icon name="touch_app" size="2rem" color="secondary" />
            <p>选择一个 Flow 查看详情</p>
          </div>

          <div v-else class="flow-details">
            <div class="detail-row">
              <span class="detail-label">名称</span>
              <span class="detail-value">{{ selectedFlow.FlowName }}</span>
            </div>
            <div class="detail-row">
              <span class="detail-label">版本</span>
              <va-badge :text="`v${selectedFlow.Version}`" color="primary" />
            </div>
            <div class="detail-row">
              <span class="detail-label">配置类型</span>
              <code>{{ selectedFlow.ConfigType }}</code>
            </div>
            <div class="detail-row">
              <span class="detail-label">状态</span>
              <va-badge text="已注册" color="success" />
            </div>

            <va-divider />

            <div class="detail-actions">
              <va-button color="primary" @click="reloadFlow(selectedFlow.FlowName)">
                <va-icon name="autorenew" class="btn-icon" />
                重载 Flow
              </va-button>
              <va-button color="danger" preset="secondary" @click="unregisterFlow(selectedFlow.FlowName)">
                <va-icon name="delete" class="btn-icon" />
                注销
              </va-button>
            </div>

            <va-alert v-if="reloadResult" color="success" class="reload-result">
              <strong>重载成功!</strong>
              <p>{{ reloadResult.OldVersion }} → {{ reloadResult.NewVersion }}</p>
            </va-alert>
          </div>
        </va-card-content>
      </va-card>
    </div>

    <!-- Event Info -->
    <va-card class="event-card">
      <va-card-title>
        <va-icon name="notifications_active" class="section-icon" />
        重载事件 (FlowReloadedEvent)
      </va-card-title>
      <va-card-content>
        <div v-if="eventInfo" class="event-info">
          <div class="event-properties">
            <h4>事件属性:</h4>
            <ul>
              <li v-for="prop in eventInfo.Properties" :key="prop">{{ prop }}</li>
            </ul>
          </div>
          <div class="event-usage">
            <h4>使用示例:</h4>
            <pre><code>{{ eventInfo.Usage }}</code></pre>
          </div>
        </div>
      </va-card-content>
    </va-card>

    <!-- Version Manager -->
    <va-card>
      <va-card-title>
        <va-icon name="history" class="section-icon" />
        版本管理器 (IFlowVersionManager)
      </va-card-title>
      <va-card-content>
        <div class="version-info">
          <div class="info-block">
            <h4>接口方法</h4>
            <ul>
              <li><code>GetCurrentVersion(flowName)</code> - 获取当前版本</li>
              <li><code>SetVersion(flowName, version)</code> - 设置版本</li>
              <li><code>IncrementVersion(flowName)</code> - 版本号 +1</li>
            </ul>
          </div>
          <div class="info-block">
            <h4>使用场景</h4>
            <ul>
              <li>在 Flow 执行前检查版本兼容性</li>
              <li>记录 Flow 配置变更历史</li>
              <li>支持 Flow 配置回滚</li>
            </ul>
          </div>
        </div>
      </va-card-content>
    </va-card>
  </div>
</template>

<style scoped>
.hotreload-page {
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

.register-form {
  display: flex;
  gap: 0.5rem;
  margin-bottom: 1rem;
}

.register-form .va-input {
  flex: 1;
}

.btn-icon {
  margin-right: 0.25rem;
}

.empty-state {
  text-align: center;
  padding: 2rem;
  color: #666;
}

.empty-state p {
  margin: 0.5rem 0 0;
}

.flow-list {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.flow-item {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  padding: 0.75rem 1rem;
  background: #f8fafc;
  border-radius: 8px;
  cursor: pointer;
  transition: all 0.2s;
}

.flow-item:hover {
  background: #e2e8f0;
}

.flow-item.active {
  background: #dbeafe;
  border-left: 3px solid var(--va-primary);
}

.flow-icon {
  color: var(--va-primary);
}

.flow-name {
  flex: 1;
  font-weight: 500;
}

.chevron {
  color: #94a3b8;
}

.flow-details {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.detail-row {
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.detail-label {
  color: #666;
  font-size: 0.875rem;
}

.detail-value {
  font-weight: 500;
}

.detail-actions {
  display: flex;
  gap: 0.75rem;
  margin-top: 0.5rem;
}

.reload-result {
  margin-top: 1rem;
}

.reload-result p {
  margin: 0.25rem 0 0;
}

.event-card {
  margin-bottom: 1.5rem;
}

.event-info {
  display: grid;
  grid-template-columns: 1fr 2fr;
  gap: 2rem;
}

@media (max-width: 768px) {
  .event-info {
    grid-template-columns: 1fr;
  }
}

.event-properties h4, .event-usage h4 {
  margin: 0 0 0.75rem;
  font-size: 0.875rem;
  color: #666;
}

.event-properties ul {
  margin: 0;
  padding-left: 1.25rem;
}

.event-properties li {
  margin: 0.25rem 0;
  font-size: 0.875rem;
}

.event-usage pre {
  background: #1e293b;
  color: #e2e8f0;
  padding: 1rem;
  border-radius: 8px;
  overflow-x: auto;
  font-size: 0.75rem;
  margin: 0;
}

.version-info {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(250px, 1fr));
  gap: 2rem;
}

.info-block h4 {
  margin: 0 0 0.75rem;
  font-size: 0.875rem;
  color: #666;
}

.info-block ul {
  margin: 0;
  padding-left: 1.25rem;
}

.info-block li {
  margin: 0.5rem 0;
}

.info-block code {
  background: #f1f5f9;
  padding: 0.125rem 0.375rem;
  border-radius: 4px;
  font-size: 0.8rem;
}
</style>
