import { DatePipe } from '@angular/common';
import { Component, OnDestroy, OnInit, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Subscription, forkJoin } from 'rxjs';
import {
  DistributedLockRecord,
  SystemInstanceRecord,
  SystemRecoveryHistoryRecord,
  SystemWorkerRegistryRecord,
} from '../core/system-monitoring.models';
import { SystemMonitoringService } from '../core/system-monitoring.service';

@Component({
  selector: 'app-monitoring-reliability',
  standalone: true,
  imports: [DatePipe, RouterLink],
  templateUrl: './monitoring-reliability.component.html',
  styleUrl: './monitoring-reliability.component.scss',
})
export class MonitoringReliabilityComponent implements OnInit, OnDestroy {
  private readonly api = inject(SystemMonitoringService);
  private pollSub?: Subscription;

  error = '';
  instances: SystemInstanceRecord[] = [];
  workers: SystemWorkerRegistryRecord[] = [];
  locks: DistributedLockRecord[] = [];
  history: SystemRecoveryHistoryRecord[] = [];
  lastRefresh: string | null = null;

  ngOnInit(): void {
    this.pollSub = this.api.pollWhileVisible(
      () =>
        forkJoin({
          instances: this.api.listSystemInstances(),
          workers: this.api.listCoordinationWorkers(),
          locks: this.api.listDistributedLocks(),
          history: this.api.listRecoveryHistory(50),
        }),
      20_000,
      {
        next: (bundle) => {
          this.error = '';
          this.instances = bundle.instances;
          this.workers = bundle.workers;
          this.locks = bundle.locks;
          this.history = bundle.history;
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

  activeInstances(): number {
    return this.instances.filter((i) => !i.isDraining).length;
  }

  healthyWorkers(): number {
    return this.workers.filter((w) => w.status === 'Running').length;
  }

  unresponsiveWorkers(): number {
    return this.workers.filter((w) => w.status === 'Unresponsive' || w.status === 'Failed').length;
  }

  activeLocks(): number {
    return this.locks.filter((l) => !l.isExpired).length;
  }

  expiredLocks(): number {
    return this.locks.filter((l) => l.isExpired).length;
  }

  recoverySuccessRate(): string {
    const completed = this.history.filter((h) => h.result === 'Succeeded' || h.result === 'Failed');
    if (!completed.length) {
      return '—';
    }
    const ok = completed.filter((h) => h.result === 'Succeeded').length;
    return `${Math.round((ok / completed.length) * 100)}%`;
  }
}