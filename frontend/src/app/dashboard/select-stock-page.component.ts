import { DatePipe, DecimalPipe, NgClass, NgTemplateOutlet } from '@angular/common';
import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  OnDestroy,
  OnInit,
  inject,
} from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { Subscription, forkJoin, interval, startWith, switchMap } from 'rxjs';
import { stashMarketSymbolPick } from '../core/app-navigation.util';
import { GAP_PLAYBOOK_ROLLOUT } from '../core/gap-strategy.util';
import { BoardService } from '../core/board.service';
import {
  OpenVsZoneBandKey,
  OpenVsZonesBoardResponse,
  OpenVsZonesRow,
  ZoneAnalyzerPatternDetailResponse,
  GapAnalyzerPlaybookRow,
  GapAnalyzerResponse,
  GapAnalyzerTradeRow,
  ReversalScanResponse,
  ReversalScanRow,
  ZoneStockAnalyzerResponse,
  ZoneStockAnalyzerRow,
} from '../core/user-dashboard.models';
import {
  ZoneAnalyzerAlertSettings,
  ensureNotificationPermission,
  fireAnalyzerNotification,
  loadAnalyzerAlertSettings,
  loadSeenAlertKeys,
  persistSeenAlertKeys,
  saveAnalyzerAlertSettings,
} from '../core/zone-analyzer-alerts.util';

type KindTab = 'stocks' | 'indices' | 'all';
type ViewMode = 'screener' | 'analyzer';

interface ZoneColumn {
  key: OpenVsZoneBandKey;
  title: string;
  hint: string;
  tone: 'up' | 'mid' | 'down';
}

const ZONE_COLUMNS: ZoneColumn[] = [
  { key: 'aboveB2', title: 'Above B2', hint: 'Strongest open · gap-up scenario', tone: 'up' },
  { key: 'aboveB1', title: 'Above B1', hint: 'Bullish open · below B2 resistance', tone: 'up' },
  { key: 'betweenS1B1', title: 'Between S1 & B1', hint: 'Neutral · inside value zone', tone: 'mid' },
  { key: 'betweenS1S2', title: 'Between S1 & S2', hint: 'Sell-band open · watch S2', tone: 'down' },
  { key: 'belowS1', title: 'Below S1', hint: 'Weak open · may target S2', tone: 'down' },
  { key: 'belowS2', title: 'Below S2', hint: 'Deepest open · crash / gap-down', tone: 'down' },
];

@Component({
  selector: 'app-select-stock',
  standalone: true,
  imports: [DecimalPipe, DatePipe, NgClass, NgTemplateOutlet, RouterLink],
  templateUrl: './select-stock-page.component.html',
  styleUrl: './select-stock-page.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SelectStockPageComponent implements OnInit, OnDestroy {
  private readonly board = inject(BoardService);
  private readonly router = inject(Router);
  private readonly cdr = inject(ChangeDetectorRef);

  readonly columns = ZONE_COLUMNS;

  viewMode: ViewMode = 'screener';
  data: OpenVsZonesBoardResponse | null = null;
  analyzer: ZoneStockAnalyzerResponse | null = null;
  reversal: ReversalScanResponse | null = null;
  gapAnalyzer: GapAnalyzerResponse | null = null;
  error = '';
  kind: KindTab = 'all';
  query = '';
  private poll?: Subscription;

  expandedKey: string | null = null;
  detailCache = new Map<string, ZoneAnalyzerPatternDetailResponse>();
  detailLoading = new Set<string>();
  detailError = new Map<string, string>();

  alertSettings: ZoneAnalyzerAlertSettings = loadAnalyzerAlertSettings();
  private seenAlertKeys = loadSeenAlertKeys();
  private prefetchQueue = new Set<string>();
  private prefetchInFlight = 0;
  private readonly prefetchMax = 4;

  ngOnInit(): void {
    this.startPoll();
  }

  ngOnDestroy(): void {
    this.poll?.unsubscribe();
  }

  private startPoll(): void {
    this.poll?.unsubscribe();
    this.poll = interval(12_000)
      .pipe(
        startWith(0),
        switchMap(() =>
          this.viewMode === 'analyzer'
            ? forkJoin({
                analyzer: this.board.zoneAnalyzer(),
                reversal: this.board.reversalScan(),
                gapAnalyzer: this.board.gapAnalyzer(),
              })
            : this.board.openVsZones(),
        ),
      )
      .subscribe({
        next: (payload) => {
          if (this.viewMode === 'analyzer') {
            const bundle = payload as {
              analyzer: ZoneStockAnalyzerResponse;
              reversal: ReversalScanResponse;
              gapAnalyzer: GapAnalyzerResponse;
            };
            this.processAnalyzerAlerts(bundle.analyzer);
            this.analyzer = bundle.analyzer;
            this.reversal = bundle.reversal;
            this.gapAnalyzer = {
              ...bundle.gapAnalyzer,
              playbooks: bundle.gapAnalyzer.playbooks ?? [],
            };
            this.queueWinRatePrefetch(bundle.analyzer);
            this.data = null;
          } else {
            this.data = payload as OpenVsZonesBoardResponse;
            this.analyzer = null;
            this.reversal = null;
            this.gapAnalyzer = null;
          }
          this.error = '';
          this.cdr.markForCheck();
        },
        error: () => {
          this.error =
            this.viewMode === 'analyzer'
              ? 'Analyzer unavailable — restart backend (needs /api/board/zone-analyzer).'
              : 'Could not load open vs zones. Is the backend running?';
          this.cdr.markForCheck();
        },
      });
  }

  private processAnalyzerAlerts(payload: ZoneStockAnalyzerResponse): void {
    if (!this.alertSettings.enabled) {
      return;
    }

    const candidates = [...payload.buy, ...payload.sell].filter((r) => {
      if (r.qualityScore < this.alertSettings.minScore) {
        return false;
      }
      if (this.alertSettings.notifyOnBuyList && r.action !== 'Buy') {
        return false;
      }
      if (this.alertSettings.watchSymbols.length) {
        return this.alertSettings.watchSymbols.some(
          (s) => s.toUpperCase() === r.name.toUpperCase(),
        );
      }
      return true;
    });

    let changed = false;
    for (const row of candidates) {
      const key = `${payload.analyzedAt}|${row.name}|${row.patternId}|${row.qualityScore}`;
      if (this.seenAlertKeys.has(key)) {
        continue;
      }
      this.seenAlertKeys.add(key);
      changed = true;
      fireAnalyzerNotification(
        `${row.name} · ${row.grade} (${row.qualityScore})`,
        `${row.headline} — ${row.entryTiming}, risk ${row.riskClass}`,
      );
    }
    if (changed) {
      persistSeenAlertKeys(this.seenAlertKeys);
    }
  }

  async toggleAlerts(): Promise<void> {
    if (!this.alertSettings.enabled) {
      const ok = await ensureNotificationPermission();
      if (!ok) {
        return;
      }
      this.alertSettings.enabled = true;
    } else {
      this.alertSettings.enabled = false;
    }
    saveAnalyzerAlertSettings(this.alertSettings);
    this.cdr.markForCheck();
  }

  toggleWatchSymbol(name: string, event: Event): void {
    event.stopPropagation();
    const u = name.toUpperCase();
    const set = new Set(this.alertSettings.watchSymbols.map((s) => s.toUpperCase()));
    if (set.has(u)) {
      set.delete(u);
    } else {
      set.add(u);
    }
    this.alertSettings.watchSymbols = [...set];
    saveAnalyzerAlertSettings(this.alertSettings);
    this.cdr.markForCheck();
  }

  isWatched(name: string): boolean {
    const u = name.toUpperCase();
    return this.alertSettings.watchSymbols.some((s) => s.toUpperCase() === u);
  }

  setViewMode(mode: ViewMode): void {
    if (this.viewMode === mode) {
      return;
    }
    this.viewMode = mode;
    this.error = '';
    this.data = null;
    this.analyzer = null;
    this.reversal = null;
    this.gapAnalyzer = null;
    this.expandedKey = null;
    this.startPoll();
    this.cdr.markForCheck();
  }

  setKind(kind: KindTab): void {
    this.kind = kind;
    this.cdr.markForCheck();
  }

  private bandKey(row: OpenVsZonesRow): OpenVsZoneBandKey {
    if (row.openBandKey) {
      return row.openBandKey;
    }
    switch (row.openBand) {
      case 'Above B2':
        return 'aboveB2';
      case 'Above B1':
        return 'aboveB1';
      case 'Below S2':
        return 'belowS2';
      case 'Below S1':
        return 'belowS1';
      case 'Between S1–S2':
        return 'betweenS1S2';
      case 'Between S1–B1':
        return 'betweenS1B1';
      default:
        return 'unknown';
    }
  }

  rowsForColumn(key: OpenVsZoneBandKey): OpenVsZonesRow[] {
    const list = this.screenerList();
    const q = this.query.trim().toUpperCase();
    return list
      .filter((row) => this.bandKey(row) === key)
      .filter((row) => !q || row.name.toUpperCase().includes(q))
      .sort((a, b) => a.name.localeCompare(b.name));
  }

  private screenerList(): OpenVsZonesRow[] {
    if (!this.data) {
      return [];
    }
    if (this.kind === 'indices') {
      return this.data.indices;
    }
    if (this.kind === 'stocks') {
      return this.data.stocks;
    }
    return [...this.data.indices, ...this.data.stocks];
  }

  columnCount(key: OpenVsZoneBandKey): number {
    return this.rowsForColumn(key).length;
  }

  reversalTab: 'fade' | 'gap' | 'warn' | 'failed' = 'fade';

  filterReversal(rows: ReversalScanRow[] | undefined): ReversalScanRow[] {
    if (!rows?.length) {
      return [];
    }
    const q = this.query.trim().toUpperCase();
    return rows
      .filter((row) => !q || row.name.toUpperCase().includes(q))
      .sort((a, b) => b.score - a.score || a.name.localeCompare(b.name));
  }

  reversalRowsForTab(): ReversalScanRow[] {
    if (!this.reversal) {
      return [];
    }
    switch (this.reversalTab) {
      case 'gap':
        return this.filterReversal(this.reversal.gapContinuations);
      case 'warn':
        return this.filterReversal(this.reversal.earlyWarnings);
      case 'failed':
        return this.filterReversal(this.reversal.failedMorningOpens);
      default:
        return this.filterReversal(this.reversal.activeFades);
    }
  }

  setReversalTab(tab: 'fade' | 'gap' | 'warn' | 'failed'): void {
    this.reversalTab = tab;
    this.cdr.markForCheck();
  }

  reversalPatternLabel(id: string): string {
    switch (id) {
      case 'MORNING_FADE':
        return 'Gunshot fade';
      case 'GAP_DOWN_CONTINUE':
        return 'Gap ↓ cont.';
      case 'EARLY_WARNING':
        return 'Early fade';
      case 'FAILED_MORNING':
        return 'Failed open';
      default:
        return id;
    }
  }

  openChartFromReversal(row: ReversalScanRow, event?: Event): void {
    event?.stopPropagation();
    stashMarketSymbolPick(row.name);
    void this.router.navigateByUrl('/market');
  }

  filterGapTrades(): GapAnalyzerTradeRow[] {
    if (!this.gapAnalyzer?.trades.length) {
      return [];
    }
    const q = this.query.trim().toUpperCase();
  const list = this.gapAnalyzer.trades.filter(
      (row) => row.liveActionable || row.qualityScore >= 55,
    );
    return list
      .filter((row) => !q || row.name.toUpperCase().includes(q))
      .sort((a, b) => b.qualityScore - a.qualityScore);
  }

  gapClueShort(id: string): string {
    switch (id) {
      case 'GAP_DOWN_AFTER_FAILED_RALLY':
        return 'Failed rally → gap↓';
      case 'GAP_DOWN_AFTER_DISTRIBUTION':
        return 'Distribution → gap↓';
      case 'GAP_DOWN_AFTER_CLOSE_BELOW_S1':
        return 'Weak close → gap↓';
      case 'GAP_UP_AFTER_CLOSE_ABOVE_B1':
        return 'Strong close → gap↑';
      default:
        return 'Gap clue';
    }
  }

  /** API playbooks, or client fallback when backend not yet redeployed. */
  gapPlaybooksView(): GapAnalyzerPlaybookRow[] {
    const fromApi = this.gapAnalyzer?.playbooks ?? [];
    if (fromApi.length >= 4) {
      return fromApi;
    }
    if (!this.gapAnalyzer) {
      return [];
    }
    const featured = this.gapAnalyzer.featured;
    const meta: Record<
      string,
      { title: string; priorRule: string; gapRule: string; tradeRule: string; action: string }
    > = {
      GAP_DOWN_AFTER_FAILED_RALLY: {
        title: 'Failed rally → gap down',
        priorRule: 'Prior: open ≥ B1, close below open & B1',
        gapRule: 'Gap down ≥ 0.35%',
        tradeRule: 'Short · target S2 · stop above session high',
        action: 'Short',
      },
      GAP_DOWN_AFTER_DISTRIBUTION: {
        title: 'Distribution → gap down',
        priorRule: 'Prior: open ≥ B1, close ≤ S1',
        gapRule: 'Gap down ≥ 0.35%',
        tradeRule: 'Short · target S2',
        action: 'Short',
      },
      GAP_DOWN_AFTER_CLOSE_BELOW_S1: {
        title: 'Weak close → gap down',
        priorRule: 'Prior close below S1',
        gapRule: 'Gap down ≥ 0.35%',
        tradeRule: 'Short · target S2',
        action: 'Short',
      },
      GAP_UP_AFTER_CLOSE_ABOVE_B1: {
        title: 'Strong close → gap up',
        priorRule: 'Prior close ≥ B1',
        gapRule: 'Gap up ≥ 0.35%',
        tradeRule: 'Long · target B2',
        action: 'Long',
      },
    };
    const apiByClue = new Map(fromApi.map((p) => [p.clueId, p]));
    return GAP_PLAYBOOK_ROLLOUT.map((entry) => {
      const existing = apiByClue.get(entry.clueId);
      if (existing) {
        return existing;
      }
      const m = meta[entry.clueId];
      const useFeatured = featured.clueId === entry.clueId;
      return {
        clueId: entry.clueId,
        catalogId: entry.id,
        title: m.title,
        priorRule: m.priorRule,
        gapRule: m.gapRule,
        tradeRule: m.tradeRule,
        action: m.action,
        paperAuto: entry.paperAuto,
        backtestWinRatePercent: useFeatured ? featured.backtestWinRatePercent : 0,
        backtestWins: useFeatured ? featured.backtestWins : 0,
        backtestLosses: useFeatured ? featured.backtestLosses : 0,
        totalGapEvents: useFeatured ? featured.totalGapEvents : 0,
        avgContinuationRatePercent: 0,
        symbolsWithEvents: useFeatured ? featured.symbolsWithEvents : 0,
        qualifies: useFeatured ? featured.qualifies : false,
      };
    });
  }

  gapPlaybookBacktestLabel(pb: GapAnalyzerPlaybookRow): string {
    const trades = pb.backtestWins + pb.backtestLosses;
    const cont =
      pb.avgContinuationRatePercent > 0
        ? `${pb.avgContinuationRatePercent.toFixed(0)}% gap-day cont`
        : '';
    if (trades === 0) {
      return cont ? `${cont} · ${pb.totalGapEvents} hits` : pb.totalGapEvents > 0 ? `${pb.totalGapEvents} hits` : 'Loading…';
    }
    const wr = `${pb.backtestWinRatePercent.toFixed(1)}% sim (${pb.backtestWins}W/${pb.backtestLosses}L)`;
    return cont ? `${wr} · ${cont}` : wr;
  }

  openChartFromGap(row: GapAnalyzerTradeRow): void {
    stashMarketSymbolPick(row.name);
    void this.router.navigateByUrl('/market');
  }

  filterAnalyzer(rows: ZoneStockAnalyzerRow[]): ZoneStockAnalyzerRow[] {
    const q = this.query.trim().toUpperCase();
    return rows
      .filter((row) => this.kind === 'all' || (this.kind === 'indices' ? row.isIndex : !row.isIndex))
      .filter((row) => !q || row.name.toUpperCase().includes(q));
  }

  rowKey(row: ZoneStockAnalyzerRow): string {
    return `${row.name}|${row.patternId}`;
  }

  isExpanded(row: ZoneStockAnalyzerRow): boolean {
    return this.expandedKey === this.rowKey(row);
  }

  toggleExpand(row: ZoneStockAnalyzerRow, event: Event): void {
    event.stopPropagation();
    const key = this.rowKey(row);
    if (this.expandedKey === key) {
      this.expandedKey = null;
      this.cdr.markForCheck();
      return;
    }
    this.expandedKey = key;
    void this.loadDetail(row);
    this.cdr.markForCheck();
  }

  private loadDetail(row: ZoneStockAnalyzerRow, silent = false, fromPrefetch = false): void {
    const key = this.rowKey(row);
    if (this.detailCache.has(key) || this.detailLoading.has(key)) {
      return;
    }
    if (row.patternId === 'ZONE_NEUTRAL') {
      return;
    }
    this.detailLoading.add(key);
    if (!silent) {
      this.detailError.delete(key);
    }
    this.board.zoneAnalyzerDetail(row.name, row.patternId).subscribe({
      next: (detail) => {
        this.detailCache.set(key, detail);
        this.detailLoading.delete(key);
        if (fromPrefetch) {
          this.prefetchInFlight = Math.max(0, this.prefetchInFlight - 1);
          this.drainPrefetchQueue();
        }
        this.cdr.markForCheck();
      },
      error: () => {
        this.detailLoading.delete(key);
        if (fromPrefetch) {
          this.prefetchInFlight = Math.max(0, this.prefetchInFlight - 1);
          this.drainPrefetchQueue();
        }
        if (!silent) {
          this.detailError.set(key, 'Backtest stats unavailable');
        }
        this.cdr.markForCheck();
      },
    });
  }

  private queueWinRatePrefetch(payload: ZoneStockAnalyzerResponse): void {
    const ranked = [...payload.buy, ...payload.sell, ...payload.watch]
      .sort((a, b) => b.qualityScore - a.qualityScore)
      .slice(0, 24);
    for (const row of ranked) {
      const key = this.rowKey(row);
      if (this.detailCache.has(key) || this.detailLoading.has(key) || this.prefetchQueue.has(key)) {
        continue;
      }
      if (row.patternId === 'ZONE_NEUTRAL') {
        continue;
      }
      this.prefetchQueue.add(key);
    }
    this.drainPrefetchQueue();
  }

  private drainPrefetchQueue(): void {
    while (this.prefetchInFlight < this.prefetchMax && this.prefetchQueue.size > 0) {
      const key = this.prefetchQueue.values().next().value as string;
      this.prefetchQueue.delete(key);
      const row = this.findRowByKey(key);
      if (!row) {
        continue;
      }
      this.prefetchInFlight++;
      this.loadDetail(row, true, true);
    }
  }

  private findRowByKey(key: string): ZoneStockAnalyzerRow | null {
    if (!this.analyzer) {
      return null;
    }
    const all = [...this.analyzer.buy, ...this.analyzer.sell, ...this.analyzer.watch];
    return all.find((r) => this.rowKey(r) === key) ?? null;
  }

  winRateDisplay(row: ZoneStockAnalyzerRow): string {
    const d = this.detailFor(row);
    if (d) {
      return `${d.winRatePercent.toFixed(0)}%`;
    }
    if (this.isDetailLoading(row)) {
      return '…';
    }
    return '—';
  }

  patternShort(patternId: string): string {
    switch (patternId) {
      case 'ZONE_B1_BREAK_B2':
        return 'B1→B2';
      case 'ZONE_S1_BREAK_S2':
        return 'S1→S2';
      case 'ZONE_S2_BOUNCE_B1':
        return 'S2→B1';
      case 'STOCK_B1_REJECTION_TO_S2':
        return 'B1 rej→S2';
      case 'ZONE_NEUTRAL':
        return 'Range';
      default:
        return patternId.replace(/^ZONE_/, '').replaceAll('_', ' ');
    }
  }

  scoreTierClass(score: number): string {
    if (score >= 90) {
      return 'score-90';
    }
    if (score >= 80) {
      return 'score-80';
    }
    if (score >= 70) {
      return 'score-70';
    }
    return 'score-low';
  }

  regimeToneClass(regime: string): string {
    const r = (regime || '').toLowerCase();
    if (r.includes('strong bull')) {
      return 'regime-strong-bull';
    }
    if (r.includes('bullish')) {
      return 'regime-bull';
    }
    if (r.includes('strong bear')) {
      return 'regime-strong-bear';
    }
    if (r.includes('bearish')) {
      return 'regime-bear';
    }
    return 'regime-neutral';
  }

  detailFor(row: ZoneStockAnalyzerRow): ZoneAnalyzerPatternDetailResponse | null {
    return this.detailCache.get(this.rowKey(row)) ?? null;
  }

  isDetailLoading(row: ZoneStockAnalyzerRow): boolean {
    return this.detailLoading.has(this.rowKey(row));
  }

  detailErr(row: ZoneStockAnalyzerRow): string {
    return this.detailError.get(this.rowKey(row)) ?? '';
  }

  openChartFromOpenVs(row: OpenVsZonesRow): void {
    stashMarketSymbolPick(row.name);
    void this.router.navigateByUrl('/market');
  }

  openChartFromAnalyzer(row: ZoneStockAnalyzerRow, event?: Event): void {
    event?.stopPropagation();
    stashMarketSymbolPick(row.name);
    void this.router.navigateByUrl('/market');
  }

  moveClass(row: OpenVsZonesRow): string {
    const m = (row.sessionMove ?? '').toLowerCase();
    if (m.startsWith('rising')) {
      return 'move-up';
    }
    if (m.startsWith('falling')) {
      return 'move-down';
    }
    if (m.startsWith('holding')) {
      return 'move-hold';
    }
    return '';
  }

  openDelta(row: OpenVsZonesRow): number {
    return row.last - row.todayOpen;
  }

  confidenceClass(c: string): string {
    return `conf-${(c || 'low').toLowerCase()}`;
  }

  gradeClass(grade: string): string {
    return `grade-${(grade || 'd').replace('+', 'plus').toLowerCase()}`;
  }

  riskClass(risk: string): string {
    return `risk-${(risk || 'normal').toLowerCase()}`;
  }

  timingClass(timing: string): string {
    const t = (timing || '').toLowerCase().replace(/\s+/g, '-');
    return `timing-${t}`;
  }

  onSearchInput(event: Event): void {
    this.query = (event.target as HTMLInputElement).value;
    this.cdr.markForCheck();
  }
}
