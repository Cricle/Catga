<template>
  <div id="app" class="catga-debugger">
    <el-container class="layout">
      <!-- Header -->
      <el-header class="header">
        <div class="header-content">
          <h1 class="title">
            <el-icon><Monitor /></el-icon>
            Catga Debugger
          </h1>
          
          <div class="header-stats">
            <el-tag type="info" size="large">
              <el-icon><Document /></el-icon>
              Events: {{ statsStore.totalEvents }}
            </el-tag>
            <el-tag type="success" size="large">
              <el-icon><Connection /></el-icon>
              Flows: {{ statsStore.totalFlows }}
            </el-tag>
            <el-tag :type="signalRConnected ? 'success' : 'danger'" size="large">
              <el-icon><Link /></el-icon>
              {{ signalRConnected ? 'Connected' : 'Disconnected' }}
            </el-tag>
          </div>
        </div>
      </el-header>

      <!-- Main content -->
      <el-container class="main-container">
        <!-- Sidebar navigation -->
        <el-aside width="200px" class="sidebar">
          <el-menu
            :default-active="$route.path"
            router
            class="sidebar-menu"
          >
            <el-menu-item index="/">
              <el-icon><HomeFilled /></el-icon>
              <span>Dashboard</span>
            </el-menu-item>
            <el-menu-item index="/flows">
              <el-icon><List /></el-icon>
              <span>Flows</span>
            </el-menu-item>
            <el-menu-item index="/replay">
              <el-icon><VideoPlay /></el-icon>
              <span>Replay</span>
            </el-menu-item>
          </el-menu>
        </el-aside>

        <!-- Page content -->
        <el-main class="content">
          <RouterView />
        </el-main>
      </el-container>
    </el-container>
  </div>
</template>

<script setup lang="ts">
import { onMounted, onUnmounted, computed } from 'vue';
import { Monitor, Document, Connection, Link, HomeFilled, List, VideoPlay } from '@element-plus/icons-vue';
import { useSignalR } from '@/composables/useSignalR';
import { useFlowStore } from '@/stores/flow';
import { useStatsStore } from '@/stores/stats';

const flowStore = useFlowStore();
const statsStore = useStatsStore();
const signalR = useSignalR();

const signalRConnected = computed(() => signalR.isConnected.value);

onMounted(async () => {
  // Load initial data
  await Promise.all([
    flowStore.loadFlows(),
    statsStore.loadStats(),
  ]);

  // Connect to SignalR
  await signalR.connect();

  // Subscribe to updates
  if (signalR.isConnected.value) {
    signalR.onFlowEvent((update) => {
      flowStore.handleFlowEvent(update);
    });

    signalR.onStatsUpdate((update) => {
      statsStore.handleStatsUpdate(update);
    });

    // Subscribe to system-wide updates
    await signalR.subscribeToSystem();
  }
});

onUnmounted(() => {
  signalR.disconnect();
});
</script>

<style scoped>
.catga-debugger {
  height: 100vh;
  background: #f5f7fa;
}

.layout {
  height: 100vh;
}

.header {
  background: #fff;
  border-bottom: 1px solid #e4e7ed;
  padding: 0 20px;
  display: flex;
  align-items: center;
}

.header-content {
  display: flex;
  justify-content: space-between;
  align-items: center;
  width: 100%;
}

.title {
  margin: 0;
  font-size: 20px;
  color: #303133;
  display: flex;
  align-items: center;
  gap: 8px;
}

.header-stats {
  display: flex;
  gap: 12px;
}

.main-container {
  height: calc(100vh - 60px);
}

.sidebar {
  background: #fff;
  border-right: 1px solid #e4e7ed;
}

.sidebar-menu {
  border: none;
}

.content {
  padding: 20px;
  overflow-y: auto;
}
</style>

