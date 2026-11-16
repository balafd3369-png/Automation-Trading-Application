import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import {
  EMPTY,
  Observable,
  Subscription,
  catchError,
  filter,
  fromEvent,
  interval,
  map,
  merge,
  of,
  startWith,
  switchMap,
  tap,
} from 'rxjs';
import {
  BrokerConnectivityHealthSnapshot,
  EscalationPoliciesUpdateRequest,
  EscalationPolicyTierResponse,
  IncidentNotificationRecord,
  MarketDataHealthSnapshot,
  NotificationSettingsResponse,
  NotificationSettingsUpdateRequest,
  SignalRServerHealthSnapshot,
  SystemHealthComponentSummary,
  SystemHealthLiveSnapshot,
  SystemHealthReportEntry,
  SystemIncidentEventRecord,
  SystemIncidentListQuery,
  SystemIncidentNoteRecord,
  SystemIncidentRecord,
  SystemIncidentStatistics,
  WorkerHealthDetailSnapshot,
  DistributedLockRecord,
  LeaderElectionRecord,
  SystemInstanceRecord,
  SystemRecoveryHistoryRecord,
  SystemRecoveryPolicyRecord,
  SystemWorkerRegistryRecord,
  ManualRecoveryRequest,
  ProductionSafetyStatusSnapshot,
  DatabaseBackupRecord,
  TradingSafetySummarySnapshot,
  EmergencyGateAuditEntry,
  LiveTradingEmergencyGateSnapshot,
  BrokerCircuitBreakerSnapshot,
} from './system-monitoring.models';

const DEFAULT_TIMEOUT_MS = 15_000;

@Injectable({ providedIn: 'root' })
export class SystemMonitoringService {
  private readonly http = inject(HttpClient);

  getLiveSnapshot(): Observable<SystemHealthLiveSnapshot> {
    return this.http.get<SystemHealthLiveSnapshot>('/api/admin/system-health/live');
  }

  getComponentSummaries(): Observable<SystemHealthComponentSummary[]> {
    return this.http.get<SystemHealthComponentSummary[]>('/api/admin/system-health/components');
  }

  getWorkers(): Observable<WorkerHealthDetailSnapshot[]> {
    return this.http.get<WorkerHealthDetailSnapshot[]>('/api/admin/system-health/workers');
  }

  getBrokerHealth(): Observable<BrokerConnectivityHealthSnapshot> {
    return this.http.get<BrokerConnectivityHealthSnapshot>('/api/admin/system-health/brokers');
  }

  getMarketDataHealth(): Observable<MarketDataHealthSnapshot> {
    return this.http.get<MarketDataHealthSnapshot>('/api/admin/system-health/market-data');
  }

  getSignalRHealth(): Observable<SignalRServerHealthSnapshot> {
    return this.http.get<SignalRServerHealthSnapshot>('/api/admin/system-health/signalr');
  }

  listHealthReports(limit = 30): Observable<SystemHealthReportEntry[]> {
    return this.http.get<SystemHealthReportEntry[]>('/api/admin/system-health/reports', {
      params: { limit: String(limit) },
    });
  }

  listIncidents(query: SystemIncidentListQuery = {}): Observable<SystemIncidentRecord[]> {
    let params = new HttpParams();
    if (query.status) {
      params = params.set('status', query.status);
    }
    if (query.minSeverity) {
      params = params.set('minSeverity', query.minSeverity);
    }
    if (query.component) {
      params = params.set('component', query.component);
    }
    if (query.fromUtc) {
      params = params.set('fromUtc', query.fromUtc);
    }
    if (query.toUtc) {
      params = params.set('toUtc', query.toUtc);
    }
    params = params.set('limit', String(query.limit ?? 50));
    params = params.set('offset', String(query.offset ?? 0));
    return this.http.get<SystemIncidentRecord[]>('/api/admin/incidents', { params });
  }

  listActiveIncidents(limit = 100): Observable<SystemIncidentRecord[]> {
    return this.http.get<SystemIncidentRecord[]>('/api/admin/incidents/active', {
      params: { limit: String(limit) },
    });
  }

  getIncident(id: string): Observable<SystemIncidentRecord> {
    return this.http.get<SystemIncidentRecord>(`/api/admin/incidents/${encodeURIComponent(id)}`);
  }

  getIncidentStatistics(fromUtc?: string, toUtc?: string): Observable<SystemIncidentStatistics> {
    let params = new HttpParams();
    if (fromUtc) {
      params = params.set('fromUtc', fromUtc);
    }
    if (toUtc) {
      params = params.set('toUtc', toUtc);
    }
    return this.http.get<SystemIncidentStatistics>('/api/admin/incidents/statistics', { params });
  }

  listIncidentEvents(id: string, limit = 200): Observable<SystemIncidentEventRecord[]> {
    return this.http.get<SystemIncidentEventRecord[]>(
      `/api/admin/incidents/${encodeURIComponent(id)}/events`,
      { params: { limit: String(limit) } },
    );
  }

  listIncidentNotes(id: string, limit = 200): Observable<SystemIncidentNoteRecord[]> {
    return this.http.get<SystemIncidentNoteRecord[]>(
      `/api/admin/incidents/${encodeURIComponent(id)}/notes`,
      { params: { limit: String(limit) } },
    );
  }

  acknowledgeIncident(id: string): Observable<SystemIncidentRecord> {
    return this.http.post<SystemIncidentRecord>(`/api/admin/incidents/${encodeURIComponent(id)}/acknowledge`, {});
  }

  investigateIncident(id: string): Observable<SystemIncidentRecord> {
    return this.http.post<SystemIncidentRecord>(`/api/admin/incidents/${encodeURIComponent(id)}/investigate`, {});
  }

  resolveIncident(id: string, message?: string): Observable<SystemIncidentRecord> {
    return this.http.post<SystemIncidentRecord>(`/api/admin/incidents/${encodeURIComponent(id)}/resolve`, {
      message: message ?? null,
    });
  }

  closeIncident(id: string, message?: string): Observable<SystemIncidentRecord> {
    return this.http.post<SystemIncidentRecord>(`/api/admin/incidents/${encodeURIComponent(id)}/close`, {
      message: message ?? null,
    });
  }

  addIncidentNote(id: string, note: string): Observable<SystemIncidentNoteRecord> {
    return this.http.post<SystemIncidentNoteRecord>(`/api/admin/incidents/${encodeURIComponent(id)}/notes`, { note });
  }

  listNotifications(query: {
    incidentId?: string;
    channel?: string;
    status?: string;
    limit?: number;
    offset?: number;
  } = {}): Observable<IncidentNotificationRecord[]> {
    let params = new HttpParams();
    if (query.incidentId) {
      params = params.set('incidentId', query.incidentId);
    }
    if (query.channel) {
      params = params.set('channel', query.channel);
    }
    if (query.status) {
      params = params.set('status', query.status);
    }
    params = params.set('limit', String(query.limit ?? 50));
    params = params.set('offset', String(query.offset ?? 0));
    return this.http.get<IncidentNotificationRecord[]>('/api/admin/notifications', { params });
  }

  getNotification(id: string): Observable<IncidentNotificationRecord> {
    return this.http.get<IncidentNotificationRecord>(`/api/admin/notifications/${encodeURIComponent(id)}`);
  }

  listIncidentNotifications(incidentId: string, limit = 50): Observable<IncidentNotificationRecord[]> {
    return this.http.get<IncidentNotificationRecord[]>(
      `/api/admin/incidents/${encodeURIComponent(incidentId)}/notifications`,
      { params: { limit: String(limit) } },
    );
  }

  retryNotification(id: string): Observable<IncidentNotificationRecord> {
    return this.http.post<IncidentNotificationRecord>(
      `/api/admin/notifications/retry/${encodeURIComponent(id)}`,
      {},
    );
  }

  getNotificationSettings(): Observable<NotificationSettingsResponse> {
    return this.http.get<NotificationSettingsResponse>('/api/admin/notification-settings');
  }

  updateNotificationSettings(body: NotificationSettingsUpdateRequest): Observable<NotificationSettingsResponse> {
    return this.http.put<NotificationSettingsResponse>('/api/admin/notification-settings', body);
  }

  getEscalationPolicies(): Observable<EscalationPolicyTierResponse[]> {
    return this.http.get<EscalationPolicyTierResponse[]>('/api/admin/escalation-policies');
  }

  updateEscalationPolicies(body: EscalationPoliciesUpdateRequest): Observable<EscalationPolicyTierResponse[]> {
    return this.http.put<EscalationPolicyTierResponse[]>('/api/admin/escalation-policies', body);
  }

  testEmail(recipientOverride?: string): Observable<IncidentNotificationRecord> {
    return this.http.post<IncidentNotificationRecord>('/api/admin/notifications/test-email', {
      recipientOverride: recipientOverride ?? null,
    });
  }

  testTelegram(chatIdOverride?: number): Observable<IncidentNotificationRecord> {
    return this.http.post<IncidentNotificationRecord>('/api/admin/notifications/test-telegram', {
      chatIdOverride: chatIdOverride ?? null,
    });
  }

  listSystemInstances(): Observable<SystemInstanceRecord[]> {
    return this.http.get<SystemInstanceRecord[]>('/api/admin/system/instances');
  }

  getSystemInstance(id: string): Observable<SystemInstanceRecord> {
    return this.http.get<SystemInstanceRecord>(`/api/admin/system/instances/${encodeURIComponent(id)}`);
  }

  drainSystemInstance(id: string): Observable<{ status: string; instanceId: string }> {
    return this.http.post<{ status: string; instanceId: string }>(
      `/api/admin/system/instances/${encodeURIComponent(id)}/drain`,
      {},
    );
  }

  listCoordinationWorkers(instanceId?: string): Observable<SystemWorkerRegistryRecord[]> {
    let params = new HttpParams();
    if (instanceId) {
      params = params.set('instanceId', instanceId);
    }
    return this.http.get<SystemWorkerRegistryRecord[]>('/api/admin/system/workers', { params });
  }

  getCoordinationWorker(id: string): Observable<SystemWorkerRegistryRecord> {
    return this.http.get<SystemWorkerRegistryRecord>(`/api/admin/system/workers/${encodeURIComponent(id)}`);
  }

  listDistributedLocks(): Observable<DistributedLockRecord[]> {
    return this.http.get<DistributedLockRecord[]>('/api/admin/system/locks');
  }

  getLeaderElection(role?: string): Observable<LeaderElectionRecord> {
    let params = new HttpParams();
    if (role) {
      params = params.set('role', role);
    }
    return this.http.get<LeaderElectionRecord>('/api/admin/system/leader', { params });
  }

  listRecoveryHistory(limit = 100): Observable<SystemRecoveryHistoryRecord[]> {
    return this.http.get<SystemRecoveryHistoryRecord[]>('/api/admin/system/recovery/history', {
      params: { limit: String(limit) },
    });
  }

  listRecoveryPolicies(): Observable<SystemRecoveryPolicyRecord[]> {
    return this.http.get<SystemRecoveryPolicyRecord[]>('/api/admin/system/recovery/policies');
  }

  updateRecoveryPolicies(
    policies: SystemRecoveryPolicyRecord[],
  ): Observable<SystemRecoveryPolicyRecord[]> {
    return this.http.put<SystemRecoveryPolicyRecord[]>('/api/admin/system/recovery/policies', policies);
  }

  triggerWorkerRecovery(
    workerId: string,
    body: ManualRecoveryRequest = {},
  ): Observable<SystemRecoveryHistoryRecord> {
    return this.http.post<SystemRecoveryHistoryRecord>(
      `/api/admin/system/workers/${encodeURIComponent(workerId)}/recovery`,
      body,
    );
  }

  disableWorkerRecovery(workerId: string): Observable<SystemWorkerRegistryRecord> {
    return this.http.post<SystemWorkerRegistryRecord>(
      `/api/admin/system/workers/${encodeURIComponent(workerId)}/disable-recovery`,
      {},
    );
  }

  enableWorkerRecovery(workerId: string): Observable<SystemWorkerRegistryRecord> {
    return this.http.post<SystemWorkerRegistryRecord>(
      `/api/admin/system/workers/${encodeURIComponent(workerId)}/enable-recovery`,
      {},
    );
  }

  getProductionSafetyStatus(): Observable<ProductionSafetyStatusSnapshot> {
    return this.http.get<ProductionSafetyStatusSnapshot>('/api/admin/production-safety/status');
  }

  getTradingSafetySummary(): Observable<TradingSafetySummarySnapshot> {
    return this.http.get<TradingSafetySummarySnapshot>('/api/admin/trading-safety/summary');
  }

  getTradingSafetyGateAudit(limit = 50): Observable<EmergencyGateAuditEntry[]> {
    return this.http.get<EmergencyGateAuditEntry[]>(
      `/api/admin/trading-safety/emergency-gate/audit?limit=${limit}`,
    );
  }

  haltTradingEmergencyGate(reason: string, confirmation: string): Observable<LiveTradingEmergencyGateSnapshot> {
    return this.http.post<LiveTradingEmergencyGateSnapshot>('/api/admin/trading-safety/emergency-gate/halt', {
      reason,
      confirmation,
    });
  }

  clearTradingEmergencyGate(reason: string, confirmation: string): Observable<LiveTradingEmergencyGateSnapshot> {
    return this.http.post<LiveTradingEmergencyGateSnapshot>('/api/admin/trading-safety/emergency-gate/clear', {
      reason,
      confirmation,
    });
  }

  resetTradingCircuitBreaker(reason: string, confirmation: string): Observable<BrokerCircuitBreakerSnapshot> {
    return this.http.post<BrokerCircuitBreakerSnapshot>('/api/admin/trading-safety/circuit-breaker/reset', {
      reason,
      confirmation,
    });
  }

  triggerProductionBackup(): Observable<DatabaseBackupRecord> {
    return this.http.post<DatabaseBackupRecord>('/api/admin/production-safety/backups', {});
  }

  /**
   * Polls while the document is visible; pauses when hidden. Uses switchMap to avoid overlap.
   */
  pollWhileVisible<T>(
    loader: () => Observable<T>,
    periodMs: number,
    callbacks: {
      next: (value: T) => void;
      error?: (message: string) => void;
    },
  ): Subscription {
    const visible$ =
      typeof document === 'undefined'
        ? of(true)
        : merge(
            of(!document.hidden),
            fromEvent(document, 'visibilitychange').pipe(map(() => !document.hidden)),
          );

    return visible$
      .pipe(
        switchMap((visible) => {
          if (!visible) {
            return EMPTY;
          }
          return interval(periodMs).pipe(
            startWith(0),
            switchMap(() =>
              loader().pipe(
                catchError((err: unknown) => {
                  callbacks.error?.(err instanceof Error ? err.message : 'Request failed');
                  return EMPTY;
                }),
              ),
            ),
          );
        }),
        tap((value) => callbacks.next(value)),
      )
      .subscribe();
  }
}