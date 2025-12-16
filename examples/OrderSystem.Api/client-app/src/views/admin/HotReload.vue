<script setup lang="ts">
import { ref, onMounted } from 'vue'
import api from '../../api'

const flows = ref<string[]>([])
const loading = ref(false)
const newFlowName = ref('')
const selectedFlow = ref<any>(null)
const reloadResult = ref<any>(null)

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
    reloadResult.value = null
    await loadFlows()
  } catch (e) {
    console.error('Failed to unregister flow:', e)
  }
}

onMounted(loadFlows)
</script>

<template>
  <div>
    <va-card class="mb-4">
      <va-card-title>
        <va-icon name="autorenew" class="mr-2" /> Flow 热重载 (Hot Reload)
      </va-card-title>
      <va-card-content>
        <p>动态注册、版本管理和 Flow 重载演示</p>
      </va-card-content>
    </va-card>

    <div class="row">
      <div class="flex md6 xs12">
        <va-card class="mb-4">
          <va-card-title>
            <va-icon name="folder_special" class="mr-2" /> 已注册的 Flows
            <va-button size="small" preset="secondary" class="ml-auto" @click="loadFlows" :loading="loading">
              <va-icon name="refresh" />
            </va-button>
          </va-card-title>
          <va-card-content>
            <div class="row mb-3">
              <div class="flex xs8">
                <va-input v-model="newFlowName" placeholder="Flow 名称" size="small" />
              </div>
              <div class="flex xs4">
                <va-button size="small" block @click="registerFlow" :disabled="!newFlowName">
                  <va-icon name="add" class="mr-1" /> 注册
                </va-button>
              </div>
            </div>
            <va-divider />
            <va-inner-loading :loading="loading">
              <div v-if="flows.length === 0" class="text-center pa-4">
                <va-icon name="inbox" size="2rem" color="secondary" />
                <p class="mt-2">暂无注册的 Flow</p>
              </div>
              <va-list v-else>
                <va-list-item v-for="flow in flows" :key="flow" @click="loadFlowDetails(flow)" :class="{ 'va-list-item--active': selectedFlow?.FlowName === flow }">
                  <va-list-item-section avatar><va-icon name="schema" color="primary" /></va-list-item-section>
                  <va-list-item-section><va-list-item-label>{{ flow }}</va-list-item-label></va-list-item-section>
                  <va-list-item-section icon><va-icon name="chevron_right" /></va-list-item-section>
                </va-list-item>
              </va-list>
            </va-inner-loading>
          </va-card-content>
        </va-card>
      </div>

      <div class="flex md6 xs12">
        <va-card class="mb-4">
          <va-card-title><va-icon name="info" class="mr-2" /> Flow 详情</va-card-title>
          <va-card-content>
            <div v-if="!selectedFlow" class="text-center pa-4">
              <va-icon name="touch_app" size="2rem" color="secondary" />
              <p class="mt-2">选择一个 Flow 查看详情</p>
            </div>
            <div v-else>
              <va-list>
                <va-list-item>
                  <va-list-item-section><va-list-item-label caption>名称</va-list-item-label></va-list-item-section>
                  <va-list-item-section><va-list-item-label>{{ selectedFlow.FlowName }}</va-list-item-label></va-list-item-section>
                </va-list-item>
                <va-list-item>
                  <va-list-item-section><va-list-item-label caption>版本</va-list-item-label></va-list-item-section>
                  <va-list-item-section><va-badge :text="`v${selectedFlow.Version}`" color="primary" /></va-list-item-section>
                </va-list-item>
                <va-list-item>
                  <va-list-item-section><va-list-item-label caption>配置类型</va-list-item-label></va-list-item-section>
                  <va-list-item-section><code>{{ selectedFlow.ConfigType }}</code></va-list-item-section>
                </va-list-item>
                <va-list-item>
                  <va-list-item-section><va-list-item-label caption>状态</va-list-item-label></va-list-item-section>
                  <va-list-item-section><va-badge text="已注册" color="success" /></va-list-item-section>
                </va-list-item>
              </va-list>
              <va-divider class="my-3" />
              <va-button color="primary" class="mr-2" @click="reloadFlow(selectedFlow.FlowName)">
                <va-icon name="autorenew" class="mr-1" /> 重载
              </va-button>
              <va-button color="danger" preset="secondary" @click="unregisterFlow(selectedFlow.FlowName)">
                <va-icon name="delete" class="mr-1" /> 注销
              </va-button>
              <va-alert v-if="reloadResult" color="success" class="mt-3">
                重载成功! v{{ reloadResult.OldVersion }} → v{{ reloadResult.NewVersion }}
              </va-alert>
            </div>
          </va-card-content>
        </va-card>
      </div>
    </div>

    <va-card class="mb-4">
      <va-card-title><va-icon name="history" class="mr-2" /> 版本管理器接口</va-card-title>
      <va-card-content>
        <va-list>
          <va-list-item>
            <va-list-item-section avatar><va-icon name="code" /></va-list-item-section>
            <va-list-item-section>
              <va-list-item-label><code>GetCurrentVersion(flowName)</code></va-list-item-label>
              <va-list-item-label caption>获取当前版本</va-list-item-label>
            </va-list-item-section>
          </va-list-item>
          <va-list-item>
            <va-list-item-section avatar><va-icon name="code" /></va-list-item-section>
            <va-list-item-section>
              <va-list-item-label><code>SetVersion(flowName, version)</code></va-list-item-label>
              <va-list-item-label caption>设置版本</va-list-item-label>
            </va-list-item-section>
          </va-list-item>
          <va-list-item>
            <va-list-item-section avatar><va-icon name="code" /></va-list-item-section>
            <va-list-item-section>
              <va-list-item-label><code>IncrementVersion(flowName)</code></va-list-item-label>
              <va-list-item-label caption>版本号 +1</va-list-item-label>
            </va-list-item-section>
          </va-list-item>
        </va-list>
      </va-card-content>
    </va-card>
  </div>
</template>
