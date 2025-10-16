/**
 * Flow types matching backend API
 */

export enum FlowType {
  Command = 'Command',
  Query = 'Query',
  Event = 'Event',
  Saga = 'Saga',
}

export enum StepStatus {
  Pending = 'Pending',
  Running = 'Running',
  Completed = 'Completed',
  Failed = 'Failed',
  Compensating = 'Compensating',
  Compensated = 'Compensated',
}

export interface FlowInfo {
  correlationId: string;
  startTime: string;
  endTime: string;
  eventCount: number;
  hasErrors: boolean;
}

export interface FlowsResponse {
  flows: FlowInfo[];
  totalFlows: number;
  timestamp: string;
}

export interface EventInfo {
  id: string;
  type: string;
  timestamp: string;
  serviceName: string;
}

export interface FlowResponse {
  correlationId: string;
  startTime: string;
  endTime: string;
  eventCount: number;
  events: EventInfo[];
}

export interface StatsResponse {
  totalEvents: number;
  totalFlows: number;
  storageSizeBytes: number;
  oldestEvent: string;
  newestEvent: string;
  timestamp: string;
}

export interface FlowEventUpdate {
  correlationId: string;
  eventId: string;
  eventType: string;
  timestamp: string;
  serviceName: string | null;
}

export interface StatsUpdate {
  totalEvents: number;
  totalFlows: number;
  storageSizeBytes: number;
  timestamp: string;
}

export interface ReplayProgressUpdate {
  correlationId: string;
  currentStep: number;
  totalSteps: number;
  progress: number;
}

export interface SystemReplayRequest {
  startTime: string;
  endTime: string;
  speed?: number;
}

export interface SystemReplayResponse {
  eventCount: number;
  startTime: string;
  endTime: string;
  speed: number;
}

export interface FlowReplayRequest {
  correlationId: string;
}

export interface FlowReplayResponse {
  correlationId: string;
  totalSteps: number;
  currentStep: number;
}

