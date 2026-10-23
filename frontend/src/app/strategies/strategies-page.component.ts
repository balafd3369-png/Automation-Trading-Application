import { DecimalPipe } from '@angular/common';

import {

  AfterViewInit,

  ChangeDetectionStrategy,

  ChangeDetectorRef,

  Component,

  DestroyRef,

  ElementRef,

  Input,

  OnChanges,

  OnInit,

  SimpleChanges,

  inject,

} from '@angular/core';

import { takeUntilDestroyed } from '@angular/core/rxjs-interop';

import { Router } from '@angular/router';

import { FormsModule } from '@angular/forms';

import {

  BehaviorSubject,

  catchError,

  finalize,

  of,

  startWith,

  switchMap,

} from 'rxjs';

import { PaperStrategyFocusService } from '../core/paper-strategy-focus.service';
import { BoardService } from '../core/board.service';

import { PaperTradeHistoryItem, TradeHistoryRange } from '../core/board.models';

import {

  buildCatalogPaperHistoryStrategyBreakdown,

  filterCatalogPaperHistoryTrades,

  formatIstCalendarDayLabel,

  formatPaperHistoryStrategyLabel,

  istTodayCalendarYmd,

  PaperHistoryStrategyBreakdownRow,

} from '../core/trading-ui.util';

import {

  StrategyFeedResponse,

  StrategyLiveRecommendation,

  StrategyTrackRecordResponse,

  ZoneStrategyRolloutResponse,

  ZoneStrategyRolloutRow,

} from '../core/strategies.models';

import { StrategiesPageStore, StrategyPnlQuery } from './strategies-page.store';



@Component({

  selector: 'app-strategies-page',

  standalone: true,

  imports: [DecimalPipe, FormsModule],

  templateUrl: './strategies-page.component.html',

  styleUrl: './strategies-page.component.scss',

  changeDetection: ChangeDetectionStrategy.OnPush,

})

export class StrategiesPageComponent implements OnInit, AfterViewInit, OnChanges {

  private readonly board = inject(BoardService);

  private readonly store = inject(StrategiesPageStore);

  private readonly destroyRef = inject(DestroyRef);

  private readonly host = inject(ElementRef<HTMLElement>);

  private readonly cdr = inject(ChangeDetectorRef);

  private readonly router = inject(Router);

  private readonly paperStrategyFocus = inject(PaperStrategyFocusService);



  @Input() active = true;



  feed: StrategyFeedResponse | null = null;

  trackRecord: StrategyTrackRecordResponse | null = null;

  zoneRollout: ZoneStrategyRolloutResponse | null = null;

  paperHistoryTrades: PaperTradeHistoryItem[] = [];

  pnlRange: TradeHistoryRange = '1m';

  pnlDateFrom: string | null = null;

  pnlDateTo: string | null = null;

  pnlCustomDateOpen = false;

  loading = true;

  pnlLoading = true;

  pnlLoadError = '';

  error = '';

  paperBusy = false;

  paperMessage = '';

  expandedStats = new Set<string>();



  pnlBreakdown: PaperHistoryStrategyBreakdownRow[] = [];

  pnlSummary = {

    closedTrades: 0,

    winningTrades: 0,

    losingTrades: 0,

    openTrades: 0,

    realizedPnl: 0,

  };

  zonePaperAutoRowsCache: ZoneStrategyRolloutRow[] = [];

  zoneMonitorRowsCache: ZoneStrategyRolloutRow[] = [];

  liveRecommendationsCache: StrategyLiveRecommendation[] = [];



  private readonly pnlQuery$ = new BehaviorSubject<StrategyPnlQuery>({

    range: '1m',

    from: null,

    to: null,

  });



  /** Non-zone catalog strategies that paper-auto-admit when the agent is in auto mode. */

  readonly catalogPaperAutoEntries: ReadonlyArray<{

    id: string;

    label: string;

    universe: string;

  }> = [

    {

      id: 'bala-10am-high-fade',

      label: 'Ten AM high fade → day low',

      universe: 'Indices, index options/futures, Nifty 50 stocks & stock options',

    },

    {

      id: 'ten-am-low-break-to-day-open',

      label: 'Ten AM low break → day open',

      universe: 'Indices, index options/futures, Nifty 50 stocks & stock options',

    },

    {

      id: 'stock-b1-rejection-to-s2',

      label: 'Index B1 rejection → S2',

      universe: 'NIFTY, BANKNIFTY, FINNIFTY, SENSEX',

    },

  ];



  private static readonly ZONE_CANONICAL_TO_ROLLOUT_CODE: Record<string, string> = {

    'zone-b1-break-b2': 'ZONE_B1_BREAK_B2',

    'zone-s1-break-s2': 'ZONE_S1_BREAK_S2',

    'zone-s2-bounce-b1': 'ZONE_S2_BOUNCE_B1',

  };



  ngOnChanges(changes: SimpleChanges): void {

    if (changes['active']?.currentValue === true) {

      queueMicrotask(() => this.scrollPageToTop());

    }

  }



  ngAfterViewInit(): void {

    if (this.active) {

      this.scrollPageToTop();

    }

  }



  private scrollPageToTop(): void {

    const run = (): void => {

      const page = this.host.nativeElement.querySelector('.strategies-page') as HTMLElement | null;

      if (page) {

        page.scrollTop = 0;

      }

    };

    run();

    requestAnimationFrame(() => {

      run();

      requestAnimationFrame(run);

    });

  }



  ngOnInit(): void {

    this.store.ensurePolling();



    this.feed = this.store.snapshotFeed();

    this.trackRecord = this.store.snapshotTrackRecord();

    this.zoneRollout = this.store.snapshotZoneRollout();

    this.error = this.store.snapshotFeedError();

    this.loading = !this.feed;

    this.refreshRolloutRows();

    this.refreshLiveRecommendations();



    const initialPnl = this.pnlPaperTradesQuery();

    const cachedPnl = this.store.snapshotPaperTrades(initialPnl);

    if (cachedPnl) {

      this.paperHistoryTrades = cachedPnl;

      this.pnlLoading = false;

      this.refreshPnlDerived();

    }



    this.store

      .watchFeed()

      .pipe(takeUntilDestroyed(this.destroyRef))

      .subscribe((feed) => {

        this.feed = feed;

        this.error = feed ? '' : this.store.snapshotFeedError();

        this.loading = false;

        this.refreshLiveRecommendations();

        this.cdr.markForCheck();

      });



    this.store

      .watchTrackRecord()

      .pipe(takeUntilDestroyed(this.destroyRef))

      .subscribe((record) => {

        this.trackRecord = record;

        this.cdr.markForCheck();

      });



    this.store

      .watchZoneRollout()

      .pipe(takeUntilDestroyed(this.destroyRef))

      .subscribe((rollout) => {

        this.zoneRollout = rollout;

        this.refreshRolloutRows();

        if (this.active) {

          this.scrollPageToTop();

        }

        this.cdr.markForCheck();

      });



    this.pnlQuery$

      .pipe(

        switchMap((query) => {

          if (this.pnlAwaitingCustomDateForQuery(query)) {

            return of([] as PaperTradeHistoryItem[]);

          }

          const cached = this.store.snapshotPaperTrades(query);

          return this.store.watchPaperTrades(query).pipe(startWith(cached ?? []));

        }),

        takeUntilDestroyed(this.destroyRef),

      )

      .subscribe((trades) => {

        this.paperHistoryTrades = trades;

        this.pnlLoading = false;

        this.pnlLoadError = this.store.snapshotPnlError();

        this.refreshPnlDerived();

        this.cdr.markForCheck();

      });

  }



  setPnlRange(range: TradeHistoryRange): void {

    this.pnlRange = range;

    this.pnlCustomDateOpen = false;

    this.pnlDateFrom = null;

    this.pnlDateTo = null;

    this.pushPnlQuery();

  }



  pnlDateFilterActive(): boolean {

    return !!this.pnlDateFrom;

  }



  pnlTodayIstYmd(): string {

    return istTodayCalendarYmd();

  }



  openPnlCustomDate(): void {

    this.pnlCustomDateOpen = true;

    if (!this.pnlDateFrom) {

      this.paperHistoryTrades = [];

      this.pnlLoading = false;

      this.refreshPnlDerived();

      this.pushPnlQuery();

    }

    queueMicrotask(() => this.focusPnlFromDatePicker());

  }



  focusPnlFromDatePicker(): void {

    const input = document.querySelector<HTMLInputElement>('.strategy-pnl-date-input[data-pnl-from="1"]');

    if (!input) {

      return;

    }

    input.focus();

    try {

      input.showPicker?.();

    } catch {

      /* showPicker may require a user gesture */

    }

  }



  pnlDateFilterLabel(): string {

    const from = this.pnlDateFrom;

    if (!from) {

      return '';

    }

    const to = this.pnlDateTo || from;

    if (to === from) {

      return formatIstCalendarDayLabel(from);

    }

    return `${formatIstCalendarDayLabel(from)} – ${formatIstCalendarDayLabel(to)}`;

  }



  onPnlDateFromChange(value: string): void {

    this.pnlCustomDateOpen = true;

    this.pnlDateFrom = value?.trim() || null;

    if (!this.pnlDateFrom) {

      this.pnlDateTo = null;

    } else if (this.pnlDateTo && this.pnlDateTo < this.pnlDateFrom) {

      this.pnlDateTo = this.pnlDateFrom;

    }

    this.pushPnlQuery(true);

  }



  onPnlDateToChange(value: string): void {

    this.pnlCustomDateOpen = true;

    this.pnlDateTo = value?.trim() || null;

    if (this.pnlDateFrom && this.pnlDateTo && this.pnlDateTo < this.pnlDateFrom) {

      this.pnlDateFrom = this.pnlDateTo;

    }

    if (this.pnlDateFrom) {

      this.pushPnlQuery(true);

    }

  }



  clearPnlDateFilter(): void {

    this.pnlDateFrom = null;

    this.pnlDateTo = null;

    this.pnlCustomDateOpen = false;

    this.pushPnlQuery(true);

  }



  pnlAwaitingCustomDate(): boolean {

    return this.pnlCustomDateOpen && !this.pnlDateFrom;

  }



  private pnlAwaitingCustomDateForQuery(_query: StrategyPnlQuery): boolean {

    return this.pnlAwaitingCustomDate();

  }



  private pnlPaperTradesQuery(): StrategyPnlQuery {

    if (this.pnlDateFrom) {

      return {

        range: '1m',

        from: this.pnlDateFrom,

        to: this.pnlDateTo || this.pnlDateFrom,

      };

    }

    return { range: this.pnlRange, from: null, to: null };

  }



  private pushPnlQuery(reload = false): void {

    const query = this.pnlPaperTradesQuery();

    if (this.pnlAwaitingCustomDate()) {

      this.pnlLoading = false;

      this.pnlQuery$.next(query);

      return;

    }

    if (!this.store.snapshotPaperTrades(query)) {

      this.pnlLoading = true;

      this.cdr.markForCheck();

    }

    if (reload) {

      this.store.reloadPaperTrades(query);

    }

    this.pnlQuery$.next(query);

  }



  pnlPeriodLabel(): string {
    if (this.pnlDateFilterActive()) {
      return this.pnlDateFilterLabel();
    }
    switch (this.pnlRange) {
      case 'today':
        return 'Today (IST)';
      case '1w':
        return 'Last 7 days (IST)';
      case '1m':
        return 'Last 1 month';
      default:
        return 'Last 1 month';
    }
  }

  pnlShowEmptyHint(): boolean {
    return !this.pnlLoading && !this.pnlAwaitingCustomDate() && this.paperHistoryTrades.length === 0;
  }

  pnlEmptyHint(): string {
    if (this.pnlLoadError) {
      return this.pnlLoadError;
    }
    if (this.pnlRange === 'today') {
      return 'No catalog paper trades for today (IST). Older closes count under 1 Week or 1 Month — the table below still lists every strategy with zero until there is activity.';
    }
    return 'No catalog paper trades in this period. Start the paper agent (Trade Details → Agent) or widen the date range.';
  }

  showPnlQuickMonth(): void {
    this.setPnlRange('1m');
  }

  openStrategyInPaperTrading(strategy: string): void {
    this.paperStrategyFocus.open(strategy, {
      range: this.pnlRange,
      from: this.pnlDateFrom,
      to: this.pnlDateTo,
    });
    void this.router.navigateByUrl('/trades/paper');
  }

  isStrategyFocusedInPaper(strategy: string): boolean {
    return this.paperStrategyFocus.snapshot()?.strategyId === strategy;
  }

  tradeStrategyLabel(trade: PaperTradeHistoryItem): string {

    return formatPaperHistoryStrategyLabel(trade.strategy ?? '');

  }



  autoTradedSummaryHint(): string {

    const zone = this.zonePaperAutoRowsCache.length;

    const catalog = this.catalogPaperAutoEntries.length;

    return `${zone} zone index setup${zone === 1 ? '' : 's'} + ${catalog} catalog strategies on paper auto`;

  }



  strategyRowPaperAutoLabel(canonicalStrategyId: string): string | null {

    const catalogEntry = this.catalogPaperAutoEntries.find((e) => e.id === canonicalStrategyId);

    if (catalogEntry) {

      return `Paper auto · ${catalogEntry.universe}`;

    }

    const code = StrategiesPageComponent.ZONE_CANONICAL_TO_ROLLOUT_CODE[canonicalStrategyId];

    if (!code) {

      return null;

    }

    const autos = this.zonePaperAutoRowsCache.filter((r) => r.strategyCode === code);

    if (!autos.length) {

      return 'Monitor only';

    }

    return `Paper auto · ${autos.map((r) => r.index).join(', ')}`;

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

    this.cdr.markForCheck();

  }



  statsExpanded(strategyId: string): boolean {

    return this.expandedStats.has(strategyId);

  }



  liveForStrategy(strategyId: string): StrategyLiveRecommendation[] {

    return this.liveRecommendationsCache.filter((r) => r.strategyId === strategyId);

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

          this.cdr.markForCheck();

        }),

      )

      .subscribe((snapshot) => {

        if (snapshot) {

          this.paperMessage = `Paper trade submitted for ${rec.symbol} (${rec.strategyName}). Check Trade Details → Paper.`;

          this.cdr.markForCheck();

        }

      });

  }



  private refreshPnlDerived(): void {

    this.pnlBreakdown = buildCatalogPaperHistoryStrategyBreakdown(this.paperHistoryTrades);

    const closed = this.paperHistoryTrades.filter((t) => t.status === 'CLOSED');

    this.pnlSummary = {

      closedTrades: closed.length,

      winningTrades: closed.filter((t) => (t.pnl ?? 0) > 0).length,

      losingTrades: closed.filter((t) => (t.pnl ?? 0) < 0).length,

      openTrades: this.paperHistoryTrades.filter((t) => t.status === 'OPEN').length,

      realizedPnl: closed.reduce((sum, t) => sum + (t.pnl ?? 0), 0),

    };

  }



  private refreshRolloutRows(): void {

    this.zonePaperAutoRowsCache = (this.zoneRollout?.rows ?? []).filter((row) => row.phase === 'paper_auto');

    this.zoneMonitorRowsCache = (this.zoneRollout?.rows ?? []).filter((row) => row.phase === 'monitor');

  }



  private refreshLiveRecommendations(): void {

    this.liveRecommendationsCache = this.feed?.liveNow ?? [];

  }

}


