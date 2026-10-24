import { Injectable, inject } from '@angular/core';
import {
  Observable,
  Subject,
  catchError,
  exhaustMap,
  filter,
  interval,
  map,
  merge,
  of,
  shareReplay,
  startWith,
} from 'rxjs';
import { BoardService } from '../core/board.service';
import { PaperTradeHistoryItem, TradeHistoryRange } from '../core/board.models';
import { filterCatalogPaperHistoryTrades } from '../core/trading-ui.util';
import {
  StrategyFeedResponse,
  StrategyTrackRecordResponse,
  ZoneStrategyRolloutResponse,
} from '../core/strategies.models';

export type StrategyPnlQuery = {
  range: TradeHistoryRange;
  from: string | null;
  to: string | null;
};

export function strategyPnlQueryKey(query: StrategyPnlQuery): string {
  if (query.from) {
    return `d:${query.from}:${query.to || query.from}`;
  }
  return `r:${query.range}`;
}

@Injectable({ providedIn: 'root' })
export class StrategiesPageStore {
  private readonly board = inject(BoardService);

  private feedSnapshot: StrategyFeedResponse | null = null;
  private trackRecordSnapshot: StrategyTrackRecordResponse | null = null;
  private zoneRolloutSnapshot: ZoneStrategyRolloutResponse | null = null;
  private readonly pnlSnapshots = new Map<string, PaperTradeHistoryItem[]>();

  private feedErrorSnapshot = '';
  private pnlErrorSnapshot = '';
  private readonly pnlReload$ = new Subject<StrategyPnlQuery>();

  private feed$?: Observable<StrategyFeedResponse | null>;
  private trackRecord$?: Observable<StrategyTrackRecordResponse | null>;
  private zoneRollout$?: Observable<ZoneStrategyRolloutResponse | null>;
  private readonly pnlStreams = new Map<string, Observable<PaperTradeHistoryItem[]>>();

  snapshotFeed(): StrategyFeedResponse | null {
    return this.feedSnapshot;
  }

  snapshotTrackRecord(): StrategyTrackRecordResponse | null {
    return this.trackRecordSnapshot;
  }

  snapshotZoneRollout(): ZoneStrategyRolloutResponse | null {
    return this.zoneRolloutSnapshot;
  }

  snapshotFeedError(): string {
    return this.feedErrorSnapshot;
  }

  snapshotPaperTrades(query: StrategyPnlQuery): PaperTradeHistoryItem[] | null {
    const cached = this.pnlSnapshots.get(strategyPnlQueryKey(query));
    return cached ?? null;
  }

  snapshotPnlError(): string {
    return this.pnlErrorSnapshot;
  }

  watchFeed(): Observable<StrategyFeedResponse | null> {
    if (!this.feed$) {
      this.feed$ = interval(3000).pipe(
        startWith(0),
        exhaustMap(() =>
          this.board.strategyFeed().pipe(
            catchError(() => {
              this.feedErrorSnapshot =
                'Live strategy feed unavailable. Is the backend on port 45211?';
              return of(null);
            }),
          ),
        ),
        map((feed) => {
          if (feed) {
            this.feedSnapshot = feed;
            this.feedErrorSnapshot = '';
          }
          return this.feedSnapshot;
        }),
        shareReplay({ bufferSize: 1, refCount: true }),
      );
    }
    return this.feed$;
  }

  watchTrackRecord(): Observable<StrategyTrackRecordResponse | null> {
    if (!this.trackRecord$) {
      this.trackRecord$ = interval(15000).pipe(
        startWith(0),
        exhaustMap(() => this.board.strategyTrackRecord().pipe(catchError(() => of(null)))),
        map((record) => {
          if (record) {
            this.trackRecordSnapshot = record;
          }
          return this.trackRecordSnapshot;
        }),
        shareReplay({ bufferSize: 1, refCount: true }),
      );
    }
    return this.trackRecord$;
  }

  watchZoneRollout(): Observable<ZoneStrategyRolloutResponse | null> {
    if (!this.zoneRollout$) {
      this.zoneRollout$ = interval(15000).pipe(
        startWith(0),
        exhaustMap(() => this.board.strategyRollout().pipe(catchError(() => of(null)))),
        map((rollout) => {
          if (rollout) {
            this.zoneRolloutSnapshot = rollout;
          }
          return this.zoneRolloutSnapshot;
        }),
        shareReplay({ bufferSize: 1, refCount: true }),
      );
    }
    return this.zoneRollout$;
  }

  watchPaperTrades(query: StrategyPnlQuery): Observable<PaperTradeHistoryItem[]> {
    const key = strategyPnlQueryKey(query);
    let stream = this.pnlStreams.get(key);
    if (!stream) {
      stream = merge(
        interval(8000).pipe(startWith(0)),
        this.pnlReload$.pipe(filter((q) => strategyPnlQueryKey(q) === key)),
      ).pipe(
        exhaustMap(() =>
          this.board.paperTrades(query.range, query.from, query.to).pipe(
            catchError(() => {
              this.pnlErrorSnapshot =
                'Strategy P&L could not load. Check the backend on port 45211 and refresh.';
              return of(null);
            }),
          ),
        ),
        map((history) => {
          if (history) {
            this.pnlErrorSnapshot = '';
          }
          return filterCatalogPaperHistoryTrades(history?.trades ?? []);
        }),
        map((trades) => {
          this.pnlSnapshots.set(key, trades);
          return trades;
        }),
        shareReplay({ bufferSize: 1, refCount: true }),
      );
      this.pnlStreams.set(key, stream);
    }
    return stream;
  }

  reloadPaperTrades(query: StrategyPnlQuery): void {
    this.pnlReload$.next(query);
  }
}
