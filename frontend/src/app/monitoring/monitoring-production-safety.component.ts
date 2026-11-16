import { DatePipe } from '@angular/common';
import { Component, OnDestroy, OnInit, inject } from '@angular/core';
import { Subscription } from 'rxjs';
import { ProductionSafetyStatusSnapshot } from '../core/system-monitoring.models';
import { SystemMonitoringService } from '../core/system-monitoring.service';

@Component({
  selector: 'app-monitoring-production-safety',
  standalone: true,
  imports: [DatePipe],
  templateUrl: './monitoring-production-safety.component.html',
  styleUrl: './monitoring-production-safety.component.scss',
})
export class MonitoringProductionSafetyComponent implements OnInit, OnDestroy {
  private readonly api = inject(SystemMonitoringService);
  private pollSub?: Subscription;

  status: ProductionSafetyStatusSnapshot | null = null;
  error = '';
  message = '';

  ngOnInit(): void {
    this.pollSub = this.api.pollWhileVisible(
      () => this.api.getProductionSafetyStatus(),
      25_000,
      {
        next: (s) => {
          this.error = '';
          this.status = s;
        },
        error: (msg) => (this.error = msg),
      },
    );
  }

  ngOnDestroy(): void {
    this.pollSub?.unsubscribe();
  }

  triggerBackup(): void {
    if (!confirm('Create a SQLite backup now? This does not modify trading state.')) {
      return;
    }
    this.message = '';
    this.api.triggerProductionBackup().subscribe({
      next: (b) => {
        this.message = `Backup ${b.status} (${b.fileSizeBytes} bytes).`;
      },
      error: (err: unknown) => {
        this.error = err instanceof Error ? err.message : 'Backup failed';
      },
    });
  }
}