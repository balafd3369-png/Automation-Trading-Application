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

import { Router, RouterLink } from '@angular/router';

import { FormsModule } from '@angular/forms';

import {

  BehaviorSubject,

  catchError,

  finalize,

  of,

  startWith,

  switchMap,

} from 'rxjs';

import { buildStrategyCommandCenterUrl } from '../core/strategy-command-center-navigation.util';
import { parseStrategyCommandCenterFromUrl } from '../core/strategy-command-center-navigation.util';
import { PaperStrategyFocusService } from '../core/paper-strategy-focus.service';
import { AuthService } from '../core/auth.service';
import { BoardService } from '../core/board.service';
import { TradingRefreshService } from '../core/trading-refresh.service';
import { TradeTicketService } from '../core/trade-ticket.service';
import { contextFromRecommendation } from '../core/trade-ticket.util';

import { PaperTradeHistoryItem, TradeHistoryRange } from '../core/board.models';

import {

  buildCatalogPaperHistoryStrategyBreakdown,

  filterCatalogPaperHistoryTrades,

  filterExecutedPaperHistoryTrades,

  formatIstCalendarDayLabel,

  formatPaperHistoryStrategyLabel,

  istTodayCalendarYmd,

  PaperHistoryStrategyBreakdownRow,

  tradeMatchesCatalogStrategy,

} from '../core/trading-ui.util';

import {

  StrategyCatalogEntry,

  StrategyFeedResponse,

  StrategyLiveRecommendation,

  StrategyTrackRecordResponse,

  ZoneStrategyRolloutResponse,

  ZoneStrategyRolloutRow,

} from '../core/strategies.models';

export type StrategyOperationalStatus = 'active' | 'idle' | 'disabled';

export interface StrategySparkline {
  linePath: string;
  areaPath: string;
  positive: boolean;
}

import { StrategiesPageStore, StrategyPnlQuery } from './strategies-page.store';



@Component({

  selector: 'app-strategies-page',

  standalone: true,

  imports: [DecimalPipe, FormsModule, RouterLink],

  templateUrl: './strategies-page.component.html',

  styleUrl: './strategies-page.component.scss',

  changeDetection: ChangeDetectionStrategy.OnPush,

})

export class StrategiesPageComponent implements OnInit, AfterViewInit, OnChanges {

  private readonly board = inject(BoardService);
  readonly auth = inject(AuthService);

  private readonly tradeTickets = inject(TradeTicketService);

  private readonly store = inject(StrategiesPageStore);

  private readonly destroyRef = inject(DestroyRef);

  private readonly host = inject(ElementRef<HTMLElement>);

  private readonly cdr = inject(ChangeDetectorRef);

  private readonly router = inject(Router);

  private readonly paperStrategyFocus = inject(PaperStrategyFocusService);
  private readonly tradingRefresh = inject(TradingRefreshService);



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

      this.paperHistoryTrades = filterExecutedPaperHistoryTrades(cachedPnl);

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

        this.paperHistoryTrades = filterExecutedPaperHistoryTrades(trades);

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

  openStrategyCommandCenter(strategy: string, source: 'paper' | 'live' | 'all' = 'paper'): void {
    const query = this.pnlDateFrom
      ? { range: '1m' as const, from: this.pnlDateFrom, to: this.pnlDateTo || this.pnlDateFrom }
      : { range: this.pnlRange, from: null, to: null };
    void this.router.navigateByUrl(buildStrategyCommandCenterUrl(strategy, query, { source }));
  }

  /** @deprecated Use openStrategyCommandCenter */
  openStrategyInPaperTrading(strategy: string): void {
    this.openStrategyCommandCenter(strategy, 'paper');
  }

  isStrategyCommandCenterOpen(strategy: string): boolean {
    const parsed = parseStrategyCommandCenterFromUrl(this.router.url);
    return parsed?.strategyId === strategy;
  }

  isStrategyFocusedInPaper(strategy: string): boolean {
    return this.isStrategyCommandCenterOpen(strategy);
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



  openTradeTicket(rec: StrategyLiveRecommendation): void {
    this.tradeTickets.open(
      contextFromRecommendation(rec.symbol, rec.direction, rec.entryPrice, 'PAPER'),
    );
    this.cdr.markForCheck();
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

          this.store.reloadPaperTrades(this.pnlPaperTradesQuery());

          this.tradingRefresh.requestPaperRefresh();

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

  readonly liveMonitorIndices = ['NIFTY', 'BANKNIFTY', 'FINNIFTY', 'SENSEX'];

  feedUpdatedLabel(): string {
    const at = this.feed?.updatedAt;
    if (!at) {
      return '—';
    }
    return new Date(at).toLocaleString('en-IN', {
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit',
      hour12: false,
      timeZone: 'Asia/Kolkata',
    }) + ' IST';
  }

  pnlRowWinRate(row: PaperHistoryStrategyBreakdownRow): number {
    if (row.closed <= 0) {
      return 0;
    }
    return (row.wins / row.closed) * 100;
  }

  pnlBreakdownRow(strategyId: string): PaperHistoryStrategyBreakdownRow | undefined {
    return this.pnlBreakdown.find((row) => row.strategy === strategyId);
  }

  bestPerformingStrategy(): PaperHistoryStrategyBreakdownRow | null {
    const ranked = this.pnlBreakdown
      .filter((row) => row.closed > 0)
      .sort((left, right) => right.realizedPnl - left.realizedPnl);
    return ranked[0] ?? null;
  }

  worstPerformingStrategy(): PaperHistoryStrategyBreakdownRow | null {
    const ranked = this.pnlBreakdown
      .filter((row) => row.closed > 0)
      .sort((left, right) => left.realizedPnl - right.realizedPnl);
    return ranked[0] ?? null;
  }

  strategyStatusForCanonicalId(strategyId: string): StrategyOperationalStatus {
    const item = this.feed?.strategies.find((row) => row.id === strategyId);
    if (!item) {
      const row = this.pnlBreakdownRow(strategyId);
      if ((row?.open ?? 0) > 0) {
        return 'active';
      }
      return 'idle';
    }
    return this.strategyOperationalStatus(item);
  }

  strategyOperationalStatus(item: StrategyCatalogEntry): StrategyOperationalStatus {
    if (item.status === 'planned') {
      return 'disabled';
    }
    const autoLabel = this.strategyRowPaperAutoLabel(item.id);
    if (autoLabel === 'Monitor only') {
      return 'disabled';
    }
    const blocked = (this.zoneRollout?.rows ?? []).some(
      (row) => row.strategyLabel === item.name && row.phase === 'live_blocked',
    );
    if (blocked) {
      return 'disabled';
    }
    const row = this.pnlBreakdownRow(item.id);
    if (this.liveForStrategy(item.id).length > 0 || (row?.open ?? 0) > 0) {
      return 'active';
    }
    return 'idle';
  }

  strategyStatusLabel(status: StrategyOperationalStatus): string {
    switch (status) {
      case 'active':
        return 'Active';
      case 'idle':
        return 'Idle';
      default:
        return 'Disabled';
    }
  }

  strategyTypeLabel(item: StrategyCatalogEntry): string {
    if (item.id.startsWith('zone-')) {
      return 'Zone breakout';
    }
    if (item.id.includes('b1-rejection') || item.id.includes('stock-b1')) {
      return 'Index rejection';
    }
    if (item.id.includes('10am') || item.id.includes('ten-am')) {
      return 'Session timing';
    }
    if (item.tags?.length) {
      return item.tags[0];
    }
    return item.timeframe || 'Catalog';
  }

  strategyTradeCountInPeriod(strategyId: string): number {
    const row = this.pnlBreakdownRow(strategyId);
    if (!row) {
      return 0;
    }
    return row.closed + row.open;
  }

  strategySparkline(strategyId: string): StrategySparkline | null {
    const closed = this.paperHistoryTrades
      .filter((trade) => tradeMatchesCatalogStrategy(trade, strategyId) && trade.status === 'CLOSED')
      .sort(
        (left, right) =>
          Date.parse(left.closedAt ?? left.createdAt) - Date.parse(right.closedAt ?? right.createdAt),
      );
    if (closed.length < 2) {
      return null;
    }
    let cumulative = 0;
    const values: number[] = [];
    for (const trade of closed) {
      cumulative += trade.pnl ?? 0;
      values.push(cumulative);
    }
    const min = Math.min(...values, 0);
    const max = Math.max(...values, 0);
    const span = max - min || 1;
    const width = 120;
    const height = 36;
    const step = values.length > 1 ? width / (values.length - 1) : width;
    const points = values.map((value, index) => {
      const x = index * step;
      const y = height - ((value - min) / span) * (height - 4) - 2;
      return `${x.toFixed(1)},${y.toFixed(1)}`;
    });
    const linePath = `M ${points.join(' L ')}`;
    const areaPath = `${linePath} L ${width} ${height} L 0 ${height} Z`;
    return { linePath, areaPath, positive: cumulative >= 0 };
  }

  openStrategyPlaybook(item: StrategyCatalogEntry): void {
    if (this.expandedStats.has(item.id)) {
      this.expandedStats.delete(item.id);
    } else {
      this.expandedStats.add(item.id);
    }
    this.cdr.markForCheck();
  }

}


