import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, OnInit, inject } from '@angular/core';
import { forkJoin } from 'rxjs';
import {
  IncidentNotificationRecord,
  SystemHealthReportEntry,
  SystemIncidentStatistics,
} from '../core/system-monitoring.models';
import { SystemMonitoringService } from '../core/system-monitoring.service';
import { mapOverallStatus } from '../core/system-monitoring.util';

@Component({
  selector: 'app-monitoring-analytics',
  standalone: true,
  imports: [DatePipe, DecimalPipe],
  templateUrl: './monitoring-analytics.component.html',
  styleUrl: './monitoring-analytics.component.scss',
})
export class MonitoringAnalyticsComponent implements OnInit {
  private readonly api = inject(SystemMonitoringService);

  stats: SystemIncidentStatistics | null = null;
  reports: SystemHealthReportEntry[] = [];
  notifications: IncidentNotificationRecord[] = [];
  error = '';

  ngOnInit(): void {
    forkJoin({
      stats: this.api.getIncidentStatistics(),
      reports: this.api.listHealthReports(20),
      notifications: this.api.listNotifications({ limit: 200 }),
    }).subscribe({
      next: (bundle) => {
        this.stats = bundle.stats;
        this.reports = bundle.reports;
        this.notifications = bundle.notifications;
      },
      error: () => (this.error = 'Failed to load analytics'),
    });
  }

  severityBars(): { label: string; count: number; pct: number }[] {
    if (!this.stats) {
      return [];
    }
    const entries = Object.entries(this.stats.countsBySeverity);
    const max = Math.max(1, ...entries.map(([, v]) => v));
    return entries.map(([label, count]) => ({ label, count, pct: (count / max) * 100 }));
  }

  componentBars(): { label: string; count: number; pct: number }[] {
    if (!this.stats) {
      return [];
    }
    const entries = Object.entries(this.stats.countsByComponent);
    const max = Math.max(1, ...entries.map(([, v]) => v));
    return entries.map(([label, count]) => ({ label, count, pct: (count / max) * 100 }));
  }

  notificationSuccessRate(): number | null {
    const sent = this.notifications.filter((n) => n.deliveryStatus === 'Sent').length;
    const failed = this.notifications.filter((n) => n.deliveryStatus === 'Failed').length;
    const total = sent + failed;
    if (!total) {
      return null;
    }
    return (sent / total) * 100;
  }

  reportStatusClass(status: string): string {
    const mapped = mapOverallStatus(status as 'Healthy');
    return mapped.toLowerCase();
  }
}