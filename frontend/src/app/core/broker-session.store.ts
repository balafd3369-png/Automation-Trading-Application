import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, Subject, catchError, exhaustMap, interval, merge, of, shareReplay, startWith, timeout } from 'rxjs';

export type BrokerSessionSnapshot = {
  connected?: boolean;
  provider?: string | null;
  displayName?: string | null;
  availableFunds?: number | null;
  utilizedFunds?: number | null;
};

@Injectable({ providedIn: 'root' })
export class BrokerSessionStore {
  private readonly http = inject(HttpClient);
  private readonly refresh$ = new Subject<void>();

  private readonly session$ = merge(interval(10_000).pipe(startWith(0)), this.refresh$).pipe(
    exhaustMap(() => this.fetch()),
    shareReplay({ bufferSize: 1, refCount: true }),
  );

  watch(): Observable<BrokerSessionSnapshot> {
    return this.session$;
  }

  refresh(): void {
    this.refresh$.next();
  }

  private fetch(): Observable<BrokerSessionSnapshot> {
    return this.http.get<BrokerSessionSnapshot>('/api/broker/session').pipe(
      timeout(8_000),
      catchError(() => of({ connected: false })),
    );
  }
}
