import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ChartStreamService, ChartStreamStatus } from './chart-stream.service';

/** Chart live stream wiring (SignalR watch + honest connection status). */
@Injectable({ providedIn: 'root' })
export class ChartLiveCoordinator {
  private readonly stream = inject(ChartStreamService);

  readonly live$ = this.stream.live$;
  readonly status$: Observable<ChartStreamStatus> = this.stream.status$;

  snapshotStatus(): ChartStreamStatus {
    return this.stream.snapshotStatus();
  }

  isStreamingActive(): boolean {
    return this.stream.isStreamingActive();
  }

  syncWatch(name: string, interval: string): Promise<void> {
    return this.stream.syncWatch(name, interval);
  }

  disconnect(): Promise<void> {
    return this.stream.disconnect();
  }
}
