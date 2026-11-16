import { DatePipe } from '@angular/common';
import { Component, OnDestroy, OnInit, inject } from '@angular/core';
import { forkJoin, Subscription } from 'rxjs';
import { MonitoringComponentRow } from '../core/system-monitoring.models';
import { SystemMonitoringService } from '../core/system-monitoring.service';
import { buildComponentRows, healthStatusClass } from '../core/system-monitoring.util';

@Component({
  selector: 'app-monitoring-components',
  standalone: true,
  imports: [DatePipe],
  templateUrl: './monitoring-components.component.html',
  styleUrl: './monitoring-components.component.scss',
})
export class MonitoringComponentsComponent implements OnInit, OnDestroy {
  private readonly api = inject(SystemMonitoringService);
  private pollSub?: Subscription;

  rows: MonitoringComponentRow[] = [];
  filtered: MonitoringComponentRow[] = [];
  category = '';
  status = '';
  error = '';

  ngOnInit(): void {
    this.pollSub = this.api.pollWhileVisible(
      () =>
        forkJoin({
          live: this.api.getLiveSnapshot(),
          components: this.api.getComponentSummaries(),
          workers: this.api.getWorkers(),
          broker: this.api.getBrokerHealth(),
          marketData: this.api.getMarketDataHealth(),
          signalR: this.api.getSignalRHealth(),
        }),
      25_000,
      {
        next: (bundle) => {
          this.error = '';
          this.rows = buildComponentRows({
            summaries: bundle.components,
            workers: bundle.workers,
            workerHeartbeats: bundle.live.workers,
            broker: bundle.broker,
            marketData: bundle.marketData,
            signalR: bundle.signalR,
          });
          this.applyFilters();
        },
        error: (msg) => (this.error = msg),
      },
    );
  }

  ngOnDestroy(): void {
    this.pollSub?.unsubscribe();
  }

  categories(): string[] {
    return [...new Set(this.rows.map((r) => r.category))].sort();
  }

  applyFilters(): void {
    this.filtered = this.rows.filter((r) => {
      if (this.category && r.category !== this.category) {
        return false;
      }
      if (this.status && r.status !== this.status) {
        return false;
      }
      return true;
    });
  }

  onCategory(value: string): void {
    this.category = value;
    this.applyFilters();
  }

  onStatus(value: string): void {
    this.status = value;
    this.applyFilters();
  }

  statusClass(status: string): string {
    return healthStatusClass(status as 'HEALTHY');
  }
}