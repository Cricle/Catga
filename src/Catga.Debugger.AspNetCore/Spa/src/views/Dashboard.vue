<template>
  <div class="dashboard">
    <h2>Dashboard</h2>
    
    <el-row :gutter="20" class="stats-row">
      <el-col :span="6">
        <el-card shadow="hover">
          <el-statistic title="Total Events" :value="statsStore.totalEvents">
            <template #prefix>
              <el-icon><Document /></el-icon>
            </template>
          </el-statistic>
        </el-card>
      </el-col>
      
      <el-col :span="6">
        <el-card shadow="hover">
          <el-statistic title="Total Flows" :value="statsStore.totalFlows">
            <template #prefix>
              <el-icon><Connection /></el-icon>
            </template>
          </el-statistic>
        </el-card>
      </el-col>
      
      <el-col :span="6">
        <el-card shadow="hover">
          <el-statistic title="Storage Size" :value="statsStore.storageSizeMB" suffix="MB">
            <template #prefix>
              <el-icon><Coin /></el-icon>
            </template>
          </el-statistic>
        </el-card>
      </el-col>
      
      <el-col :span="6">
        <el-card shadow="hover">
          <el-statistic title="Growth Rate" :value="statsStore.getGrowthRate" suffix="events/s">
            <template #prefix>
              <el-icon><TrendCharts /></el-icon>
            </template>
          </el-statistic>
        </el-card>
      </el-col>
    </el-row>

    <el-row :gutter="20" class="content-row">
      <el-col :span="16">
        <el-card shadow="hover">
          <template #header>
            <span>Recent Flows</span>
          </template>
          <el-table :data="flowStore.flows.slice(0, 10)" style="width: 100%">
            <el-table-column prop="correlationId" label="Correlation ID" width="300" />
            <el-table-column prop="eventCount" label="Events" width="100" />
            <el-table-column label="Status" width="100">
              <template #default="{ row }">
                <el-tag :type="row.hasErrors ? 'danger' : 'success'">
                  {{ row.hasErrors ? 'Error' : 'OK' }}
                </el-tag>
              </template>
            </el-table-column>
            <el-table-column prop="startTime" label="Start Time">
              <template #default="{ row }">
                {{ new Date(row.startTime).toLocaleString() }}
              </template>
            </el-table-column>
          </el-table>
        </el-card>
      </el-col>
      
      <el-col :span="8">
        <el-card shadow="hover">
          <template #header>
            <span>Quick Actions</span>
          </template>
          <el-space direction="vertical" style="width: 100%">
            <el-button type="primary" @click="$router.push('/flows')" style="width: 100%">
              <el-icon><List /></el-icon>
              View All Flows
            </el-button>
            <el-button type="success" @click="$router.push('/replay')" style="width: 100%">
              <el-icon><VideoPlay /></el-icon>
              Start Replay
            </el-button>
            <el-button @click="refresh" style="width: 100%">
              <el-icon><Refresh /></el-icon>
              Refresh Data
            </el-button>
          </el-space>
        </el-card>
      </el-col>
    </el-row>
  </div>
</template>

<script setup lang="ts">
import { Document, Connection, Coin, TrendCharts, List, VideoPlay, Refresh } from '@element-plus/icons-vue';
import { useFlowStore } from '@/stores/flow';
import { useStatsStore } from '@/stores/stats';

const flowStore = useFlowStore();
const statsStore = useStatsStore();

const refresh = async () => {
  await Promise.all([
    flowStore.loadFlows(),
    statsStore.loadStats(),
  ]);
};
</script>

<style scoped>
.dashboard {
  height: 100%;
}

.stats-row {
  margin-bottom: 20px;
}

.content-row {
  margin-bottom: 20px;
}
</style>

