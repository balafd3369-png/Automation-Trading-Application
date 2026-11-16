import { DatePipe } from '@angular/common';
import { Component, OnDestroy, OnInit, inject } from '@angular/core';
import { Subscription } from 'rxjs';
import { SystemInstanceRecord } from '../core/system-monitoring.models';
import { SystemMonitoringService } from '../core/system-monitoring.service';

@Component({
  selector: 'app-monitoring-instances',
  standalone: true,
  imports: [DatePipe],
  templateUrl: './monitoring-instances.component.html',
  styleUrl: './monitoring-instances.component.scss',
})
export class MonitoringInstancesComponent implements OnInit, OnDestroy {
  private readonly api = inject(SystemMonitoringService);
  private pollSub?: Subscription;

  instances: SystemInstanceRecord[] = [];
  error = '';
  actionMessage = '';

  ngOnInit(): void {
    this.pollSub = this.api.pollWhileVisible(
      () => this.api.listSystemInstances(),
      20_000,
      {
        next: (rows) => {
          this.error = '';
          this.instances = rows;
        },
        error: (msg) => (this.error = msg),
      },
    );
  }

  ngOnDestroy(): void {
    this.pollSub?.unsubscribe();
  }

  drain(instance: SystemInstanceRecord): void {
    if (
      !confirm(
        `Mark instance ${instance.instanceId} as draining? New singleton work should move away; this does not stop trading.`,
      )
    ) {
      return;
    }
    this.actionMessage = '';
    this.api.drainSystemInstance(instance.instanceId).subscribe({
      next: () => {
        this.actionMessage = `Drain requested for ${instance.instanceId}.`;
      },
      error: (err: unknown) => {
        this.error = err instanceof Error ? err.message : 'Drain failed';
      },
    });
  }
}