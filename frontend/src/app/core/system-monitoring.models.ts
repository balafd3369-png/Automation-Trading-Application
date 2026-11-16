/** Mirrors backend SystemHealth / incident / notification DTOs (JSON string enums). */

export type HealthUiStatus = 'HEALTHY' | 'DEGRADED' | 'UNHEALTHY' | 'UNKNOWN';

export type SystemHealthOverallStatus =
  | 'Unknown'
  | 'Healthy'
  | 'Degraded'
  | 'Critical'
  | 'Maintenance';

export type ComponentHealthState =
  | 'Unknown'
  | 'Healthy'
  | 'Degraded'
  | 'Stalled'
  | 'Unresponsive'
  | 'Stopped';

export type WorkerRunStatus = 'Unknown' | 'Starting' | 'Running' | 'Degraded' | 'Stopped' | 'Stuck';

export type SystemIncidentSeverity = 'Info' | 'Low' | 'Medium' | 'High' | 'Critical';

export type SystemIncidentStatus =
  | 'Detected'
  | 'Acknowledged'
  | 'Investigating'
  | 'Resolved'
  | 'Closed';

export type SystemIncidentCategory = string;

export type IncidentNotificationChannel = 'Email' | 'Telegram';

export type IncidentNotificationDeliveryStatus =
  | 'Pending'
  | 'Processing'
  | 'Sent'
  | 'Failed'
  | 'RetryScheduled'
  | 'Cancelled';

export type IncidentNotificationType = string;

export type EscalationStepKind = 'UnacknowledgedReminder' | 'SecondaryRecipients' | 'UnresolvedReminder';

export interface DatabaseHealthSnapshot {
  canConnect: boolean;
  canRead: boolean;
  canWrite: boolean;
  error: string | null;
  checkedAt: string;
  latencyMs: number;
}

export interface WorkerHeartbeatSnapshot {
  workerName: string;
  instanceId: string;
  status: WorkerRunStatus;
  lastHeartbeat: string | null;
  lastSuccessfulExecution: string | null;
  lastError: string | null;
  consecutiveFailureCount: number;
  isStale: boolean;
}

export interface WorkerHealthDetailSnapshot {
  workerName: string;
  instanceId: string;
  currentState: ComponentHealthState;
  lastHeartbeatUtc: string | null;
  lastSuccessfulOperationUtc: string | null;
  lastFailedOperationUtc: string | null;
  consecutiveFailureCount: number;
  totalFailureCount: number;
  lastError: string | null;
  lastStateChangeUtc: string | null;
  progressExpected: boolean;
  isHeartbeatStale: boolean;
  isProgressStale: boolean;
}

export interface SystemHealthLiveSnapshot {
  overallStatus: SystemHealthOverallStatus;
  instanceId: string;
  apiStatus: string;
  database: DatabaseHealthSnapshot;
  workers: WorkerHeartbeatSnapshot[];
  runtime: {
    workingSetBytes: number;
    gcHeapBytes: number;
    threadPoolWorkerThreads: number;
    threadPoolCompletionPortThreads: number;
    threadPoolPendingWorkItems: number;
    unobservedTaskExceptionCount: number;
    processStartedAt: string;
  };
  lastComprehensiveReportAt: string | null;
  lastComprehensiveReportId: string | null;
}

export interface SystemHealthComponentSummary {
  component: string;
  status: ComponentHealthState;
  lastSuccessfulCheck: string | null;
  lastFailure: string | null;
  consecutiveFailures: number;
  lastError: string | null;
  checkedAt: string;
  metrics: unknown;
}

export interface BrokerConnectionHealthItem {
  brokerId: string;
  displayName: string;
  sessionPresent: boolean;
  communicationVerified: boolean;
  lastSuccessfulCommunicationAt: string | null;
  lastFailureAt: string | null;
  consecutiveFailures: number;
  lastError: string | null;
  state: ComponentHealthState;
}

export interface BrokerConnectivityHealthSnapshot {
  state: ComponentHealthState;
  connections: BrokerConnectionHealthItem[];
  lastSuccessfulProbeAt: string | null;
  lastFailureAt: string | null;
  consecutiveFailures: number;
  lastError: string | null;
  checkedAt: string;
  probeDurationMs: number;
}

export interface MarketDataHealthSnapshot {
  state: ComponentHealthState;
  feedSource: string;
  adminBrokerConnected: boolean;
  lastFeedUpdateAt: string | null;
  marketIntradayOpen: boolean;
  marketTradingDay: boolean;
  watchedChartSubscriptions: number;
  instruments: unknown[];
  consecutiveProbeFailures: number;
  lastError: string | null;
  checkedAt: string;
  probeDurationMs: number;
}

export interface SignalRServerHealthSnapshot {
  state: ComponentHealthState;
  activeConnections: number;
  totalConnectionsObserved: number;
  disconnectCount: number;
  connectionFailureCount: number;
  lastConnectedAt: string | null;
  lastDisconnectedAt: string | null;
  lastSuccessfulHubActivityAt: string | null;
  lastError: string | null;
  note: string;
  checkedAt: string;
  probeDurationMs: number;
}

export interface SystemIncidentRecord {
  id: string;
  fingerprint: string;
  title: string;
  description: string;
  component: string;
  componentIdentifier: string;
  category: SystemIncidentCategory;
  severity: SystemIncidentSeverity;
  status: SystemIncidentStatus;
  firstDetectedUtc: string;
  lastOccurrenceUtc: string;
  lastUpdatedUtc: string;
  acknowledgedUtc: string | null;
  acknowledgedBy: string | null;
  resolvedUtc: string | null;
  closedUtc: string | null;
  closedBy: string | null;
  occurrenceCount: number;
  lastError: string | null;
  metadataJson: string;
}

export interface SystemIncidentEventRecord {
  id: string;
  incidentId: string;
  eventType: string;
  previousStatus: SystemIncidentStatus | null;
  newStatus: SystemIncidentStatus | null;
  severity: SystemIncidentSeverity;
  message: string;
  actor: string | null;
  timestampUtc: string;
  metadataJson: string | null;
}

export interface SystemIncidentNoteRecord {
  id: string;
  incidentId: string;
  note: string;
  createdBy: string;
  createdUtc: string;
}

export interface SystemIncidentStatistics {
  totalIncidents: number;
  activeIncidents: number;
  criticalIncidents: number;
  highIncidents: number;
  acknowledgedIncidents: number;
  investigatingIncidents: number;
  resolvedIncidents: number;
  closedIncidents: number;
  countsByComponent: Record<string, number>;
  countsBySeverity: Record<string, number>;
  countsByCategory: Record<string, number>;
  averageSecondsToAcknowledge: number | null;
  averageSecondsToResolve: number | null;
  repeatedIncidentFingerprints: number;
  generatedAtUtc: string;
  filterFromUtc: string | null;
  filterToUtc: string | null;
}

export interface SystemIncidentListQuery {
  status?: SystemIncidentStatus;
  minSeverity?: SystemIncidentSeverity;
  component?: string;
  fromUtc?: string;
  toUtc?: string;
  limit?: number;
  offset?: number;
}

export interface IncidentNotificationRecord {
  id: string;
  incidentId: string;
  idempotencyKey: string;
  channel: IncidentNotificationChannel;
  recipient: string;
  notificationType: IncidentNotificationType;
  subject: string;
  bodyHtml: string;
  bodyText: string;
  deliveryStatus: IncidentNotificationDeliveryStatus;
  attemptCount: number;
  createdUtc: string;
  sentUtc: string | null;
  lastAttemptUtc: string | null;
  nextRetryUtc: string | null;
  failureReason: string | null;
  providerMetadataJson: string | null;
}

export interface NotificationSettingsResponse {
  notificationsEnabled: boolean;
  emailEnabled: boolean;
  telegramEnabled: boolean;
  senderEmail: string;
  primaryEmailRecipients: string[];
  secondaryEmailRecipients: string[];
  primaryTelegramChatIds: number[];
  secondaryTelegramChatIds: number[];
  telegramOnMediumSeverity: boolean;
  notifyOnLowSeverity: boolean;
  incidentPageUrlTemplate: string;
  notificationWorkerIntervalSeconds: number;
  maxConcurrentDeliveries: number;
  maximumDeliveryAttempts: number;
  retryBaseDelaySeconds: number;
  retryMaxDelaySeconds: number;
  alertMinimumSeverityForEmail: SystemIncidentSeverity;
  notificationThrottleSeconds: number;
}

export interface NotificationSettingsUpdateRequest {
  notificationsEnabled?: boolean;
  emailEnabled?: boolean;
  telegramEnabled?: boolean;
  senderEmail?: string;
  primaryEmailRecipients?: string[];
  secondaryEmailRecipients?: string[];
  primaryTelegramChatIds?: number[];
  secondaryTelegramChatIds?: number[];
  telegramOnMediumSeverity?: boolean;
  notifyOnLowSeverity?: boolean;
  incidentPageUrlTemplate?: string;
  notificationThrottleSeconds?: number;
}

export interface EscalationStepResponse {
  kind: EscalationStepKind;
  delayMinutes: number;
  useSecondaryRecipients: boolean;
}

export interface EscalationPolicyTierResponse {
  severity: SystemIncidentSeverity;
  steps: EscalationStepResponse[];
}

export interface EscalationPoliciesUpdateRequest {
  tiers: { severity: SystemIncidentSeverity; steps: EscalationStepResponse[] }[];
}

export interface SystemHealthReportEntry {
  id: string;
  reportKind: string;
  overallStatus: SystemHealthOverallStatus;
  payload: { generatedAt: string; findings: string[] };
  createdAt: string;
}

export interface MonitoringComponentRow {
  category: string;
  name: string;
  identifier: string;
  status: HealthUiStatus;
  lastSuccess: string | null;
  lastCheck: string | null;
  responseMs: number | null;
  lastError: string | null;
  lastFailure: string | null;
}

export type SystemWorkerRegistryStatus =
  | 'Starting'
  | 'Running'
  | 'Degraded'
  | 'Stopped'
  | 'Unresponsive'
  | 'Recovering'
  | 'Failed';

export type SystemRecoveryResult = 'Pending' | 'Succeeded' | 'Failed' | 'Skipped' | 'Denied';

export interface SystemInstanceRecord {
  instanceId: string;
  hostName: string;
  applicationVersion: string;
  isDraining: boolean;
  isLeader: boolean;
  leaderRole: string | null;
  registeredUtc: string;
  lastHeartbeatUtc: string;
  leaseExpiresUtc: string | null;
}

export interface DistributedLockRecord {
  lockName: string;
  ownerInstanceId: string;
  lockToken: string;
  fencingToken: number;
  createdUtc: string;
  expiresUtc: string;
  lastRenewedUtc: string;
  isExpired: boolean;
}

export interface SystemWorkerRegistryRecord {
  workerId: string;
  workerName: string;
  instanceId: string;
  workerType: string;
  status: SystemWorkerRegistryStatus;
  startedUtc: string;
  lastHeartbeatUtc: string;
  lastSuccessfulExecutionUtc: string | null;
  lastFailureUtc: string | null;
  restartCount: number;
  currentLease: string | null;
  lastError: string | null;
  recoveryEnabled: boolean;
  recoveryEligible: boolean;
}

export interface LeaderElectionRecord {
  role: string;
  leaderInstanceId: string;
  fencingToken: number;
  electedUtc: string;
  leaseExpiresUtc: string;
  lastHeartbeatUtc: string;
}

export interface SystemRecoveryHistoryRecord {
  id: string;
  workerId: string;
  workerName: string;
  instanceId: string;
  triggeringIncidentId: string | null;
  recoveryReason: string;
  attemptNumber: number;
  result: SystemRecoveryResult;
  correlationId: string;
  startedUtc: string;
  completedUtc: string | null;
  failureReason: string | null;
}

export interface SystemRecoveryPolicyRecord {
  workerName: string;
  automaticRecoveryAllowed: boolean;
  maxAttempts: number;
  cooldownSeconds: number;
  notes: string;
}

export interface ManualRecoveryRequest {
  reason?: string | null;
}

export interface LiveTradingEmergencyGateSnapshot {
  isHalted: boolean;
  reason: string;
  changedUtc: string | null;
  changedBy: string | null;
}

export interface BrokerCircuitBreakerSnapshot {
  liveOrdersBlocked: boolean;
  state: string;
  consecutiveFailures: number;
  lastSuccessAt: string | null;
  lastFailureAt: string | null;
  trippedAt: string | null;
  tripReason: string | null;
  lastHeartbeatAt: string | null;
}

export interface TradingSafetySummarySnapshot {
  emergencyGate: LiveTradingEmergencyGateSnapshot;
  circuitBreaker: BrokerCircuitBreakerSnapshot;
  nonTerminalManualLiveOrders: number;
  nonTerminalStrategyOrders: number;
  unknownOrderCount: number;
  stuckSubmittedCount: number;
  generatedUtc: string;
}

export interface EmergencyGateAuditEntry {
  action: string;
  detail: string;
  actor: string;
  createdUtc: string;
}

export interface ProductionSafetyStatusSnapshot {
  enabled: boolean;
  lastBackupUtc: string | null;
  lastBackupStatus: string | null;
  lastBackupSizeBytes: number | null;
  backupCount: number;
  lastStartupAuditUtc: string | null;
  unresolvedOrderFindings: number;
  reconciliationIssueCount: number;
  reconciliationHealth: string | null;
  generatedUtc: string;
}

export interface DatabaseBackupRecord {
  id: string;
  sourceDatabasePath: string;
  backupFilePath: string;
  status: string;
  fileSizeBytes: number;
  failureReason: string | null;
  integrityVerified: boolean;
  correlationId: string;
  startedUtc: string;
  completedUtc: string | null;
}