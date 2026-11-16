import { DatePipe } from '@angular/common';
import { Component, OnDestroy, OnInit, inject } from '@angular/core';
import { Subscription, forkJoin } from 'rxjs';
import { SystemRecoveryHistoryRecord, SystemRecoveryPolicyRecord } from '../core/system-monitoring.models';
import { SystemMonitoringService } from '../core/system-monitoring.service';

@Component({
  selector: 'app-monitoring-recovery',
  standalone: true,
  imports: [DatePipe],
  templateUrl: './monitoring-recovery.component.html',
  styleUrl: './monitoring-recovery.component.scss',
})
export class MonitoringRecoveryComponent implements OnInit, OnDestroy {
  private readonly api = inject(SystemMonitoringService);
  private pollSub?: Subscription;

  history: SystemRecoveryHistoryRecord[] = [];
  policies: SystemRecoveryPolicyRecord[] = [];
  error = '';
  message = '';

  ngOnInit(): void {
    this.pollSub = this.api.pollWhileVisible(
      () =>
        forkJoin({
          history: this.api.listRecoveryHistory(100),
          policies: this.api.listRecoveryPolicies(),
        }),
      25_000,
      {
        next: (bundle) => {
          this.error = '';
          this.history = bundle.history;
          this.policies = bundle.policies;
        },
        error: (msg) => (this.error = msg),
      },
    );
  }

  ngOnDestroy(): void {
    this.pollSub?.unsubscribe();
  }

  failedAttempts(): SystemRecoveryHistoryRecord[] {
    return this.history.filter((h) => h.result === 'Failed' || h.result === 'Denied');
  }
}