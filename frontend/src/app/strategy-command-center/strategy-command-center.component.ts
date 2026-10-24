import { DatePipe, DecimalPipe, TitleCasePipe } from '@angular/common';
import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  Input,
  OnChanges,
  OnDestroy,
  OnInit,
  SimpleChanges,
  DestroyRef,
  inject,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router } from '@angular/router';
import { Subject, Subscription, filter, forkJoin, interval, of, startWith, takeUntil } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { BoardService } from '../core/board.service';
import {
  LiveExecutionSnapshot,
  LiveTrade,
  PaperAgentSnapshot,
  PaperTradeHistoryItem,
  RankedDecision,
} from '../core/board.models';
import {
  buildStrategyLiveTradesUrl,
  buildStrategyPaperTradesUrl,
  parseStrategyCommandCenterFromUrl,
} from '../core/strategy-command-center-navigation.util';
import { StrategyCatalogEntry, StrategyFeedResponse, ZoneStrategyRolloutResponse } from '../core/strategies.models';
import { StrategiesPageStore } from '../strategies/strategies-page.store';
import {
  formatPaperHistoryStrategyLabel,
  normalizeCatalogStrategyId,
  tradeMatchesCatalogStrategy,
} from '../core/trading-ui.util';
import { buildTradeHistoryAnalytics } from '../trade-history-dashboard/trade-history-analytics.util';
import { StrategyTradesSource } from '../core/paper-strategy-navigation.util';
import { StrategyPnlQuery } from '../strategies/strategies-page.store';
import {
  ActivityFeedItem,
  IndexRolloutRow,
  RiskHealth,
  averageHoldMinutes,
  buildActivityFeed,
  closedTradesInEquityWindow,
  computeRiskHealth,
  filterDecisionsForStrategy,
  filterLiveTradesForStrategy,
  filterTradesForStrategy,
  indexRolloutForStrategy,
  strategyPaperOpenRisk,
  zoneAutoEligibleBadge,
} from './strategy-command-center.util';
import { engineStrategyMatchesCatalog, isZoneCatalogStrategy } from '../core/strategy-engine.util';

type EquityWindow = 'today' | '1w' | '1m' | 'all';
type OperationalStatus = 'monitoring' | 'eligible' | 'blocked' | 'waiting' | 'live-open' | 'paper-open';

@Component({
  selector: 'app-strategy-command-center',
  standalone: true,
  imports: [DecimalPipe, DatePipe, TitleCasePipe],
  templateUrl: './strategy-command-center.component.html',
  styleUrl: './strategy-command-center.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class StrategyCommandCenterComponent implements OnInit, OnChanges, OnDestroy {
  private readonly board = inject(BoardService);
  private readonly store = inject(StrategiesPageStore);
  private readonly router = inject(Router);
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly destroyRef = inject(DestroyRef);

  @Input({ required: true }) strategyId!: string;
  @Input() active = true;

  catalog: StrategyCatalogEntry | null = null;
  feed: StrategyFeedResponse | null = null;
  rollout: ZoneStrategyRolloutResponse | null = null;
  paperAgent: PaperAgentSnapshot | null = null;
  liveAgent: LiveExecutionSnapshot | null = null;
  trades: PaperTradeHistoryItem[] = [];
  loading = true;
  loadError = '';

  query: StrategyPnlQuery = { range: '1m', from: null, to: null };
  sourceFilter: StrategyTradesSource = 'paper';
  equityWindow: EquityWindow = '1m';

  private readonly destroy$ = new Subject<void>();
  private pollSub: Subscription | null = null;
  private routerSub: Subscription | null = null;

  ngOnInit(): void {
    this.syncFromRouter(this.router.url);
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
    this.routerSub = this.router.events
      .pipe(filter((e): e is NavigationEnd => e instanceof NavigationEnd))
      .subscribe((e) => this.syncFromRouter(e.urlAfterRedirects));
    this.startPolling();
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['strategyId'] || changes['active']) {
      this.reloadCatalog();
      this.refreshData();
    }
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
    this.pollSub?.unsubscribe();
    this.routerSub?.unsubscribe();
  }

  canonicalId(): string {
    return normalizeCatalogStrategyId(this.strategyId) ?? this.strategyId;
  }

  strategyTitle(): string {
    return this.catalog?.name ?? formatPaperHistoryStrategyLabel(this.canonicalId());
  }

  strategyTypeLabel(): string {
    const item = this.catalog;
    if (!item) {
      return 'Catalog strategy';
    }
    if (item.id.startsWith('zone-')) {
      return 'Zone breakout';
    }
    if (item.id.includes('b1-rejection')) {
      return 'Index rejection';
    }
    if (item.id.includes('10am') || item.id.includes('ten-am')) {
      return 'Session timing';
    }
    return item.tags?.[0] ?? item.timeframe ?? 'Catalog';
  }

  statusBadge(): string {
    const item = this.catalog;
    if (item?.status === 'planned') {
      return 'INACTIVE';
    }
    if (this.liveOpenCount() > 0) {
      return 'ACTIVE';
    }
    if (this.paperOpenCount() > 0 || (this.feed?.liveNow?.some((r) => r.strategyId === this.canonicalId()) ?? false)) {
      return 'ACTIVE';
    }
    return item?.status === 'paper-ready' ? 'ACTIVE' : 'INACTIVE';
  }

  paperAutoBadge(): string | null {
    if (isZoneCatalogStrategy(this.canonicalId())) {
      const zoneRows = (this.rollout?.rows ?? []).filter((r) => this.engineRolloutMatches(r.strategyCode));
      if (zoneRows.some((r) => r.phase === 'paper_auto')) {
        return 'PAPER AUTO';
      }
      if (zoneRows.length > 0 && zoneRows.every((r) => r.phase === 'monitor')) {
        return 'MONITOR ONLY';
      }
    }
    if (this.catalogPaperAuto()) {
      return 'PAPER AUTO';
    }
    return null;
  }

  zoneAutoBadge(): boolean {
    return zoneAutoEligibleBadge(
      this.canonicalId(),
      this.rollout?.rows ?? [],
      this.liveAgent?.zoneAutoLiveArmed === true,
    );
  }

  periodLabel(): string {
    if (this.query.from) {
      return `${this.query.from} → ${this.query.to ?? this.query.from}`;
    }
    switch (this.query.range) {
      case 'today':
        return 'Today';
      case '1w':
        return '1 week';
      default:
        return '1 month';
    }
  }

  strategyTrades(): PaperTradeHistoryItem[] {
    return filterTradesForStrategy(this.trades, this.canonicalId());
  }

  paperTradesOnly(): PaperTradeHistoryItem[] {
    return this.strategyTrades().filter((t) => (t.executionSource ?? 'PAPER') === 'PAPER');
  }

  liveHistoryTrades(): PaperTradeHistoryItem[] {
    return this.strategyTrades().filter((t) => t.executionSource === 'LIVE');
  }

  analytics() {
    return buildTradeHistoryAnalytics(this.strategyTrades());
  }

  equityAnalytics() {
    const windowed = closedTradesInEquityWindow(this.strategyTrades(), this.equityWindow);
    return buildTradeHistoryAnalytics(windowed);
  }

  recentSignals(): RankedDecision[] {
    return filterDecisionsForStrategy(this.paperAgent?.recentDecisions ?? [], this.canonicalId()).slice(0, 8);
  }

  operationalStatus(): OperationalStatus {
    if (this.liveOpenCount() > 0) {
      return 'live-open';
    }
    if (this.paperOpenCount() > 0) {
      return 'paper-open';
    }
    if (this.hasPendingProposalForStrategy()) {
      return 'waiting';
    }
    const rec = this.feed?.liveNow?.find((r) => r.strategyId === this.canonicalId());
    if (rec) {
      return rec.canPaperTrade ? 'eligible' : 'monitoring';
    }
    if (this.catalog?.status === 'planned') {
      return 'blocked';
    }
    return 'monitoring';
  }

  operationalStatusLabel(): string {
    switch (this.operationalStatus()) {
      case 'live-open':
        return 'Live trade open';
      case 'paper-open':
        return 'Paper position open';
      case 'waiting':
        return 'Waiting for confirmation';
      case 'eligible':
        return 'Eligible';
      case 'blocked':
        return 'Blocked';
      default:
        return 'Monitoring';
    }
  }

  lastEvaluationTime(): string | null {
    const top = this.recentSignals()[0];
    return top?.evaluatedAt ?? this.paperAgent?.updatedAt ?? null;
  }

  marketContextLines(): { symbol: string; pressure: string }[] {
    const ctx = this.paperAgent?.marketContext ?? [];
    const symbols = this.contextSymbols();
    return symbols.map((sym) => {
      const row = ctx.find((c) => c.symbol.toUpperCase() === sym.toUpperCase() && c.instrumentType === 'INDEX')
        ?? ctx.find((c) => c.symbol.toUpperCase() === sym.toUpperCase());
      const zone = row?.evidence?.zone?.trim();
      let pressure = zone || row?.evidence?.trend || '—';
      if (pressure === 'BUYING') pressure = 'Buying pressure';
      if (pressure === 'SELLING') pressure = 'Selling pressure';
      if (pressure === 'RANGE BOUND') pressure = 'Range bound';
      return { symbol: sym, pressure };
    });
  }

  rolloutMatrix(): IndexRolloutRow[] {
    return indexRolloutForStrategy(
      this.canonicalId(),
      this.rollout?.rows ?? [],
      this.liveAgent?.zoneAutoLiveArmed === true,
    );
  }

  paperOpenCount(): number {
    const fromAgent = (this.paperAgent?.positions ?? []).filter((p) =>
      tradeMatchesCatalogStrategy({ strategy: p.strategy ?? '' } as PaperTradeHistoryItem, this.canonicalId()),
    ).length;
    if (fromAgent > 0) {
      return fromAgent;
    }
    return this.paperTradesOnly().filter((t) => t.status === 'OPEN').length;
  }

  paperClosedCount(): number {
    return this.paperTradesOnly().filter((t) => t.status === 'CLOSED').length;
  }

  paperPnl(): number {
    return this.analytics().summary.netRealizedPnl;
  }

  liveTrades(): LiveTrade[] {
    return filterLiveTradesForStrategy(this.liveAgent?.trades ?? [], this.canonicalId());
  }

  liveOpenCount(): number {
    return this.liveTrades().filter((t) => t.status === 'OPEN' || t.status === 'OPEN_NRML' || t.status === 'SUBMITTING').length;
  }

  livePnl(): number {
    return this.liveAgent?.brokerPnl?.totalPnl ?? 0;
  }

  liveRealizedPnl(): number {
    return this.liveAgent?.brokerPnl?.realisedPnl ?? 0;
  }

  openRisk(): number {
    return strategyPaperOpenRisk(this.paperAgent, this.canonicalId());
  }

  riskHealth(): RiskHealth {
    const cfg = this.paperAgent?.config;
    return computeRiskHealth(
      this.openRisk(),
      cfg?.maxDailyLoss ?? 0,
      this.paperAgent?.positions?.length ?? 0,
      cfg?.maxOpenPositions ?? 3,
    );
  }

  activityFeed(): ActivityFeedItem[] {
    return buildActivityFeed(this.canonicalId(), this.paperAgent, this.liveAgent, this.trades);
  }

  avgHoldMinutes(): number | null {
    return averageHoldMinutes(this.strategyTrades().filter((t) => t.status === 'CLOSED'));
  }

  setEquityWindow(w: EquityWindow): void {
    this.equityWindow = w;
    this.cdr.markForCheck();
  }

  equityCurveSvg(): { linePath: string; areaPath: string; positive: boolean } | null {
    const curve = this.equityAnalytics().equityCurve;
    if (curve.length < 2) {
      return null;
    }
    const values = curve.map((p) => p.cumulativePnl);
    const min = Math.min(...values, 0);
    const max = Math.max(...values, 0);
    const span = max - min || 1;
    const width = 640;
    const height = 140;
    const step = values.length > 1 ? width / (values.length - 1) : width;
    const points = values.map((value, index) => {
      const x = index * step;
      const y = height - ((value - min) / span) * (height - 10) - 5;
      return `${x.toFixed(1)},${y.toFixed(1)}`;
    });
    const linePath = `M ${points.join(' L ')}`;
    const areaPath = `${linePath} L ${width} ${height} L 0 ${height} Z`;
    return { linePath, areaPath, positive: values[values.length - 1] >= 0 };
  }

  navigateStrategies(): void {
    void this.router.navigateByUrl('/strategies');
  }

  viewAllPaperTrades(): void {
    void this.router.navigateByUrl(buildStrategyPaperTradesUrl(this.canonicalId(), this.query, 'paper'));
  }

  viewLiveTrades(): void {
    void this.router.navigateByUrl(buildStrategyLiveTradesUrl(this.canonicalId(), this.query));
  }

  hasPendingProposalForStrategy(): boolean {
    const pending = this.paperAgent?.pendingTrade;
    if (!pending?.strategy) {
      return false;
    }
    return engineStrategyMatchesCatalog(pending.strategy, this.canonicalId());
  }

  pendingProposal() {
    return this.hasPendingProposalForStrategy() ? this.paperAgent?.pendingTrade ?? null : null;
  }

  signalHeadline(d: RankedDecision): string {
    const dir = d.direction.toUpperCase();
    if (dir.includes('BULL') || dir === 'LONG') {
      return 'BUY Signal';
    }
    if (dir.includes('BEAR') || dir === 'SHORT') {
      return 'SELL Signal';
    }
    return `${d.direction} Signal`;
  }

  signalConfidenceLabel(d: RankedDecision): string {
    return `Score ${d.evidenceScore.toFixed(0)}`;
  }

  proposalMaxRisk(): number {
    const pending = this.pendingProposal();
    if (pending?.maxRisk) {
      return pending.maxRisk;
    }
    return this.openRisk();
  }

  proposalRewardRisk(): number | null {
    const pending = this.pendingProposal();
    if (pending?.rewardRisk) {
      return pending.rewardRisk;
    }
    const accepted = this.recentSignals().find((s) => s.accepted);
    return accepted?.rewardRisk ?? null;
  }

  signalDecisionLabel(d: RankedDecision): string {
    if (!d.accepted) {
      return d.rejections.length ? `Rejected · ${d.rejections[0]}` : 'Rejected by risk';
    }
    const pending = this.paperAgent?.pendingTrade;
    if (
      pending
      && d.underlying === pending.underlying
      && this.paperAgent?.state === 'AWAITING CONFIRMATION'
    ) {
      return 'Awaiting confirm';
    }
    if (this.paperTradesOnly().some((t) => t.status === 'OPEN' && t.underlying === d.underlying)) {
      return 'Executed';
    }
    return 'Accepted';
  }

  private syncFromRouter(url: string): void {
    const parsed = parseStrategyCommandCenterFromUrl(url);
    if (!parsed || parsed.strategyId !== this.strategyId) {
      return;
    }
    this.query = parsed.query;
    this.sourceFilter = parsed.source;
    this.cdr.markForCheck();
  }

  private startPolling(): void {
    this.pollSub = interval(5000)
      .pipe(startWith(0), takeUntil(this.destroy$))
      .subscribe(() => {
        if (this.active) {
          this.refreshData();
        }
      });
  }

  private reloadCatalog(): void {
    this.board
      .strategy(this.canonicalId())
      .pipe(catchError(() => of(null)))
      .subscribe((entry) => {
        this.catalog = entry;
        this.cdr.markForCheck();
      });
  }

  private refreshData(): void {
    this.feed = this.store.snapshotFeed();
    this.rollout = this.store.snapshotZoneRollout();
    const range = this.query.range;
    const from = this.query.from;
    const to = this.query.to;
    forkJoin({
      paper: this.board.paperAgent().pipe(catchError(() => of(null))),
      live: this.board.liveAgent().pipe(catchError(() => of(null))),
      trades: this.board.paperTrades(range, from, to).pipe(catchError(() => of(null))),
    }).subscribe({
      next: ({ paper, live, trades }) => {
        this.paperAgent = paper;
        this.liveAgent = live;
        this.trades = trades?.trades ?? [];
        this.loading = false;
        this.loadError = '';
        this.cdr.markForCheck();
      },
      error: () => {
        this.loading = false;
        this.loadError = 'Could not refresh strategy data.';
        this.cdr.markForCheck();
      },
    });
  }

  private catalogPaperAuto(): boolean {
    const id = this.canonicalId();
    return id === 'bala-10am-high-fade' || id === 'ten-am-low-break-to-day-open' || id === 'stock-b1-rejection-to-s2';
  }

  private engineRolloutMatches(strategyCode: string): boolean {
    const codes = {
      'zone-b1-break-b2': 'ZONE_B1_BREAK_B2',
      'zone-s1-break-s2': 'ZONE_S1_BREAK_S2',
      'zone-s2-bounce-b1': 'ZONE_S2_BOUNCE_B1',
    } as Record<string, string>;
    const id = this.canonicalId();
    return codes[id] === strategyCode;
  }

  private contextSymbols(): string[] {
    if (isZoneCatalogStrategy(this.canonicalId()) || this.canonicalId() === 'stock-b1-rejection-to-s2') {
      return ['NIFTY', 'BANKNIFTY', 'FINNIFTY', 'SENSEX'];
    }
    if (this.canonicalId().includes('10am') || this.canonicalId().includes('ten-am')) {
      return ['NIFTY', 'BANKNIFTY'];
    }
    return ['NIFTY'];
  }
}
