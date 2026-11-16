import { DatePipe } from '@angular/common';
import { Component, OnDestroy, OnInit, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Subscription } from 'rxjs';
import { SystemIncidentRecord, SystemIncidentStatus } from '../core/system-monitoring.models';
import { SystemMonitoringService } from '../core/system-monitoring.service';
import { severityClass } from '../core/system-monitoring.util';

@Component({
  selector: 'app-monitoring-incidents',
  standalone: true,
  imports: [DatePipe, RouterLink, FormsModule],
  templateUrl: './monitoring-incidents.component.html',
  styleUrl: './monitoring-incidents.component.scss',
})
export class MonitoringIncidentsComponent implements OnInit, OnDestroy {
  private readonly api = inject(SystemMonitoringService);
  private pollSub?: Subscription;

  incidents: SystemIncidentRecord[] = [];
  error = '';
  status: SystemIncidentStatus | '' = '';
  minSeverity = '';
  component = '';
  offset = 0;
  readonly limit = 50;
  actionBusy = false;
  toast = '';

  ngOnInit(): void {
    this.pollSub = this.api.pollWhileVisible(
      () =>
        this.api.listIncidents({
          status: this.status || undefined,
          minSeverity: (this.minSeverity as 'High') || undefined,
          component: this.component || undefined,
          limit: this.limit,
          offset: this.offset,
        }),
      30_000,
      {
        next: (rows) => {
          this.error = '';
          this.incidents = rows;
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
      .listIncidents({
        status: this.status || undefined,
        minSeverity: (this.minSeverity as 'High') || undefined,
        component: this.component || undefined,
        limit: this.limit,
        offset: this.offset,
      })
      .subscribe({
        next: (rows) => (this.incidents = rows),
        error: () => (this.error = 'Failed to reload incidents'),
      });
  }

  sevClass(s: string): string {
    return severityClass(s);
  }

  ack(id: string): void {
    if (!confirm('Acknowledge this incident?')) {
      return;
    }
    this.runAction(() => this.api.acknowledgeIncident(id));
  }

  investigate(id: string): void {
    this.runAction(() => this.api.investigateIncident(id));
  }

  resolve(id: string): void {
    if (!confirm('Resolve this incident?')) {
      return;
    }
    this.runAction(() => this.api.resolveIncident(id));
  }

  close(id: string): void {
    if (!confirm('Close this incident? This is an administrative action.')) {
      return;
    }
    this.runAction(() => this.api.closeIncident(id));
  }

  private runAction(call: () => ReturnType<SystemMonitoringService['acknowledgeIncident']>): void {
    this.actionBusy = true;
    call().subscribe({
      next: () => {
        this.toast = 'Incident updated';
        this.reload();
        this.actionBusy = false;
      },
      error: (err: unknown) => {
        this.toast = err instanceof Error ? err.message : 'Action failed';
        this.actionBusy = false;
      },
    });
  }
}