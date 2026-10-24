import { Injectable } from '@angular/core';
import * as signalR from '@microsoft/signalr';
import { BehaviorSubject, Observable, Subject } from 'rxjs';
import { ChartLiveUpdate } from './board.models';

export type ChartStreamStatus = {
  hubConnected: boolean;
  watchActive: boolean;
  /** True when a live tick arrived recently while hub is connected and watch is active. */
  streaming: boolean;
};

const STREAM_STALE_MS = 20_000;

@Injectable({ providedIn: 'root' })
export class ChartStreamService {
  private hub?: signalR.HubConnection;
  private readonly live = new Subject<ChartLiveUpdate>();
  private watchKey = '';
  private connecting: Promise<void> | null = null;
  private lastLiveAt = 0;

  private readonly statusSubject = new BehaviorSubject<ChartStreamStatus>({
    hubConnected: false,
    watchActive: false,
    streaming: false,
  });

  readonly live$: Observable<ChartLiveUpdate> = this.live.asObservable();
  readonly status$: Observable<ChartStreamStatus> = this.statusSubject.asObservable();

  snapshotStatus(): ChartStreamStatus {
    return this.refreshStreamingFlag(this.statusSubject.value);
  }

  isConnected(): boolean {
    return this.hub?.state === signalR.HubConnectionState.Connected;
  }

  isStreamingActive(): boolean {
    return this.refreshStreamingFlag(this.statusSubject.value).streaming;
  }

  async syncWatch(name: string, interval: string): Promise<void> {
    const key = `${name.toUpperCase()}|${interval}`;
    if (this.watchKey === key && this.isConnected()) {
      this.patchStatus({ watchActive: true });
      return;
    }
    await this.ensureConnected();
    if (this.watchKey && this.watchKey !== key) {
      const [prevName, prevInterval] = this.watchKey.split('|');
      await this.hub!.invoke('UnwatchChart', prevName, prevInterval);
      this.patchStatus({ watchActive: false, streaming: false });
    }
    await this.hub!.invoke('WatchChart', name, interval);
    this.watchKey = key;
    this.lastLiveAt = 0;
    this.patchStatus({ watchActive: true, streaming: false });
  }

  async disconnect(): Promise<void> {
    if (!this.hub) {
      return;
    }
    try {
      if (this.watchKey) {
        const [prevName, prevInterval] = this.watchKey.split('|');
        await this.hub.invoke('UnwatchChart', prevName, prevInterval);
      }
      await this.hub.stop();
    } catch {
      // ignore teardown errors
    }
    this.hub = undefined;
    this.watchKey = '';
    this.connecting = null;
    this.lastLiveAt = 0;
    this.patchStatus({ hubConnected: false, watchActive: false, streaming: false });
  }

  private async ensureConnected(): Promise<void> {
    if (this.isConnected()) {
      this.patchStatus({ hubConnected: true });
      return;
    }
    if (this.connecting) {
      await this.connecting;
      return;
    }
    this.connecting = this.startHub();
    await this.connecting;
    this.connecting = null;
  }

  private async startHub(): Promise<void> {
    this.hub = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/market-chart')
      .withAutomaticReconnect([0, 2000, 5000, 10000])
      .configureLogging(signalR.LogLevel.Warning)
      .build();

    this.hub.on('chartLive', (payload: ChartLiveUpdate) => {
      this.lastLiveAt = Date.now();
      this.patchStatus({ streaming: true });
      this.live.next(payload);
    });

    this.hub.onreconnecting(() => {
      this.patchStatus({ hubConnected: false, streaming: false });
    });

    this.hub.onreconnected(async () => {
      this.patchStatus({ hubConnected: true, streaming: false });
      await this.restoreWatchAfterReconnect();
    });

    this.hub.onclose(() => {
      this.patchStatus({ hubConnected: false, streaming: false });
    });

    await this.hub.start();
    this.patchStatus({ hubConnected: true });
  }

  private async restoreWatchAfterReconnect(): Promise<void> {
    if (!this.hub || !this.watchKey) {
      return;
    }
    const [name, interval] = this.watchKey.split('|');
    if (!name || !interval) {
      return;
    }
    try {
      await this.hub.invoke('WatchChart', name, interval);
      this.lastLiveAt = 0;
      this.patchStatus({ watchActive: true, streaming: false });
    } catch {
      this.patchStatus({ watchActive: false, streaming: false });
    }
  }

  private patchStatus(patch: Partial<ChartStreamStatus>): void {
    const next = this.refreshStreamingFlag({ ...this.statusSubject.value, ...patch });
    this.statusSubject.next(next);
  }

  private refreshStreamingFlag(status: ChartStreamStatus): ChartStreamStatus {
    const streaming =
      status.hubConnected
      && status.watchActive
      && this.lastLiveAt > 0
      && Date.now() - this.lastLiveAt <= STREAM_STALE_MS;
    return { ...status, streaming };
  }
}
