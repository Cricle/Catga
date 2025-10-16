import { defineStore } from 'pinia';
import { ref, computed } from 'vue';
import { flowsApi } from '@/api/flows';
import type { FlowInfo, FlowResponse, FlowEventUpdate } from '@/types/flow';

/**
 * Flow store - manages flow data and real-time updates
 */
export const useFlowStore = defineStore('flow', () => {
  // State
  const flows = ref<FlowInfo[]>([]);
  const currentFlow = ref<FlowResponse | null>(null);
  const loading = ref(false);
  const error = ref<string | null>(null);

  // Computed
  const flowCount = computed(() => flows.value.length);
  const errorFlows = computed(() => flows.value.filter(f => f.hasErrors));
  const errorCount = computed(() => errorFlows.value.length);

  // Actions

  /**
   * Load all flows
   */
  const loadFlows = async () => {
    loading.value = true;
    error.value = null;

    try {
      const response = await flowsApi.getFlows();
      flows.value = response.flows;
    } catch (err) {
      error.value = err instanceof Error ? err.message : 'Failed to load flows';
      console.error('Load flows error:', err);
    } finally {
      loading.value = false;
    }
  };

  /**
   * Load specific flow
   */
  const loadFlow = async (correlationId: string) => {
    loading.value = true;
    error.value = null;

    try {
      currentFlow.value = await flowsApi.getFlow(correlationId);
    } catch (err) {
      error.value = err instanceof Error ? err.message : 'Failed to load flow';
      console.error('Load flow error:', err);
    } finally {
      loading.value = false;
    }
  };

  /**
   * Handle real-time flow event (from SignalR)
   */
  const handleFlowEvent = (update: FlowEventUpdate) => {
    // Update or add flow
    const existingIndex = flows.value.findIndex(
      f => f.correlationId === update.correlationId
    );

    if (existingIndex >= 0) {
      // Update existing flow
      flows.value[existingIndex].eventCount++;
      flows.value[existingIndex].endTime = update.timestamp;

      if (update.eventType === 'ExceptionThrown') {
        flows.value[existingIndex].hasErrors = true;
      }
    } else {
      // Add new flow
      flows.value.unshift({
        correlationId: update.correlationId,
        startTime: update.timestamp,
        endTime: update.timestamp,
        eventCount: 1,
        hasErrors: update.eventType === 'ExceptionThrown',
      });

      // Limit to 100 flows in memory
      if (flows.value.length > 100) {
        flows.value.pop();
      }
    }

    // Update current flow if it's open
    if (currentFlow.value?.correlationId === update.correlationId) {
      currentFlow.value.eventCount++;
      currentFlow.value.endTime = update.timestamp;
      currentFlow.value.events.push({
        id: update.eventId,
        type: update.eventType,
        timestamp: update.timestamp,
        serviceName: update.serviceName || 'Unknown',
      });
    }
  };

  /**
   * Clear all flows
   */
  const clearFlows = () => {
    flows.value = [];
    currentFlow.value = null;
  };

  /**
   * Select flow
   */
  const selectFlow = (correlationId: string) => {
    return loadFlow(correlationId);
  };

  return {
    // State
    flows,
    currentFlow,
    loading,
    error,

    // Computed
    flowCount,
    errorFlows,
    errorCount,

    // Actions
    loadFlows,
    loadFlow,
    handleFlowEvent,
    clearFlows,
    selectFlow,
  };
});

