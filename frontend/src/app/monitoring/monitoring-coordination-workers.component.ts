import { DatePipe } from '@angular/common';
import { Component, OnDestroy, OnInit, inject } from '@angular/core';
import { Subscription } from 'rxjs';
import { SystemWorkerRegistryRecord } from '../core/system-monitoring.models';
import { SystemMonitoringService } from '../core/system-monitoring.service';

@Component({
  selector: 'app-monitoring-coordination-workers',
  standalone: true,
  imports: [DatePipe],
  templateUrl: './monitoring-coordination-workers.component.html',
  styleUrl: './monitoring-coordination-workers.component.scss',
})
export class MonitoringCoordinationWorkersComponent implements OnInit, OnDestroy {
  private readonly api = inject(SystemMonitoringService);
  private pollSub?: Subscription;

  workers: SystemWorkerRegistryRecord[] = [];
  error = '';
  message = '';

  ngOnInit(): void {
    this.pollSub = this.api.pollWhileVisible(
      () => this.api.listCoordinationWorkers(),
      20_000,
      {
        next: (rows) => {
          this.error = '';
          this.workers = rows;
        },
        error: (msg) => (this.error = msg),
      },
    );
  }

  ngOnDestroy(): void {
    this.pollSub?.unsubscribe();
  }

  isTradingCritical(worker: SystemWorkerRegistryRecord): boolean {
    return worker.workerType === 'trading' || !worker.recoveryEligible;
  }

  recover(worker: SystemWorkerRegistryRecord): void {
    if (this.isTradingCritical(worker)) {
      alert('Manual recovery is blocked for trading-critical workers from this dashboard.');
      return;
    }
    if (
      !confirm(
        `Attempt safe recovery for ${worker.workerName}? This only runs policy-approved housekeeping actions.`,
      )
    ) {
      return;
    }
    this.message = '';
    this.api.triggerWorkerRecovery(worker.workerId, { reason: 'Admin dashboard recovery' }).subscribe({
      next: (h) => {
        this.message = `Recovery ${h.result} for ${worker.workerName} (${h.correlationId}).`;
      },
      error: (err: unknown) => {
        this.error = err instanceof Error ? err.message : 'Recovery failed';
      },
    });
  }

  toggleRecovery(worker: SystemWorkerRegistryRecord, enable: boolean): void {
    if (this.isTradingCritical(worker)) {
      return;
    }
    const call = enable
      ? this.api.enableWorkerRecovery(worker.workerId)
      : this.api.disableWorkerRecovery(worker.workerId);
    call.subscribe({
      next: () => {
        this.message = enable ? 'Automatic recovery enabled.' : 'Automatic recovery disabled.';
      },
      error: (err: unknown) => {
        this.error = err instanceof Error ? err.message : 'Update failed';
      },
    });
  }
}