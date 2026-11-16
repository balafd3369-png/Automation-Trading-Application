import {
  ComponentHealthState,
  HealthUiStatus,
  MonitoringComponentRow,
  SystemHealthOverallStatus,
  WorkerHealthDetailSnapshot,
  SystemHealthComponentSummary,
  BrokerConnectivityHealthSnapshot,
  MarketDataHealthSnapshot,
  SignalRServerHealthSnapshot,
  WorkerHeartbeatSnapshot,
} from './system-monitoring.models';

export function mapComponentState(status: ComponentHealthState | null | undefined): HealthUiStatus {
  switch (status) {
    case 'Healthy':
      return 'HEALTHY';
    case 'Degraded':
      return 'DEGRADED';
    case 'Stalled':
    case 'Unresponsive':
    case 'Stopped':
      return 'UNHEALTHY';
    default:
      return 'UNKNOWN';
  }
}

export function mapOverallStatus(status: SystemHealthOverallStatus | null | undefined): HealthUiStatus {
  switch (status) {
    case 'Healthy':
    case 'Maintenance':
      return 'HEALTHY';
    case 'Degraded':
      return 'DEGRADED';
    case 'Critical':
      return 'UNHEALTHY';
    default:
      return 'UNKNOWN';
  }
}

export function healthStatusClass(status: HealthUiStatus): string {
  switch (status) {
    case 'HEALTHY':
      return 'status-healthy';
    case 'DEGRADED':
      return 'status-degraded';
    case 'UNHEALTHY':
      return 'status-unhealthy';
    default:
      return 'status-unknown';
  }
}

export function severityClass(severity: string): string {
  switch (severity) {
    case 'Critical':
      return 'sev-critical';
    case 'High':
      return 'sev-high';
    case 'Medium':
      return 'sev-medium';
    case 'Low':
      return 'sev-low';
    default:
      return 'sev-info';
  }
}

export function countComponentsByUiStatus(rows: MonitoringComponentRow[]): Record<HealthUiStatus, number> {
  const counts: Record<HealthUiStatus, number> = {
    HEALTHY: 0,
    DEGRADED: 0,
    UNHEALTHY: 0,
    UNKNOWN: 0,
  };
  for (const row of rows) {
    counts[row.status] += 1;
  }
  return counts;
}

export function buildComponentRows(input: {
  summaries: SystemHealthComponentSummary[];
  workers: WorkerHealthDetailSnapshot[];
  workerHeartbeats: WorkerHeartbeatSnapshot[];
  broker: BrokerConnectivityHealthSnapshot | null;
  marketData: MarketDataHealthSnapshot | null;
  signalR: SignalRServerHealthSnapshot | null;
}): MonitoringComponentRow[] {
  const rows: MonitoringComponentRow[] = [];

  for (const summary of input.summaries) {
    rows.push({
      category: categoryForBackendComponent(summary.component),
      name: summary.component,
      identifier: summary.component,
      status: mapComponentState(summary.status),
      lastSuccess: summary.lastSuccessfulCheck,
      lastCheck: summary.checkedAt,
      responseMs: null,
      lastError: summary.lastError,
      lastFailure: summary.lastFailure,
    });
  }

  for (const w of input.workers.length ? input.workers : mapHeartbeatWorkers(input.workerHeartbeats)) {
    rows.push({
      category: 'Background processing',
      name: w.workerName,
      identifier: w.instanceId || w.workerName,
      status: mapComponentState(w.currentState),
      lastSuccess: w.lastSuccessfulOperationUtc,
      lastCheck: w.lastHeartbeatUtc,
      responseMs: null,
      lastError: w.lastError,
      lastFailure: w.lastFailedOperationUtc,
    });
  }

  if (input.broker) {
    for (const c of input.broker.connections) {
      rows.push({
        category: 'Broker infrastructure',
        name: c.displayName,
        identifier: c.brokerId,
        status: mapComponentState(c.state),
        lastSuccess: c.lastSuccessfulCommunicationAt,
        lastCheck: input.broker.checkedAt,
        responseMs: input.broker.probeDurationMs,
        lastError: c.lastError ?? input.broker.lastError,
        lastFailure: c.lastFailureAt ?? input.broker.lastFailureAt,
      });
    }
  }

  if (input.marketData) {
    rows.push({
      category: 'Market data infrastructure',
      name: 'Market data feed',
      identifier: input.marketData.feedSource,
      status: mapComponentState(input.marketData.state),
      lastSuccess: input.marketData.lastFeedUpdateAt,
      lastCheck: input.marketData.checkedAt,
      responseMs: input.marketData.probeDurationMs,
      lastError: input.marketData.lastError,
      lastFailure: null,
    });
  }

  if (input.signalR) {
    rows.push({
      category: 'Application infrastructure',
      name: 'SignalR hub',
      identifier: 'market-chart-hub',
      status: mapComponentState(input.signalR.state),
      lastSuccess: input.signalR.lastSuccessfulHubActivityAt,
      lastCheck: input.signalR.checkedAt,
      responseMs: input.signalR.probeDurationMs,
      lastError: input.signalR.lastError,
      lastFailure: input.signalR.lastDisconnectedAt,
    });
  }

  return rows;
}

function mapHeartbeatWorkers(heartbeats: WorkerHeartbeatSnapshot[]): WorkerHealthDetailSnapshot[] {
  return heartbeats.map((h) => ({
    workerName: h.workerName,
    instanceId: h.instanceId,
    currentState: heartbeatToState(h),
    lastHeartbeatUtc: h.lastHeartbeat,
    lastSuccessfulOperationUtc: h.lastSuccessfulExecution,
    lastFailedOperationUtc: null,
    consecutiveFailureCount: h.consecutiveFailureCount,
    totalFailureCount: h.consecutiveFailureCount,
    lastError: h.lastError,
    lastStateChangeUtc: null,
    progressExpected: true,
    isHeartbeatStale: h.isStale,
    isProgressStale: false,
  }));
}

function heartbeatToState(h: WorkerHeartbeatSnapshot): ComponentHealthState {
  if (h.status === 'Running' && !h.isStale) {
    return 'Healthy';
  }
  if (h.status === 'Degraded') {
    return 'Degraded';
  }
  if (h.status === 'Stuck' || h.isStale) {
    return 'Stalled';
  }
  if (h.status === 'Stopped') {
    return 'Stopped';
  }
  return 'Unknown';
}

function categoryForBackendComponent(component: string): string {
  switch (component) {
    case 'database':
      return 'Application infrastructure';
    case 'runtime':
      return 'Application infrastructure';
    case 'broker':
      return 'Broker infrastructure';
    case 'market-data':
      return 'Market data infrastructure';
    case 'signalr':
      return 'Application infrastructure';
    case 'workers':
      return 'Background processing';
    default:
      return 'Application infrastructure';
  }
}

export const MONITORING_NAV = [
  { path: 'overview', label: 'Overview' },
  { path: 'components', label: 'Component Health' },
  { path: 'incidents', label: 'Incidents' },
  { path: 'notifications', label: 'Notifications' },
  { path: 'settings', label: 'Notification Settings' },
  { path: 'escalation', label: 'Escalation Policies' },
  { path: 'analytics', label: 'Health Analytics' },
  { path: 'reliability', label: 'Reliability Overview' },
  { path: 'instances', label: 'Instances' },
  { path: 'coordination-workers', label: 'Workers' },
  { path: 'locks', label: 'Distributed Locks' },
  { path: 'recovery', label: 'Recovery Center' },
  { path: 'production-safety', label: 'Production Safety' },
  { path: 'trading-safety', label: 'Trading Safety' },
] as const;