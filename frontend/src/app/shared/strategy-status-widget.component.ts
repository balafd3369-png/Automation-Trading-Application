import { DatePipe, DecimalPipe } from '@angular/common';
import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  DestroyRef,
  OnInit,
  inject,
  input,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { catchError, interval, of, startWith, switchMap } from 'rxjs';
import { PaperTradeHistoryItem, SymbolSnapshot } from '../core/board.models';
import { BoardService } from '../core/board.service';
import { BrokerReconciliationDashboard } from '../core/operations.models';
import { StrategiesPageStore } from '../strategies/strategies-page.store';
import { StrategyFeedResponse, ZoneStrategyRolloutResponse } from '../core/strategies.models';

@Component({
  selector: 'app-strategy-status-widget',
  standalone: true,
  imports: [DecimalPipe, DatePipe],
  templateUrl: './strategy-status-widget.component.html',
  styleUrl: './strategy-status-widget.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class StrategyStatusWidgetComponent implements OnInit {
  private readonly store = inject(StrategiesPageStore);
  private readonly board = inject(BoardService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly cdr = inject(ChangeDetectorRef);

  /** @deprecated Dashboard no longer uses board pressure counts. */
  boardCounts = input<unknown>(null);
  indexSnapshots = input<SymbolSnapshot[]>([]);
  variant = input<'desk' | 'compact'>('desk');

  brokerConnected = input(false);
  boardFeed = input<string | null>(null);
  boardUpdatedAt = input<string | null>(null);

  feed: StrategyFeedResponse | null = null;
  rollout: ZoneStrategyRolloutResponse | null = null;
  reconciliation: BrokerReconciliationDashboard | null = null;
  paperTradesToday: PaperTradeHistoryItem[] = [];

  ngOnInit(): void {
    this.feed = this.store.snapshotFeed();
    this.rollout = this.store.snapshotZoneRollout();

    this.store
      .watchFeed()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((feed) => {
        this.feed = feed;
        this.cdr.markForCheck();
      });

    this.store
      .watchZoneRollout()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((rollout) => {
        this.rollout = rollout;
        this.cdr.markForCheck();
      });

    if (this.variant() === 'compact') {
      this.store
        .watchPaperTrades({ range: 'today', from: null, to: null })
        .pipe(takeUntilDestroyed(this.destroyRef))
        .subscribe((trades) => {
          this.paperTradesToday = trades;
          this.cdr.markForCheck();
        });

      interval(30_000)
        .pipe(
          startWith(0),
          switchMap(() => this.board.operationsReconciliation().pipe(catchError(() => of(null)))),
          takeUntilDestroyed(this.destroyRef),
        )
        .subscribe((dashboard) => {
          this.reconciliation = dashboard;
          this.cdr.markForCheck();
        });
    }
  }

  isCompact(): boolean {
    return this.variant() === 'compact';
  }

  runningStrategiesCount(): number {
    const rows = this.rollout?.rows ?? [];
    const codes = new Set(
      rows.filter((r) => r.phase === 'paper_auto').map((r) => r.strategyCode),
    );
    return codes.size;
  }

  todaySignalsCount(): number {
    const key = this.todayIstKey();
    return (this.feed?.liveNow ?? []).filter((rec) => {
      if (!rec.detectedAt) {
        return false;
      }
      return this.istDayKey(rec.detectedAt) === key;
    }).length;
  }

  todayClosedTrades(): PaperTradeHistoryItem[] {
    const key = this.todayIstKey();
    return this.paperTradesToday.filter(
      (t) =>
        t.status === 'CLOSED' &&
        t.closedAt &&
        this.istDayKey(t.closedAt) === key,
    );
  }

  todayPaperTradesCount(): number {
    return this.todayClosedTrades().length;
  }

  todayWinRate(): number | null {
    const closed = this.todayClosedTrades();
    if (!closed.length) {
      return null;
    }
    const wins = closed.filter((t) => (t.pnl ?? 0) > 0).length;
    return (wins / closed.length) * 100;
  }

  lastActivityIso(): string | null {
    const candidates: string[] = [];
    const boardAt = this.boardUpdatedAt();
    if (boardAt) {
      candidates.push(boardAt);
    }
    if (this.feed?.updatedAt) {
      candidates.push(this.feed.updatedAt);
    }
    for (const t of this.todayClosedTrades()) {
      if (t.closedAt) {
        candidates.push(t.closedAt);
      }
    }
    for (const rec of this.feed?.liveNow ?? []) {
      if (rec.detectedAt) {
        candidates.push(rec.detectedAt);
      }
    }
    if (!candidates.length) {
      return null;
    }
    let best = candidates[0];
    for (const c of candidates) {
      if (new Date(c).getTime() > new Date(best).getTime()) {
        best = c;
      }
    }
    return best;
  }

  feedStatusLabel(): string {
    const feed = (this.boardFeed() ?? '').toLowerCase();
    if (!feed) {
      return 'Waiting';
    }
    if (feed.includes('awaiting') || feed.includes('login') || feed.includes('offline')) {
      return 'Waiting';
    }
    if (feed.includes('error') || feed.includes('fail')) {
      return 'Issue';
    }
    return 'Healthy';
  }

  feedStatusOk(): boolean {
    return this.feedStatusLabel() === 'Healthy';
  }

  reconciliationStatusLabel(): string {
    const h = this.reconciliation?.health;
    if (!h) {
      return '—';
    }
    if (h === 'Healthy') {
      return 'Healthy';
    }
    if (h === 'IssuesFound') {
      return 'Review';
    }
    if (h === 'BrokerError') {
      return 'Error';
    }
    return '—';
  }

  reconciliationStatusOk(): boolean {
    return this.reconciliation?.health === 'Healthy';
  }

  platformStatusLabel(): string {
    const broker = this.brokerConnected();
    const feedOk = this.feedStatusOk();
    const reconOk = this.reconciliationStatusOk();
    if (broker && feedOk && (this.reconciliation === null || reconOk)) {
      return 'Ready';
    }
    if (!broker && feedOk) {
      return 'Paper mode';
    }
    return 'Limited';
  }

  private todayIstKey(): string {
    return new Date().toLocaleDateString('en-CA', { timeZone: 'Asia/Kolkata' });
  }

  private istDayKey(iso: string): string {
    return new Date(iso).toLocaleDateString('en-CA', { timeZone: 'Asia/Kolkata' });
  }
}
