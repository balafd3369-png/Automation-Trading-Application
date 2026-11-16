import { DatePipe } from '@angular/common';
import { Component, OnDestroy, OnInit, inject } from '@angular/core';
import { Subscription } from 'rxjs';
import { DistributedLockRecord } from '../core/system-monitoring.models';
import { SystemMonitoringService } from '../core/system-monitoring.service';

@Component({
  selector: 'app-monitoring-locks',
  standalone: true,
  imports: [DatePipe],
  templateUrl: './monitoring-locks.component.html',
  styleUrl: './monitoring-locks.component.scss',
})
export class MonitoringLocksComponent implements OnInit, OnDestroy {
  private readonly api = inject(SystemMonitoringService);
  private pollSub?: Subscription;

  locks: DistributedLockRecord[] = [];
  error = '';

  ngOnInit(): void {
    this.pollSub = this.api.pollWhileVisible(
      () => this.api.listDistributedLocks(),
      20_000,
      {
        next: (rows) => {
          this.error = '';
          this.locks = rows;
        },
        error: (msg) => (this.error = msg),
      },
    );
  }

  ngOnDestroy(): void {
    this.pollSub?.unsubscribe();
  }
}