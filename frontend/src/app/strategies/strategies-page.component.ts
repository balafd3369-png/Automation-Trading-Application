import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, DestroyRef, OnInit, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { catchError, finalize, interval, of, startWith, switchMap } from 'rxjs';
import { BoardService } from '../core/board.service';
import { PaperTradeHistoryItem, TradeHistoryRange } from '../core/board.models';
import {
  buildPaperHistoryStrategyBreakdown,
  filterCatalogPaperHistoryTrades,
  formatPaperHistoryStrategyLabel,
} from '../core/trading-ui.util';
import {
  StrategyFeedResponse,
  StrategyLiveRecommendation,
  StrategyTrackRecordResponse,
  ZoneStrategyRolloutResponse,
} from '../core/strategies.models';

@Component({
  selector: 'app-strategies-page',
  standalone: true,
  imports: [DecimalPipe, DatePipe],
  templateUrl: './strategies-page.component.html',
  styleUrl: './strategies-page.component.scss',
})
export class StrategiesPageComponent implements OnInit {
  private readonly board = inject(BoardService);
  private readonly destroyRef = inject(DestroyRef);

  feed: StrategyFeedResponse | null = null;
  trackRecord: StrategyTrackRecordResponse | null = null;
  zoneRollout: ZoneStrategyRolloutResponse | null = null;
  paperHistoryTrades: PaperTradeHistoryItem[] = [];
  pnlRange: TradeHistoryRange = '1m';
  strategyPnlFilter: string | null = null;
  loading = true;
  pnlLoading = true;
  error = '';
  paperBusy = false;
  paperMessage = '';
  expandedStats = new Set<string>();

  ngOnInit(): void {
    interval(3000)
      .pipe(
        startWith(0),
        switchMap(() =>
          this.board.strategyFeed().pipe(
            catchError(() => {
              this.error = 'Live strategy feed unavailable. Is the backend on port 45211?';
              return of(null);
            }),
          ),
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((feed) => {
        if (feed) {
          this.feed = feed;
          this.error = '';
        }
        this.loading = false;
      });

    interval(15000)
      .pipe(
        startWith(0),
        switchMap(() =>
          this.board.strategyTrackRecord().pipe(catchError(() => of(null))),
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((record) => {
        if (record) {
          this.trackRecord = record;
        }
      });

    this.board
      .strategyRollout()
      .pipe(catchError(() => of(null)), takeUntilDestroyed(this.destroyRef))
      .subscribe((rollout) => {
        this.zoneRollout = rollout;
      });

    interval(8000)
      .pipe(
        startWith(0),
        switchMap(() =>
          this.board.paperTrades(this.pnlRange).pipe(catchError(() => of(null))),
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((history) => {
        this.paperHistoryTrades = filterCatalogPaperHistoryTrades(history?.trades ?? []);
        this.pnlLoading = false;
      });
  }

  setPnlRange(range: TradeHistoryRange): void {
    this.pnlRange = range;
    this.strategyPnlFilter = null;
    this.pnlLoading = true;
    this.board
      .paperTrades(range)
      .pipe(catchError(() => of(null)))
      .subscribe((history) => {
        this.paperHistoryTrades = filterCatalogPaperHistoryTrades(history?.trades ?? []);
        this.pnlLoading = false;
      });
  }

  strategyPnlBreakdown() {
    return buildPaperHistoryStrategyBreakdown(this.paperHistoryTrades);
  }

  strategyPnlSummary(): {
    closedTrades: number;
    winningTrades: number;
    losingTrades: number;
    openTrades: number;
    realizedPnl: number;
  } {
    const closed = this.paperHistoryTrades.filter((t) => t.status === 'CLOSED');
    return {
      closedTrades: closed.length,
      winningTrades: closed.filter((t) => (t.pnl ?? 0) > 0).length,
      losingTrades: closed.filter((t) => (t.pnl ?? 0) < 0).length,
      openTrades: this.paperHistoryTrades.filter((t) => t.status === 'OPEN').length,
      realizedPnl: closed.reduce((sum, t) => sum + (t.pnl ?? 0), 0),
    };
  }

  strategyFilterLabel(): string {
    if (!this.strategyPnlFilter) {
      return '';
    }
    return formatPaperHistoryStrategyLabel(this.strategyPnlFilter);
  }

  toggleStrategyPnlFilter(strategy: string): void {
    this.strategyPnlFilter = this.strategyPnlFilter === strategy ? null : strategy;
  }

  clearStrategyPnlFilter(): void {
    this.strategyPnlFilter = null;
  }

  strategyPnlTradesPreview(): PaperTradeHistoryItem[] {
    let items = [...this.paperHistoryTrades].sort((a, b) => {
      const at = Date.parse(a.closedAt ?? a.openedAt ?? a.createdAt ?? '') || 0;
      const bt = Date.parse(b.closedAt ?? b.openedAt ?? b.createdAt ?? '') || 0;
      return bt - at;
    });
    if (this.strategyPnlFilter) {
      items = items.filter((t) => t.strategy === this.strategyPnlFilter);
    }
    return items.slice(0, 15);
  }

  tradeStrategyLabel(trade: PaperTradeHistoryItem): string {
    return formatPaperHistoryStrategyLabel(trade.strategy ?? '');
  }

  rolloutPhaseLabel(phase: string): string {
    switch (phase) {
      case 'paper_auto':
        return 'Paper auto';
      case 'monitor':
        return 'Monitor';
      case 'live_blocked':
        return 'Live blocked';
      default:
        return phase;
    }
  }

  monthlyStat(strategyId: string) {
    return this.feed?.monthlyStats.find((s) => s.strategyId === strategyId);
  }

  liveRecommendations(): StrategyLiveRecommendation[] {
    return this.feed?.liveNow ?? [];
  }

  monthlyCount(strategyId: string): number {
    return this.feed?.monthlyStats.find((s) => s.strategyId === strategyId)?.tradesThisMonth ?? 0;
  }

  catalogTradesThisMonth() {
    return filterCatalogPaperHistoryTrades(this.trackRecord?.trades ?? []);
  }

  toggleStats(strategyId: string): void {
    if (this.expandedStats.has(strategyId)) {
      this.expandedStats.delete(strategyId);
    } else {
      this.expandedStats.add(strategyId);
    }
  }

  statsExpanded(strategyId: string): boolean {
    return this.expandedStats.has(strategyId);
  }

  liveForStrategy(strategyId: string): StrategyLiveRecommendation[] {
    return this.liveRecommendations().filter((r) => r.strategyId === strategyId);
  }

  takePaperTrade(rec: StrategyLiveRecommendation): void {
    if (this.paperBusy || !rec.canPaperTrade) {
      return;
    }
    this.paperBusy = true;
    this.paperMessage = '';
    this.board
      .takeStrategyPaperTrade(rec.strategyId, rec.symbol)
      .pipe(
        catchError(() => {
          this.paperMessage = 'Could not execute paper trade.';
          return of(null);
        }),
        finalize(() => {
          this.paperBusy = false;
        }),
      )
      .subscribe((snapshot) => {
        if (snapshot) {
          this.paperMessage = `Paper trade submitted for ${rec.symbol} (${rec.strategyName}). Check Trade Details → Paper.`;
        }
      });
  }
}
