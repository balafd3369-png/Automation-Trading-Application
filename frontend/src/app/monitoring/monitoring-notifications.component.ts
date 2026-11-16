import { DatePipe } from '@angular/common';
import { Component, OnDestroy, OnInit, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Subscription } from 'rxjs';
import { IncidentNotificationRecord } from '../core/system-monitoring.models';
import { SystemMonitoringService } from '../core/system-monitoring.service';

@Component({
  selector: 'app-monitoring-notifications',
  standalone: true,
  imports: [DatePipe, FormsModule, RouterLink],
  templateUrl: './monitoring-notifications.component.html',
  styleUrl: './monitoring-notifications.component.scss',
})
export class MonitoringNotificationsComponent implements OnInit, OnDestroy {
  private readonly api = inject(SystemMonitoringService);
  private pollSub?: Subscription;

  rows: IncidentNotificationRecord[] = [];
  channel = '';
  status = '';
  error = '';
  toast = '';
  busy = false;

  ngOnInit(): void {
    this.pollSub = this.api.pollWhileVisible(
      () =>
        this.api.listNotifications({
          channel: this.channel || undefined,
          status: this.status || undefined,
          limit: 100,
        }),
      20_000,
      {
        next: (rows) => {
          this.error = '';
          this.rows = rows;
        },
        error: (msg) => (this.error = msg),
      },
    );
  }

  ngOnDestroy(): void {
    this.pollSub?.unsubscribe();
  }

  reload(): void {
    this.api
      .listNotifications({
        channel: this.channel || undefined,
        status: this.status || undefined,
        limit: 100,
      })
      .subscribe({
        next: (rows) => (this.rows = rows),
        error: () => (this.error = 'Failed to load notifications'),
      });
  }

  retry(id: string): void {
    if (!confirm('Retry delivery for this notification?')) {
      return;
    }
    this.busy = true;
    this.api.retryNotification(id).subscribe({
      next: () => {
        this.toast = 'Retry queued';
        this.reload();
        this.busy = false;
      },
      error: () => {
        this.toast = 'Retry failed';
        this.busy = false;
      },
    });
  }

  canRetry(row: IncidentNotificationRecord): boolean {
    return row.deliveryStatus === 'Failed' || row.deliveryStatus === 'RetryScheduled';
  }
}