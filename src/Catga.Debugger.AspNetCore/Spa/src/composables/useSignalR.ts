import { ref, onUnmounted } from 'vue';
import * as signalR from '@microsoft/signalr';
import type { FlowEventUpdate, StatsUpdate, ReplayProgressUpdate } from '@/types/flow';

/**
 * SignalR connection composable
 */
export function useSignalR() {
  const connection = ref<signalR.HubConnection | null>(null);
  const isConnected = ref(false);
  const isConnecting = ref(false);
  const error = ref<string | null>(null);

  /**
   * Initialize SignalR connection
   */
  const connect = async () => {
    if (connection.value || isConnecting.value) return;

    isConnecting.value = true;
    error.value = null;

    try {
      connection.value = new signalR.HubConnectionBuilder()
        .withUrl('/debug/hub')
        .withAutomaticReconnect({
          nextRetryDelayInMilliseconds: (retryContext) => {
            // Exponential backoff: 0s, 2s, 10s, 30s
            if (retryContext.previousRetryCount === 0) return 0;
            if (retryContext.previousRetryCount === 1) return 2000;
            if (retryContext.previousRetryCount === 2) return 10000;
            return 30000;
          },
        })
        .configureLogging(signalR.LogLevel.Information)
        .build();

      // Connection state handlers
      connection.value.onclose(() => {
        isConnected.value = false;
        console.log('SignalR: Disconnected');
      });

      connection.value.onreconnecting(() => {
        isConnected.value = false;
        console.log('SignalR: Reconnecting...');
      });

      connection.value.onreconnected(() => {
        isConnected.value = true;
        console.log('SignalR: Reconnected');
      });

      await connection.value.start();
      isConnected.value = true;
      console.log('SignalR: Connected');
    } catch (err) {
      error.value = err instanceof Error ? err.message : 'Connection failed';
      console.error('SignalR connection error:', err);
    } finally {
      isConnecting.value = false;
    }
  };

  /**
   * Disconnect from hub
   */
  const disconnect = async () => {
    if (connection.value) {
      await connection.value.stop();
      connection.value = null;
      isConnected.value = false;
    }
  };

  /**
   * Subscribe to flow events
   */
  const onFlowEvent = (callback: (update: FlowEventUpdate) => void) => {
    if (connection.value) {
      connection.value.on('FlowEventReceived', callback);
    }
  };

  /**
   * Subscribe to stats updates
   */
  const onStatsUpdate = (callback: (stats: StatsUpdate) => void) => {
    if (connection.value) {
      connection.value.on('StatsUpdated', callback);
    }
  };

  /**
   * Subscribe to replay progress
   */
  const onReplayProgress = (callback: (progress: ReplayProgressUpdate) => void) => {
    if (connection.value) {
      connection.value.on('ReplayProgress', callback);
    }
  };

  /**
   * Subscribe to specific flow
   */
  const subscribeToFlow = async (correlationId: string) => {
    if (connection.value && isConnected.value) {
      await connection.value.invoke('SubscribeToFlow', correlationId);
    }
  };

  /**
   * Unsubscribe from specific flow
   */
  const unsubscribeFromFlow = async (correlationId: string) => {
    if (connection.value && isConnected.value) {
      await connection.value.invoke('UnsubscribeFromFlow', correlationId);
    }
  };

  /**
   * Subscribe to system-wide updates
   */
  const subscribeToSystem = async () => {
    if (connection.value && isConnected.value) {
      await connection.value.invoke('SubscribeToSystem');
    }
  };

  /**
   * Unsubscribe from system-wide updates
   */
  const unsubscribeFromSystem = async () => {
    if (connection.value && isConnected.value) {
      await connection.value.invoke('UnsubscribeFromSystem');
    }
  };

  /**
   * Get current stats
   */
  const getStats = async (): Promise<StatsUpdate | null> => {
    if (connection.value && isConnected.value) {
      return await connection.value.invoke('GetStats');
    }
    return null;
  };

  // Auto cleanup
  onUnmounted(() => {
    disconnect();
  });

  return {
    connection,
    isConnected,
    isConnecting,
    error,
    connect,
    disconnect,
    onFlowEvent,
    onStatsUpdate,
    onReplayProgress,
    subscribeToFlow,
    unsubscribeFromFlow,
    subscribeToSystem,
    unsubscribeFromSystem,
    getStats,
  };
}

