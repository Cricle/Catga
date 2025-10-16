<template>
  <div class="replay-view">
    <h2>Time-Travel Replay</h2>
    
    <el-card>
      <template #header>
        <span>System Replay</span>
      </template>
      
      <el-form :model="replayForm" label-width="120px">
        <el-form-item label="Start Time">
          <el-date-picker
            v-model="replayForm.startTime"
            type="datetime"
            placeholder="Select start time"
          />
        </el-form-item>
        
        <el-form-item label="End Time">
          <el-date-picker
            v-model="replayForm.endTime"
            type="datetime"
            placeholder="Select end time"
          />
        </el-form-item>
        
        <el-form-item label="Speed">
          <el-slider v-model="replayForm.speed" :min="0.25" :max="10" :step="0.25" show-input />
        </el-form-item>
        
        <el-form-item>
          <el-button type="primary" @click="startReplay" :loading="loading">
            Start Replay
          </el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <el-card v-if="replayResult" class="result-card">
      <template #header>
        <span>Replay Result</span>
      </template>
      <el-descriptions :column="2" border>
        <el-descriptions-item label="Event Count">
          {{ replayResult.eventCount }}
        </el-descriptions-item>
        <el-descriptions-item label="Speed">
          {{ replayResult.speed }}x
        </el-descriptions-item>
        <el-descriptions-item label="Start Time">
          {{ new Date(replayResult.startTime).toLocaleString() }}
        </el-descriptions-item>
        <el-descriptions-item label="End Time">
          {{ new Date(replayResult.endTime).toLocaleString() }}
        </el-descriptions-item>
      </el-descriptions>
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive } from 'vue';
import { ElMessage } from 'element-plus';
import { flowsApi } from '@/api/flows';
import type { SystemReplayResponse } from '@/types/flow';

const loading = ref(false);
const replayResult = ref<SystemReplayResponse | null>(null);

const replayForm = reactive({
  startTime: new Date(Date.now() - 3600000), // 1 hour ago
  endTime: new Date(),
  speed: 1.0,
});

const startReplay = async () => {
  loading.value = true;
  
  try {
    const result = await flowsApi.replaySystem({
      startTime: replayForm.startTime.toISOString(),
      endTime: replayForm.endTime.toISOString(),
      speed: replayForm.speed,
    });
    
    replayResult.value = result;
    ElMessage.success('Replay started successfully');
  } catch (error) {
    ElMessage.error('Failed to start replay');
    console.error(error);
  } finally {
    loading.value = false;
  }
};
</script>

<style scoped>
.replay-view {
  padding: 20px;
}

.result-card {
  margin-top: 20px;
}
</style>

