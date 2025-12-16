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
  <div class="observability-page">
    <div class="page-header">
      <h1>
        <va-icon name="monitoring" color="primary" size="large" />
        可观测性 (Observability)
      </h1>
      <p class="page-desc">Flow DSL 的指标、追踪和日志集成演示</p>
    </div>

    <!-- Metrics Overview -->
    <div class="section-grid">
      <va-card>
        <va-card-title>
          <va-icon name="analytics" class="section-icon" />
          可用指标 (Metrics)
        </va-card-title>
        <va-card-content>
          <div v-if="metricsInfo?.FlowMetrics" class="metrics-list">
            <div class="metric-group">
              <h4>计数器 (Counters)</h4>
              <va-chip v-for="c in metricsInfo.FlowMetrics.Counters" :key="c" size="small" color="primary" outline>
                {{ c }}
              </va-chip>
            </div>
            <div class="metric-group">
              <h4>直方图 (Histograms)</h4>
              <va-chip v-for="h in metricsInfo.FlowMetrics.Histograms" :key="h" size="small" color="success" outline>
                {{ h }}
              </va-chip>
            </div>
            <div class="metric-group">
              <h4>仪表 (Gauges)</h4>
              <va-chip v-for="g in metricsInfo.FlowMetrics.Gauges" :key="g" size="small" color="warning" outline>
                {{ g }}
              </va-chip>
            </div>
          </div>
          <va-progress-bar v-else indeterminate />
        </va-card-content>
      </va-card>

      <va-card>
        <va-card-title>
          <va-icon name="account_tree" class="section-icon" />
          追踪标签 (Tracing Tags)
        </va-card-title>
        <va-card-content>
          <div v-if="metricsInfo?.TracingTags" class="tags-list">
            <div class="tag-group">
              <h4>Flow 标签</h4>
              <code v-for="t in metricsInfo.TracingTags.FlowTags" :key="t" class="tag-code">{{ t }}</code>
            </div>
            <div class="tag-group">
              <h4>Step 标签</h4>
              <code v-for="t in metricsInfo.TracingTags.StepTags" :key="t" class="tag-code">{{ t }}</code>
            </div>
            <div class="tag-group">
              <h4>错误标签</h4>
              <code v-for="t in metricsInfo.TracingTags.ErrorTags" :key="t" class="tag-code error">{{ t }}</code>
            </div>
          </div>
        </va-card-content>
      </va-card>
    </div>

    <!-- Demo Section -->
    <va-card class="demo-card">
      <va-card-title>
        <va-icon name="science" class="section-icon" />
        实时演示 - 记录 Flow 指标
      </va-card-title>
      <va-card-content>
        <div class="demo-form">
          <va-input v-model="demoFlowName" label="Flow 名称" placeholder="OrderFlow" />
          <va-input v-model.number="demoDuration" label="执行时长 (ms)" type="number" />
          <va-input v-model="demoError" label="错误信息 (用于失败演示)" />
        </div>
        <div class="demo-actions">
          <va-button color="success" @click="recordFlowDemo">
            <va-icon name="check_circle" class="btn-icon" />
            记录成功的 Flow
          </va-button>
          <va-button color="danger" @click="recordFailureDemo">
            <va-icon name="error" class="btn-icon" />
            记录失败的 Flow
          </va-button>
        </div>
        <va-alert v-if="demoResult" color="info" class="demo-result">
          <pre>{{ JSON.stringify(demoResult, null, 2) }}</pre>
        </va-alert>
      </va-card-content>
    </va-card>

    <!-- Tracing Events -->
    <va-card>
      <va-card-title>
        <va-icon name="timeline" class="section-icon" />
        追踪事件 (Tracing Events)
      </va-card-title>
      <va-card-content>
        <div v-if="metricsInfo?.TracingEvents" class="events-timeline">
          <div v-for="(event, index) in metricsInfo.TracingEvents" :key="event" class="event-item">
            <div class="event-dot" :class="{ 'error': event.includes('failed') }"></div>
            <div class="event-content">
              <code>{{ event }}</code>
            </div>
          </div>
        </div>
      </va-card-content>
    </va-card>

    <!-- Grafana Info -->
    <va-card>
      <va-card-title>
        <va-icon name="dashboard" class="section-icon" />
        Grafana 仪表盘
      </va-card-title>
      <va-card-content>
        <div class="grafana-info">
          <p><strong>仪表盘位置:</strong> <code>src/Catga/Observability/GrafanaDashboard.json</code></p>
          <div class="grafana-panels">
            <h4>包含面板:</h4>
            <ul>
              <li>Flow 执行概览</li>
              <li>活跃 Flow 数量</li>
              <li>成功率统计</li>
              <li>执行时长分布</li>
              <li>Step 分析</li>
              <li>Top Flows</li>
              <li>最慢 Steps</li>
            </ul>
          </div>
        </div>
      </va-card-content>
    </va-card>
  </div>
</template>

<style scoped>
.observability-page {
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

.section-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(400px, 1fr));
  gap: 1.5rem;
  margin-bottom: 1.5rem;
}

.section-icon {
  margin-right: 0.5rem;
  color: var(--va-primary);
}

.metrics-list, .tags-list {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.metric-group, .tag-group {
  margin-bottom: 0.5rem;
}

.metric-group h4, .tag-group h4 {
  font-size: 0.875rem;
  color: #666;
  margin: 0 0 0.5rem;
}

.metric-group .va-chip {
  margin: 0.25rem;
}

.tag-code {
  display: inline-block;
  background: #f1f5f9;
  padding: 0.25rem 0.5rem;
  border-radius: 4px;
  font-size: 0.75rem;
  margin: 0.25rem;
}

.tag-code.error {
  background: #fee2e2;
  color: #dc2626;
}

.demo-card {
  margin-bottom: 1.5rem;
}

.demo-form {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
  gap: 1rem;
  margin-bottom: 1rem;
}

.demo-actions {
  display: flex;
  gap: 1rem;
  flex-wrap: wrap;
}

.btn-icon {
  margin-right: 0.5rem;
}

.demo-result {
  margin-top: 1rem;
}

.demo-result pre {
  margin: 0;
  white-space: pre-wrap;
  font-size: 0.75rem;
}

.events-timeline {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
  padding-left: 1rem;
}

.event-item {
  display: flex;
  align-items: center;
  gap: 1rem;
}

.event-dot {
  width: 12px;
  height: 12px;
  border-radius: 50%;
  background: #4ade80;
  flex-shrink: 0;
}

.event-dot.error {
  background: #f87171;
}

.event-content code {
  background: #f1f5f9;
  padding: 0.25rem 0.75rem;
  border-radius: 4px;
  font-size: 0.875rem;
}

.grafana-info p {
  margin: 0 0 1rem;
}

.grafana-panels h4 {
  margin: 0 0 0.5rem;
  font-size: 0.875rem;
}

.grafana-panels ul {
  margin: 0;
  padding-left: 1.5rem;
}

.grafana-panels li {
  margin: 0.25rem 0;
  color: #666;
}
</style>
