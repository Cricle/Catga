<script setup lang="ts">
import { ref, onMounted } from 'vue'
import axios from 'axios'

interface SystemInfo {
  transport: string
  persistence: string
  environment: string
  version: string
  developmentMode: boolean
  clusterEnabled: boolean
}

const systemInfo = ref<SystemInfo | null>(null)
const loading = ref(false)

const loadSystemInfo = async () => {
  loading.value = true
  try {
    const response = await axios.get('/api/system/info')
    systemInfo.value = response.data
  } catch (e) {
    console.error('Failed to load system info:', e)
  } finally {
    loading.value = false
  }
}

onMounted(() => loadSystemInfo())
</script>

<template>
  <div class="admin-settings">
    <h1>系统设置</h1>

    <div class="settings-grid">
      <!-- System Info Card -->
      <va-card>
        <va-card-title>
          <va-icon name="info" class="mr-2" />
          系统信息
        </va-card-title>
        <va-card-content>
          <div v-if="loading" class="loading-state">
            <va-progress-circle indeterminate />
          </div>
          <div v-else-if="systemInfo" class="info-list">
            <div class="info-item">
              <span class="label">版本</span>
              <va-badge :text="systemInfo.version" color="info" />
            </div>
            <div class="info-item">
              <span class="label">环境</span>
              <va-badge
                :text="systemInfo.environment"
                :color="systemInfo.environment === 'Production' ? 'success' : 'warning'"
              />
            </div>
            <div class="info-item">
              <span class="label">消息传输</span>
              <code>{{ systemInfo.transport }}</code>
            </div>
            <div class="info-item">
              <span class="label">数据持久化</span>
              <code>{{ systemInfo.persistence }}</code>
            </div>
            <div class="info-item">
              <span class="label">集群模式</span>
              <va-badge
                :text="systemInfo.clusterEnabled ? '已启用' : '未启用'"
                :color="systemInfo.clusterEnabled ? 'success' : 'secondary'"
              />
            </div>
            <div class="info-item">
              <span class="label">开发模式</span>
              <va-badge
                :text="systemInfo.developmentMode ? '是' : '否'"
                :color="systemInfo.developmentMode ? 'warning' : 'secondary'"
              />
            </div>
          </div>
        </va-card-content>
      </va-card>

      <!-- Architecture Card -->
      <va-card>
        <va-card-title>
          <va-icon name="architecture" class="mr-2" />
          技术架构
        </va-card-title>
        <va-card-content>
          <div class="tech-stack">
            <div class="tech-item">
              <va-icon name="hub" color="primary" />
              <div class="tech-info">
                <span class="tech-name">Catga CQRS</span>
                <span class="tech-desc">命令查询职责分离框架</span>
              </div>
            </div>
            <div class="tech-item">
              <va-icon name="storage" color="success" />
              <div class="tech-info">
                <span class="tech-name">事件溯源</span>
                <span class="tech-desc">完整的事件历史记录</span>
              </div>
            </div>
            <div class="tech-item">
              <va-icon name="code" color="info" />
              <div class="tech-info">
                <span class="tech-name">源代码生成</span>
                <span class="tech-desc">AOT 兼容，零反射</span>
              </div>
            </div>
            <div class="tech-item">
              <va-icon name="cloud" color="warning" />
              <div class="tech-info">
                <span class="tech-name">分布式支持</span>
                <span class="tech-desc">Redis / NATS 传输</span>
              </div>
            </div>
          </div>
        </va-card-content>
      </va-card>

      <!-- API Endpoints Card -->
      <va-card class="full-width">
        <va-card-title>
          <va-icon name="api" class="mr-2" />
          API 端点
        </va-card-title>
        <va-card-content>
          <div class="endpoints-list">
            <div class="endpoint-item">
              <va-badge text="GET" color="success" />
              <code>/api/orders</code>
              <span class="endpoint-desc">获取订单列表</span>
            </div>
            <div class="endpoint-item">
              <va-badge text="POST" color="primary" />
              <code>/api/orders</code>
              <span class="endpoint-desc">创建订单</span>
            </div>
            <div class="endpoint-item">
              <va-badge text="GET" color="success" />
              <code>/api/orders/:id</code>
              <span class="endpoint-desc">获取订单详情</span>
            </div>
            <div class="endpoint-item">
              <va-badge text="POST" color="primary" />
              <code>/api/orders/:id/pay</code>
              <span class="endpoint-desc">支付订单</span>
            </div>
            <div class="endpoint-item">
              <va-badge text="POST" color="primary" />
              <code>/api/orders/:id/process</code>
              <span class="endpoint-desc">处理订单</span>
            </div>
            <div class="endpoint-item">
              <va-badge text="POST" color="primary" />
              <code>/api/orders/:id/ship</code>
              <span class="endpoint-desc">发货</span>
            </div>
            <div class="endpoint-item">
              <va-badge text="POST" color="primary" />
              <code>/api/orders/:id/deliver</code>
              <span class="endpoint-desc">确认送达</span>
            </div>
            <div class="endpoint-item">
              <va-badge text="POST" color="warning" />
              <code>/api/orders/:id/cancel</code>
              <span class="endpoint-desc">取消订单</span>
            </div>
            <div class="endpoint-item">
              <va-badge text="GET" color="success" />
              <code>/api/orders/stats</code>
              <span class="endpoint-desc">订单统计</span>
            </div>
          </div>
        </va-card-content>
      </va-card>
    </div>
  </div>
</template>

<style scoped>
.admin-settings h1 {
  font-size: 1.5rem;
  margin-bottom: 1.5rem;
}

.settings-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(350px, 1fr));
  gap: 1rem;
}

.full-width {
  grid-column: 1 / -1;
}

.loading-state {
  display: flex;
  justify-content: center;
  padding: 2rem;
}

.info-list {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.info-item {
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.info-item .label {
  color: #666;
}

.tech-stack {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.tech-item {
  display: flex;
  align-items: center;
  gap: 1rem;
  padding: 0.75rem;
  background: #f7fafc;
  border-radius: 8px;
}

.tech-info {
  display: flex;
  flex-direction: column;
}

.tech-name {
  font-weight: 600;
}

.tech-desc {
  font-size: 0.875rem;
  color: #666;
}

.endpoints-list {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.endpoint-item {
  display: flex;
  align-items: center;
  gap: 1rem;
  padding: 0.5rem 0;
  border-bottom: 1px solid #eee;
}

.endpoint-item code {
  flex: 1;
  font-size: 0.9rem;
}

.endpoint-desc {
  color: #666;
  font-size: 0.875rem;
}

.mr-2 {
  margin-right: 0.5rem;
}
</style>
