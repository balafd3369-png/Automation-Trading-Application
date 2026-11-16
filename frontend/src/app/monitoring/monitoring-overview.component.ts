import { DatePipe } from '@angular/common';
import { Component, OnDestroy, OnInit, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { forkJoin, Subscription } from 'rxjs';
import {
  IncidentNotificationRecord,
  SystemHealthLiveSnapshot,
  SystemIncidentStatistics,
} from '../core/system-monitoring.models';
import { SystemMonitoringService } from '../core/system-monitoring.service';
import {
  buildComponentRows,
  countComponentsByUiStatus,
  healthStatusClass,
  mapOverallStatus,
} from '../core/system-monitoring.util';

@Component({
  selector: 'app-monitoring-overview',
  standalone: true,
  imports: [DatePipe, RouterLink],
  templateUrl: './monitoring-overview.component.html',
  styleUrl: './monitoring-overview.component.scss',
})
export class MonitoringOverviewComponent implements OnInit, OnDestroy {
  private readonly api = inject(SystemMonitoringService);
  private pollSub?: Subscription;

  live: SystemHealthLiveSnapshot | null = null;
  stats: SystemIncidentStatistics | null = null;
  notifications: IncidentNotificationRecord[] = [];
  componentCounts = { HEALTHY: 0, DEGRADED: 0, UNHEALTHY: 0, UNKNOWN: 0 };
  error = '';
  lastRefresh: string | null = null;

  ngOnInit(): void {
    this.pollSub = this.api.pollWhileVisible(
      () =>
        forkJoin({
          live: this.api.getLiveSnapshot(),
          stats: this.api.getIncidentStatistics(),
          components: this.api.getComponentSummaries(),
          workers: this.api.getWorkers(),
          broker: this.api.getBrokerHealth(),
          marketData: this.api.getMarketDataHealth(),
          signalR: this.api.getSignalRHealth(),
          notifications: this.api.listNotifications({ limit: 100 }),
        }),
      20_000,
      {
        next: (bundle) => {
          this.error = '';
          this.live = bundle.live;
          this.stats = bundle.stats;
          this.notifications = bundle.notifications;
          const rows = buildComponentRows({
            summaries: bundle.components,
            workers: bundle.workers,
            workerHeartbeats: bundle.live.workers,
            broker: bundle.broker,
            marketData: bundle.marketData,
            signalR: bundle.signalR,
          });
          this.componentCounts = countComponentsByUiStatus(rows);
          this.lastRefresh = new Date().toISOString();
        },
        error: (msg) => {
          this.error = msg;
        },
      },
    );
  }

  ngOnDestroy(): void {
    this.pollSub?.unsubscribe();
  }

  overallClass(): string {
    return healthStatusClass(mapOverallStatus(this.live?.overallStatus));
  }

  notificationSentCount(): number {
    return this.notifications.filter((n) => n.deliveryStatus === 'Sent').length;
  }

  notificationFailedCount(): number {
    return this.notifications.filter((n) => n.deliveryStatus === 'Failed').length;
  }

  notificationPendingCount(): number {
    return this.notifications.filter(
      (n) => n.deliveryStatus === 'Pending' || n.deliveryStatus === 'RetryScheduled' || n.deliveryStatus === 'Processing',
    ).length;
  }
}