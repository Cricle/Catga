<template>
  <div class="flow-detail">
    <el-page-header @back="$router.back()" title="Back to Flows">
      <template #content>
        <span class="text-large font-600 mr-3">Flow Details</span>
      </template>
    </el-page-header>

    <el-card v-if="flowStore.currentFlow" class="detail-card">
      <template #header>
        <div class="card-header">
          <span>{{ flowStore.currentFlow.correlationId }}</span>
          <el-tag>{{ flowStore.currentFlow.eventCount }} Events</el-tag>
        </div>
      </template>

      <el-timeline>
        <el-timeline-item
          v-for="event in flowStore.currentFlow.events"
          :key="event.id"
          :timestamp="new Date(event.timestamp).toLocaleString()"
        >
          <el-card>
            <p><strong>Type:</strong> {{ event.type }}</p>
            <p><strong>Service:</strong> {{ event.serviceName }}</p>
            <p><strong>ID:</strong> {{ event.id }}</p>
          </el-card>
        </el-timeline-item>
      </el-timeline>
    </el-card>

    <el-empty v-else description="Flow not found" />
  </div>
</template>

<script setup lang="ts">
import { onMounted } from 'vue';
import { useRoute } from 'vue-router';
import { useFlowStore } from '@/stores/flow';

const route = useRoute();
const flowStore = useFlowStore();

onMounted(async () => {
  const correlationId = route.params.correlationId as string;
  if (correlationId) {
    await flowStore.loadFlow(correlationId);
  }
});
</script>

<style scoped>
.flow-detail {
  padding: 20px;
}

.detail-card {
  margin-top: 20px;
}

.card-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
}
</style>

