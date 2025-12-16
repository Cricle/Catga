<script setup lang="ts">
import { ref, onMounted } from 'vue'
import api from '../../api'

const metricsInfo = ref<any>(null)
const loading = ref(false)
const demoResult = ref<any>(null)
const demoFlowName = ref('OrderFlow')
const demoDuration = ref(150)
const demoError = ref('Connection timeout')

const loadMetricsInfo = async () => {
  loading.value = true
  try {
    metricsInfo.value = await api.getObservabilityMetrics()
  } catch (e) {
    console.error('Failed to load metrics info:', e)
  } finally {
    loading.value = false
  }
}

const recordFlowDemo = async () => {
  try {
    demoResult.value = await api.recordFlowDemo(demoFlowName.value, demoDuration.value)
  } catch (e) {
    console.error('Failed to record flow:', e)
  }
}

const recordFailureDemo = async () => {
  try {
    demoResult.value = await api.recordFailureDemo(demoFlowName.value, demoError.value)
  } catch (e) {
    console.error('Failed to record failure:', e)
  }
}

onMounted(loadMetricsInfo)
</script>

<template>
  <div>
    <va-card class="mb-4">
      <va-card-title>
        <va-icon name="monitoring" class="mr-2" /> 可观测性 (Observability)
      </va-card-title>
      <va-card-content>
        <p>Flow DSL 的指标、追踪和日志集成演示</p>
      </va-card-content>
    </va-card>

    <div class="row">
      <div class="flex md6 xs12">
        <va-card class="mb-4">
          <va-card-title><va-icon name="analytics" class="mr-2" /> 可用指标</va-card-title>
          <va-card-content>
            <va-inner-loading :loading="loading">
              <div v-if="metricsInfo?.FlowMetrics">
                <h4 class="mb-2">计数器 (Counters)</h4>
                <va-chip v-for="c in metricsInfo.FlowMetrics.Counters" :key="c" size="small" color="primary" class="mr-1 mb-1">{{ c }}</va-chip>
                <h4 class="mt-3 mb-2">直方图 (Histograms)</h4>
                <va-chip v-for="h in metricsInfo.FlowMetrics.Histograms" :key="h" size="small" color="success" class="mr-1 mb-1">{{ h }}</va-chip>
                <h4 class="mt-3 mb-2">仪表 (Gauges)</h4>
                <va-chip v-for="g in metricsInfo.FlowMetrics.Gauges" :key="g" size="small" color="warning" class="mr-1 mb-1">{{ g }}</va-chip>
              </div>
            </va-inner-loading>
          </va-card-content>
        </va-card>
      </div>

      <div class="flex md6 xs12">
        <va-card class="mb-4">
          <va-card-title><va-icon name="account_tree" class="mr-2" /> 追踪标签</va-card-title>
          <va-card-content>
            <div v-if="metricsInfo?.TracingTags">
              <h4 class="mb-2">Flow 标签</h4>
              <va-chip v-for="t in metricsInfo.TracingTags.FlowTags" :key="t" size="small" outline class="mr-1 mb-1">{{ t }}</va-chip>
              <h4 class="mt-3 mb-2">Step 标签</h4>
              <va-chip v-for="t in metricsInfo.TracingTags.StepTags" :key="t" size="small" outline class="mr-1 mb-1">{{ t }}</va-chip>
              <h4 class="mt-3 mb-2">错误标签</h4>
              <va-chip v-for="t in metricsInfo.TracingTags.ErrorTags" :key="t" size="small" color="danger" outline class="mr-1 mb-1">{{ t }}</va-chip>
            </div>
          </va-card-content>
        </va-card>
      </div>
    </div>

    <va-card class="mb-4">
      <va-card-title><va-icon name="science" class="mr-2" /> 实时演示</va-card-title>
      <va-card-content>
        <div class="row">
          <div class="flex md4 xs12">
            <va-input v-model="demoFlowName" label="Flow 名称" class="mb-3" />
          </div>
          <div class="flex md4 xs12">
            <va-input v-model.number="demoDuration" label="执行时长 (ms)" type="number" class="mb-3" />
          </div>
          <div class="flex md4 xs12">
            <va-input v-model="demoError" label="错误信息" class="mb-3" />
          </div>
        </div>
        <va-button color="success" class="mr-2" @click="recordFlowDemo">
          <va-icon name="check_circle" class="mr-1" /> 记录成功
        </va-button>
        <va-button color="danger" @click="recordFailureDemo">
          <va-icon name="error" class="mr-1" /> 记录失败
        </va-button>
        <va-alert v-if="demoResult" color="info" class="mt-3">
          <pre class="ma-0">{{ JSON.stringify(demoResult, null, 2) }}</pre>
        </va-alert>
      </va-card-content>
    </va-card>

    <va-card class="mb-4">
      <va-card-title><va-icon name="timeline" class="mr-2" /> 追踪事件</va-card-title>
      <va-card-content>
        <va-list v-if="metricsInfo?.TracingEvents">
          <va-list-item v-for="event in metricsInfo.TracingEvents" :key="event">
            <va-list-item-section avatar>
              <va-icon :name="event.includes('failed') ? 'error' : 'check_circle'" :color="event.includes('failed') ? 'danger' : 'success'" />
            </va-list-item-section>
            <va-list-item-section>
              <va-list-item-label>{{ event }}</va-list-item-label>
            </va-list-item-section>
          </va-list-item>
        </va-list>
      </va-card-content>
    </va-card>

    <va-card>
      <va-card-title><va-icon name="dashboard" class="mr-2" /> Grafana 仪表盘</va-card-title>
      <va-card-content>
        <p><strong>位置:</strong> <code>src/Catga/Observability/GrafanaDashboard.json</code></p>
        <va-list>
          <va-list-item v-for="panel in ['Flow执行概览', '活跃Flow数量', '成功率', '执行时长分布', 'Step分析', 'Top Flows', '最慢Steps']" :key="panel">
            <va-list-item-section avatar><va-icon name="bar_chart" /></va-list-item-section>
            <va-list-item-section><va-list-item-label>{{ panel }}</va-list-item-label></va-list-item-section>
          </va-list-item>
        </va-list>
      </va-card-content>
    </va-card>
  </div>
</template>
