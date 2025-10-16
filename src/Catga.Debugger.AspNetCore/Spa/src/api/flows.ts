import apiClient from './client';
import type {
  FlowsResponse,
  FlowResponse,
  StatsResponse,
  SystemReplayRequest,
  SystemReplayResponse,
  FlowReplayRequest,
  FlowReplayResponse,
} from '@/types/flow';

/**
 * Flows API
 */
export const flowsApi = {
  /**
   * Get all flows
   */
  async getFlows(): Promise<FlowsResponse> {
    return apiClient.get('/flows');
  },

  /**
   * Get specific flow by correlation ID
   */
  async getFlow(correlationId: string): Promise<FlowResponse> {
    return apiClient.get(`/flows/${correlationId}`);
  },

  /**
   * Get event store statistics
   */
  async getStats(): Promise<StatsResponse> {
    return apiClient.get('/stats');
  },

  /**
   * Start system-wide replay
   */
  async replaySystem(request: SystemReplayRequest): Promise<SystemReplayResponse> {
    return apiClient.post('/replay/system', request);
  },

  /**
   * Start flow-level replay
   */
  async replayFlow(request: FlowReplayRequest): Promise<FlowReplayResponse> {
    return apiClient.post('/replay/flow', request);
  },
};

