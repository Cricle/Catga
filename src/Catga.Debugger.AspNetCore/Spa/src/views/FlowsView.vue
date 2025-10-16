<template>
  <div class="flows-view">
    <h2>Flows</h2>
    <el-card>
      <el-table :data="flowStore.flows" style="width: 100%" :loading="flowStore.loading">
        <el-table-column prop="correlationId" label="Correlation ID" width="300" />
        <el-table-column prop="eventCount" label="Events" width="100" />
        <el-table-column label="Status" width="100">
          <template #default="{ row }">
            <el-tag :type="row.hasErrors ? 'danger' : 'success'">
              {{ row.hasErrors ? 'Error' : 'OK' }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="startTime" label="Start Time" />
        <el-table-column label="Actions">
          <template #default="{ row }">
            <el-button size="small" @click="viewDetails(row.correlationId)">
              View
            </el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { useRouter } from 'vue-router';
import { useFlowStore } from '@/stores/flow';

const router = useRouter();
const flowStore = useFlowStore();

const viewDetails = (correlationId: string) => {
  router.push(`/flows/${correlationId}`);
};
</script>

