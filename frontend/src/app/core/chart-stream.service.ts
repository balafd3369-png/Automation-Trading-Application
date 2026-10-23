import { Injectable } from '@angular/core';
import * as signalR from '@microsoft/signalr';
import { Observable, Subject } from 'rxjs';
import { ChartLiveUpdate } from './board.models';

@Injectable({ providedIn: 'root' })
export class ChartStreamService {
  private hub?: signalR.HubConnection;
  private readonly live = new Subject<ChartLiveUpdate>();
  private watchKey = '';
  private connecting: Promise<void> | null = null;

  readonly live$: Observable<ChartLiveUpdate> = this.live.asObservable();

  isConnected(): boolean {
    return this.hub?.state === signalR.HubConnectionState.Connected;
  }

  async syncWatch(name: string, interval: string): Promise<void> {
    const key = `${name.toUpperCase()}|${interval}`;
    if (this.watchKey === key && this.isConnected()) {
      return;
    }
    await this.ensureConnected();
    if (this.watchKey && this.watchKey !== key) {
      const [prevName, prevInterval] = this.watchKey.split('|');
      await this.hub!.invoke('UnwatchChart', prevName, prevInterval);
    }
    await this.hub!.invoke('WatchChart', name, interval);
    this.watchKey = key;
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
  }

  private async ensureConnected(): Promise<void> {
    if (this.isConnected()) {
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
      this.live.next(payload);
    });
    await this.hub.start();
  }
}
