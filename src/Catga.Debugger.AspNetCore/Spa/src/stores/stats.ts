import { defineStore } from 'pinia';
import { ref, computed } from 'vue';
import { flowsApi } from '@/api/flows';
import type { StatsResponse, StatsUpdate } from '@/types/flow';

/**
 * Stats store - manages system statistics
 */
export const useStatsStore = defineStore('stats', () => {
  // State
  const stats = ref<StatsResponse | null>(null);
  const loading = ref(false);
  const error = ref<string | null>(null);
  const history = ref<StatsUpdate[]>([]);
  const maxHistorySize = 100;

  // Computed
  const totalEvents = computed(() => stats.value?.totalEvents || 0);
  const totalFlows = computed(() => stats.value?.totalFlows || 0);
  const storageSizeMB = computed(() =>
    ((stats.value?.storageSizeBytes || 0) / (1024 * 1024)).toFixed(2)
  );

  // Actions

  /**
   * Load current stats
   */
  const loadStats = async () => {
    loading.value = true;
    error.value = null;

    try {
      stats.value = await flowsApi.getStats();
    } catch (err) {
      error.value = err instanceof Error ? err.message : 'Failed to load stats';
      console.error('Load stats error:', err);
    } finally {
      loading.value = false;
    }
  };

  /**
   * Handle real-time stats update (from SignalR)
   */
  const handleStatsUpdate = (update: StatsUpdate) => {
    // Update current stats
    if (stats.value) {
      stats.value.totalEvents = update.totalEvents;
      stats.value.totalFlows = update.totalFlows;
      stats.value.storageSizeBytes = update.storageSizeBytes;
      stats.value.timestamp = update.timestamp;
    } else {
      stats.value = {
        ...update,
        oldestEvent: new Date().toISOString(),
        newestEvent: new Date().toISOString(),
      };
    }

    // Add to history
    history.value.push(update);

    // Limit history size
    if (history.value.length > maxHistorySize) {
      history.value.shift();
    }
  };

  /**
   * Get stats growth rate (events per second)
   */
  const getGrowthRate = computed(() => {
    if (history.value.length < 2) return 0;

    const recent = history.value.slice(-10); // Last 10 updates
    const first = recent[0];
    const last = recent[recent.length - 1];

    const eventDelta = last.totalEvents - first.totalEvents;
    const timeDelta = (new Date(last.timestamp).getTime() -
                       new Date(first.timestamp).getTime()) / 1000; // seconds

    return timeDelta > 0 ? (eventDelta / timeDelta).toFixed(2) : '0';
  });

  /**
   * Clear stats
   */
  const clearStats = () => {
    stats.value = null;
    history.value = [];
  };

  return {
    // State
    stats,
    loading,
    error,
    history,

    // Computed
    totalEvents,
    totalFlows,
    storageSizeMB,
    getGrowthRate,

    // Actions
    loadStats,
    handleStatsUpdate,
    clearStats,
  };
});

