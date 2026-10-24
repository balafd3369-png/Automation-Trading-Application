import { DatePipe, DecimalPipe } from '@angular/common';
import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  Input,
  OnChanges,
  SimpleChanges,
  inject,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { PaperTradeHistoryItem, TradeExecutionSource } from '../core/board.models';
import { realizedPnlTotal } from '../core/live-trades-history.util';
import {
  filterExecutedPaperHistoryTrades,
  formatPaperHistoryStrategyLabel,
  paperTradeCapitalUsed,
  paperTradeClosedAt,
  paperTradeDirectionShort,
  paperTradeExecutedAt,
  paperTradeEntryPrice,
  paperTradeExitPrice,
  paperTradeQuantityLabel,
  paperTradeQuantityTitle,
  tradeMatchesCatalogStrategy,
} from '../core/trading-ui.util';
import {
  TradeHistoryAnalytics,
  buildTradeHistoryAnalytics,
  classifyPaperOutcome,
  exportTradesCsv,
  tradeNetPnl,
  tradeRoiPct,
} from './trade-history-analytics.util';

export type TradeHistorySourceFilter = 'all' | 'paper' | 'live';

type SortKey =
  | 'source'
  | 'underlying'
  | 'direction'
  | 'entry'
  | 'exit'
  | 'entryPrice'
  | 'exitPrice'
  | 'qty'
  | 'capital'
  | 'gross'
  | 'net'
  | 'roi'
  | 'strategy'
  | 'status';

type ColumnKey = SortKey | 'charges' | 'outcome' | 'details';

type FilterSummaryChip = { label: string; value: string };

@Component({
  selector: 'app-trade-history-dashboard',
  standalone: true,
  imports: [DecimalPipe, DatePipe, FormsModule],
  templateUrl: './trade-history-dashboard.component.html',
  styleUrl: './trade-history-dashboard.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TradeHistoryDashboardComponent implements OnChanges {
  private readonly cdr = inject(ChangeDetectorRef);

  @Input({ required: true }) trades: PaperTradeHistoryItem[] = [];
  @Input() loading = false;
  @Input() periodLabel = '';
  @Input() periodKey = '';
  @Input() initialStrategyCanonicalId: string | null = null;
  @Input() initialSourceFilter: TradeHistorySourceFilter = 'all';

  analytics: TradeHistoryAnalytics | null = null;
  widgetAnimKey = 0;
  widgetTransitioning = false;
  private widgetTransitionTimer?: ReturnType<typeof setTimeout>;

  searchQuery = '';
  strategyFilter = '';
  symbolFilter = '';
  directionFilter: 'all' | 'bull' | 'bear' = 'all';
  statusFilter = '';
  pnlMin: number | null = null;
  pnlMax: number | null = null;
  capitalMin: number | null = null;
  capitalMax: number | null = null;
  pnlChip: 'all' | 'positive' | 'negative' = 'all';
  sourceFilter: TradeHistorySourceFilter = 'all';
  strategyCanonicalFilter = '';

  sortKey: SortKey = 'exit';
  sortDir: 'asc' | 'desc' = 'desc';
  page = 1;
  readonly pageSize = 12;
  expandedId: string | null = null;
  filtersOpen = false;
  columnsOpen = false;

  readonly columnDefs: { key: ColumnKey; label: string; default: boolean }[] = [
    { key: 'source', label: 'Source', default: true },
    { key: 'underlying', label: 'Symbol', default: true },
    { key: 'direction', label: 'Direction', default: true },
    { key: 'entry', label: 'Entry time', default: true },
    { key: 'exit', label: 'Exit time', default: true },
    { key: 'entryPrice', label: 'Entry ₹', default: true },
    { key: 'exitPrice', label: 'Exit ₹', default: false },
    { key: 'qty', label: 'Qty', default: true },
    { key: 'capital', label: 'Capital', default: true },
    { key: 'gross', label: 'Gross P&L', default: false },
    { key: 'charges', label: 'Charges', default: false },
    { key: 'net', label: 'Net P&L', default: true },
    { key: 'roi', label: 'ROI %', default: true },
    { key: 'strategy', label: 'Strategy', default: true },
    { key: 'status', label: 'Status', default: true },
    { key: 'outcome', label: 'Outcome', default: false },
    { key: 'details', label: 'Details', default: true },
  ];

  visibleColumns = new Set<ColumnKey>(this.columnDefs.filter((c) => c.default).map((c) => c.key));

  filteredTrades: PaperTradeHistoryItem[] = [];
  pageTrades: PaperTradeHistoryItem[] = [];
  pageCount = 1;
  filteredSummary = {
    total: 0,
    winners: 0,
    losers: 0,
    winRatePct: null as number | null,
    netPnl: 0,
  };
  strategyOptions: string[] = [];
  symbolOptions: string[] = [];
  statusOptions: string[] = [];

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['initialStrategyCanonicalId']) {
      this.strategyCanonicalFilter = (this.initialStrategyCanonicalId ?? '').trim();
    }
    if (changes['initialSourceFilter']) {
      this.sourceFilter = this.initialSourceFilter;
    }
    if (changes['trades']) {
      const visible = this.visibleTrades();
      this.analytics = buildTradeHistoryAnalytics(visible);
      this.strategyOptions = [...new Set(visible.map((t) => t.strategy).filter(Boolean))].sort();
      this.symbolOptions = [...new Set(visible.map((t) => t.underlying).filter(Boolean))].sort();
      this.statusOptions = [...new Set(visible.map((t) => t.status).filter(Boolean))].sort();
      this.page = 1;
      this.rebuildList();
      this.bumpWidgetTransition();
    }
    if (changes['periodKey'] && !changes['periodKey'].firstChange) {
      this.bumpWidgetTransition();
    }
    if (changes['initialStrategyCanonicalId'] || changes['initialSourceFilter']) {
      this.page = 1;
      this.rebuildList();
    }
    if (changes['loading']) {
      this.cdr.markForCheck();
    }
  }

  private bumpWidgetTransition(): void {
    this.widgetAnimKey += 1;
    this.widgetTransitioning = true;
    if (this.widgetTransitionTimer) {
      clearTimeout(this.widgetTransitionTimer);
    }
    this.widgetTransitionTimer = setTimeout(() => {
      this.widgetTransitioning = false;
      this.cdr.markForCheck();
    }, 480);
    this.cdr.markForCheck();
  }

  /** Paper proposals that never executed (expired, rejected, etc.) stay out of the default ledger. */
  private visibleTrades(): PaperTradeHistoryItem[] {
    const paper = filterExecutedPaperHistoryTrades(this.trades.filter((t) => (t.executionSource ?? 'PAPER') === 'PAPER'));
    const live = this.trades.filter((t) => t.executionSource === 'LIVE');
    return [...paper, ...live];
  }

  rebuildList(): void {
    let items = [...this.visibleTrades()];
    if (this.sourceFilter === 'paper') {
      items = items.filter((t) => (t.executionSource ?? 'PAPER') === 'PAPER');
    } else if (this.sourceFilter === 'live') {
      items = items.filter((t) => t.executionSource === 'LIVE');
    }
    if (this.strategyCanonicalFilter) {
      items = items.filter((t) => tradeMatchesCatalogStrategy(t, this.strategyCanonicalFilter));
    }
    const q = this.searchQuery.trim().toLowerCase();
    if (q) {
      items = items.filter((t) => {
        const hay = `${t.underlying} ${t.strategy} ${t.exitReason ?? ''} ${t.status}`.toLowerCase();
        return hay.includes(q);
      });
    }
    if (this.strategyFilter) {
      items = items.filter((t) => t.strategy === this.strategyFilter);
    }
    if (this.symbolFilter) {
      items = items.filter((t) => t.underlying === this.symbolFilter);
    }
    if (this.directionFilter === 'bull') {
      items = items.filter((t) => t.direction === 'BULLISH' || t.direction === 'LONG');
    } else if (this.directionFilter === 'bear') {
      items = items.filter((t) => t.direction === 'BEARISH' || t.direction === 'SHORT');
    }
    if (this.statusFilter) {
      items = items.filter((t) => t.status === this.statusFilter);
    }
    if (this.pnlChip === 'positive') {
      items = items.filter((t) => t.status === 'CLOSED' && (t.pnl ?? 0) > 0);
    } else if (this.pnlChip === 'negative') {
      items = items.filter((t) => t.status === 'CLOSED' && (t.pnl ?? 0) < 0);
    }
    if (this.pnlMin != null) {
      items = items.filter((t) => (t.pnl ?? -Infinity) >= this.pnlMin!);
    }
    if (this.pnlMax != null) {
      items = items.filter((t) => (t.pnl ?? Infinity) <= this.pnlMax!);
    }
    if (this.capitalMin != null) {
      items = items.filter((t) => paperTradeCapitalUsed(t) >= this.capitalMin!);
    }
    if (this.capitalMax != null) {
      items = items.filter((t) => paperTradeCapitalUsed(t) <= this.capitalMax!);
    }

    items.sort((a, b) => this.compareTrades(a, b));
    this.filteredTrades = items;
    const closed = items.filter((t) => t.status === 'CLOSED' && t.pnl != null);
    const winners = closed.filter((t) => (t.pnl ?? 0) > 0).length;
    const losers = closed.filter((t) => (t.pnl ?? 0) < 0).length;
    this.filteredSummary = {
      total: items.length,
      winners,
      losers,
      winRatePct: closed.length ? (winners / closed.length) * 100 : null,
      netPnl: closed.reduce((sum, t) => sum + (t.pnl ?? 0), 0),
    };
    this.pageCount = Math.max(1, Math.ceil(items.length / this.pageSize));
    this.page = Math.min(this.page, this.pageCount);
    const start = (this.page - 1) * this.pageSize;
    this.pageTrades = items.slice(start, start + this.pageSize);
    this.cdr.markForCheck();
  }

  compareTrades(a: PaperTradeHistoryItem, b: PaperTradeHistoryItem): number {
    const dir = this.sortDir === 'asc' ? 1 : -1;
    const num = (v: number | null | undefined) => (v == null || !Number.isFinite(v) ? null : v);
    const str = (v: string | null | undefined) => v ?? '';
    let av: number | string = 0;
    let bv: number | string = 0;
    switch (this.sortKey) {
      case 'source':
        av = this.tradeExecutionSource(a);
        bv = this.tradeExecutionSource(b);
        break;
      case 'underlying':
        av = str(a.underlying);
        bv = str(b.underlying);
        break;
      case 'direction':
        av = str(a.direction);
        bv = str(b.direction);
        break;
      case 'entry':
        av = Date.parse(paperTradeExecutedAt(a) ?? '') || 0;
        bv = Date.parse(paperTradeExecutedAt(b) ?? '') || 0;
        break;
      case 'exit':
        av = Date.parse(paperTradeClosedAt(a) ?? '') || 0;
        bv = Date.parse(paperTradeClosedAt(b) ?? '') || 0;
        break;
      case 'entryPrice':
        av = num(paperTradeEntryPrice(a)) ?? -1;
        bv = num(paperTradeEntryPrice(b)) ?? -1;
        break;
      case 'exitPrice':
        av = num(paperTradeExitPrice(a)) ?? -1;
        bv = num(paperTradeExitPrice(b)) ?? -1;
        break;
      case 'qty':
        av = num(a.quantity) ?? -1;
        bv = num(b.quantity) ?? -1;
        break;
      case 'capital':
        av = paperTradeCapitalUsed(a);
        bv = paperTradeCapitalUsed(b);
        break;
      case 'gross':
      case 'net':
        av = num(a.pnl) ?? -Infinity;
        bv = num(b.pnl) ?? -Infinity;
        break;
      case 'roi':
        av = num(tradeRoiPct(a)) ?? -Infinity;
        bv = num(tradeRoiPct(b)) ?? -Infinity;
        break;
      case 'strategy':
        av = str(a.strategy);
        bv = str(b.strategy);
        break;
      case 'status':
        av = str(a.status);
        bv = str(b.status);
        break;
    }
    if (typeof av === 'string' && typeof bv === 'string') {
      return av.localeCompare(bv) * dir;
    }
    return ((av as number) - (bv as number)) * dir;
  }

  setSort(key: SortKey): void {
    if (this.sortKey === key) {
      this.sortDir = this.sortDir === 'asc' ? 'desc' : 'asc';
    } else {
      this.sortKey = key;
      this.sortDir = key === 'underlying' || key === 'strategy' ? 'asc' : 'desc';
    }
    this.rebuildList();
  }

  resetFilters(): void {
    this.searchQuery = '';
    this.strategyFilter = '';
    this.symbolFilter = '';
    this.directionFilter = 'all';
    this.statusFilter = '';
    this.pnlMin = null;
    this.pnlMax = null;
    this.capitalMin = null;
    this.capitalMax = null;
    this.pnlChip = 'all';
    this.sourceFilter = this.initialSourceFilter;
    this.strategyCanonicalFilter = (this.initialStrategyCanonicalId ?? '').trim();
    this.rebuildList();
  }

  setSourceFilter(filter: TradeHistorySourceFilter): void {
    this.sourceFilter = filter;
    this.page = 1;
    this.rebuildList();
  }

  tradeExecutionSource(trade: PaperTradeHistoryItem): TradeExecutionSource {
    return trade.executionSource ?? 'PAPER';
  }

  sourcePnlBreakdown(): { paper: number; live: number; combined: number } {
    const paperTrades = this.trades.filter((t) => (t.executionSource ?? 'PAPER') === 'PAPER');
    const liveTrades = this.trades.filter((t) => t.executionSource === 'LIVE');
    const paper = realizedPnlTotal(paperTrades);
    const live = realizedPnlTotal(liveTrades);
    return { paper, live, combined: paper + live };
  }

  filterSummaryChips(): FilterSummaryChip[] {
    const chips: FilterSummaryChip[] = [];
    if (this.strategyCanonicalFilter) {
      chips.push({
        label: 'Strategy',
        value: formatPaperHistoryStrategyLabel(this.strategyCanonicalFilter),
      });
    }
    if (this.sourceFilter !== 'all') {
      chips.push({
        label: 'Source',
        value: this.sourceFilter === 'paper' ? 'Paper' : 'Live',
      });
    }
    const period = this.periodChipLabel();
    if (period && (this.strategyCanonicalFilter || this.sourceFilter !== 'all')) {
      chips.push({ label: 'Period', value: period });
    }
    return chips;
  }

  private periodChipLabel(): string {
    const raw = (this.periodLabel ?? '').trim();
    if (!raw) {
      return '';
    }
    return raw.replace(/\s*\(IST\)\s*$/i, '').replace(/^Last 1 month$/i, 'Last 1 Month');
  }

  emptyStateMessage(): string {
    const strategyId = this.strategyCanonicalFilter || this.strategyFilter;
    const strategyName = strategyId ? formatPaperHistoryStrategyLabel(strategyId) : '';
    const period = this.periodChipLabel() || 'the selected period';
    if (strategyName && this.sourceFilter === 'paper') {
      return `No Paper trades found for ${strategyName} during ${period}.`;
    }
    if (strategyName && this.sourceFilter === 'live') {
      return `No Live trades found for ${strategyName} during ${period}.`;
    }
    if (strategyName) {
      return `No trades found for ${strategyName} during ${period}.`;
    }
    if (this.sourceFilter === 'paper') {
      return `No Paper trades found during ${period}.`;
    }
    if (this.sourceFilter === 'live') {
      return `No Live trades found during ${period}.`;
    }
    return 'No trades match your filters.';
  }

  setPnlChip(chip: 'all' | 'positive' | 'negative'): void {
    this.pnlChip = chip;
    this.page = 1;
    this.rebuildList();
  }

  setPage(p: number): void {
    this.page = Math.min(Math.max(1, p), this.pageCount);
    this.expandedId = null;
    this.rebuildList();
  }

  toggleColumn(key: ColumnKey): void {
    if (this.visibleColumns.has(key)) {
      this.visibleColumns.delete(key);
    } else {
      this.visibleColumns.add(key);
    }
    this.cdr.markForCheck();
  }

  colVisible(key: ColumnKey): boolean {
    return this.visibleColumns.has(key);
  }

  visibleColumnCount(): number {
    return this.visibleColumns.size || 1;
  }

  toggleExpand(id: string): void {
    this.expandedId = this.expandedId === id ? null : id;
    this.cdr.markForCheck();
  }

  isExpanded(id: string): boolean {
    return this.expandedId === id;
  }

  exportCsv(): void {
    const blob = new Blob([exportTradesCsv(this.filteredTrades)], { type: 'text/csv;charset=utf-8' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `trade-history-${new Date().toISOString().slice(0, 10)}.csv`;
    a.click();
    URL.revokeObjectURL(url);
  }

  strategyLabel(id: string): string {
    return formatPaperHistoryStrategyLabel(id);
  }

  strategyChipLabel(id: string): string {
    const label = formatPaperHistoryStrategyLabel(id);
    if (label.length <= 22) {
      return label;
    }
    return `${label.slice(0, 20)}…`;
  }

  directionShort(d: string): string {
    return paperTradeDirectionShort(d);
  }

  isBull(direction: string): boolean {
    return direction === 'BULLISH' || direction === 'LONG';
  }

  isBear(direction: string): boolean {
    return direction === 'BEARISH' || direction === 'SHORT';
  }

  showStatusChip(status: string): boolean {
    const key = (status ?? '').toUpperCase();
    return key !== 'CLOSED';
  }

  showOutcomeHint(trade: PaperTradeHistoryItem): boolean {
    if (!trade.exitReason?.trim()) {
      return false;
    }
    if (trade.status === 'CLOSED') {
      const kind = classifyPaperOutcome(trade.exitReason, trade.status).kind;
      return kind === 'timeout' || kind === 'other';
    }
    return true;
  }

  executedAt(t: PaperTradeHistoryItem): string | null {
    return paperTradeExecutedAt(t);
  }

  closedAt(t: PaperTradeHistoryItem): string | null {
    return paperTradeClosedAt(t);
  }

  capital(t: PaperTradeHistoryItem): number {
    return paperTradeCapitalUsed(t);
  }

  entryPrice(t: PaperTradeHistoryItem): number | null {
    return paperTradeEntryPrice(t);
  }

  exitPrice(t: PaperTradeHistoryItem): number | null {
    return paperTradeExitPrice(t);
  }

  qtyLabel(t: PaperTradeHistoryItem): string {
    return paperTradeQuantityLabel(t);
  }

  qtyTitle(t: PaperTradeHistoryItem): string {
    return paperTradeQuantityTitle(t);
  }

  outcome(t: PaperTradeHistoryItem) {
    return classifyPaperOutcome(t.exitReason, t.status);
  }

  statusChip(status: string): { class: string; label: string } {
    const key = (status ?? '').toUpperCase();
    switch (key) {
      case 'CLOSED':
        return { class: 'chip-closed', label: 'Closed' };
      case 'OPEN':
        return { class: 'chip-open', label: 'Open' };
      case 'EXPIRED':
        return { class: 'chip-expired', label: 'Expired' };
      case 'REJECTED':
        return { class: 'chip-rejected', label: 'Rejected' };
      case 'CANCELLED':
        return { class: 'chip-cancelled', label: 'Cancelled' };
      case 'INTERRUPTED':
        return { class: 'chip-interrupted', label: 'Interrupted' };
      case 'PROPOSED':
        return { class: 'chip-proposed', label: 'Proposed' };
      default:
        return { class: 'chip-other', label: status || '—' };
    }
  }

  netPnl(t: PaperTradeHistoryItem): number | null {
    return tradeNetPnl(t);
  }

  roi(t: PaperTradeHistoryItem): number | null {
    return tradeRoiPct(t);
  }

  trendLabel(delta: { pct: number | null; direction: string }): string {
    if (delta.pct == null) {
      return '';
    }
    const sign = delta.pct > 0 ? '+' : '';
    return `${sign}${delta.pct.toFixed(1)}% vs earlier`;
  }

  sparklinePath(series: number[], width = 72, height = 26): string {
    if (series.length < 2) {
      return '';
    }
    const min = Math.min(...series);
    const max = Math.max(...series);
    const span = max - min || 1;
    return series
      .map((v, i) => {
        const x = (i / (series.length - 1)) * width;
        const y = height - ((v - min) / span) * (height - 4) - 2;
        return `${i === 0 ? 'M' : 'L'}${x.toFixed(1)},${y.toFixed(1)}`;
      })
      .join(' ');
  }

  equitySeries(): number[] {
    return (this.analytics?.equityCurve ?? []).map((p) => p.cumulativePnl);
  }

  equitySeriesWindow(): number[] {
    const series = this.equitySeries();
    if (series.length <= 30) {
      return series;
    }
    return series.slice(-30);
  }

  last30DailyPnl() {
    const daily = this.analytics?.dailyPnl ?? [];
    return daily.length <= 30 ? daily : daily.slice(-30);
  }

  trendWindowLabel(): string {
    const daily = this.last30DailyPnl();
    if (!daily.length) {
      return this.periodLabel || 'Selected period';
    }
    if (daily.length === 1) {
      return daily[0].dateKey;
    }
    return `${daily[0].dateKey} – ${daily[daily.length - 1].dateKey}`;
  }

  widgetEquityPath(width = 560, height = 118): string {
    return this.sparklinePath(this.equitySeriesWindow(), width, height);
  }

  widgetEquityAreaPath(width = 560, height = 118): string {
    const line = this.widgetEquityPath(width, height);
    if (!line) {
      return '';
    }
    return `${line} L${width},${height} L0,${height} Z`;
  }

  widgetDailyBarLayout(): Array<{ x: number; w: number; y: number; h: number; up: boolean }> {
    const bars = this.last30DailyPnl();
    const W = 560;
    const chartBottom = 168;
    const maxBar = 28;
    const max = Math.max(...bars.map((b) => Math.abs(b.pnl)), 1);
    const slot = bars.length ? W / bars.length : W;
    return bars.map((b, i) => {
      const h = Math.max(2, (Math.abs(b.pnl) / max) * maxBar);
      const up = b.pnl >= 0;
      return {
        x: i * slot + slot * 0.22,
        w: Math.max(2, slot * 0.56),
        y: up ? chartBottom - h : chartBottom,
        h,
        up,
      };
    });
  }

  dailyPnlSeries(): number[] {
    return (this.analytics?.dailyPnl ?? []).map((d) => d.pnl);
  }

  winRateSeries(): number[] {
    const daily = this.analytics?.dailyPnl ?? [];
    if (!daily.length) {
      return [];
    }
    let wins = 0;
    let total = 0;
    return daily.map((d) => {
      total += 1;
      if (d.pnl > 0) {
        wins += 1;
      }
      return total ? (wins / total) * 100 : 0;
    });
  }

  equityPath(width = 480, height = 112): string {
    return this.sparklinePath(this.equitySeries(), width, height);
  }

  equityAreaPath(width = 480, height = 112): string {
    const line = this.equityPath(width, height);
    if (!line) {
      return '';
    }
    return `${line} L${width},${height} L0,${height} Z`;
  }

  dailyBarHeight(pnl: number): number {
    const daily = this.analytics?.dailyPnl ?? [];
    const max = Math.max(...daily.map((d) => Math.abs(d.pnl)), 1);
    return Math.max(4, (Math.abs(pnl) / max) * 72);
  }

  winLossConic(): string {
    const w = this.analytics?.winLoss.wins ?? 0;
    const l = this.analytics?.winLoss.losses ?? 0;
    const b = this.analytics?.winLoss.breakeven ?? 0;
    const total = w + l + b || 1;
    const wp = (w / total) * 100;
    const lp = ((w + l) / total) * 100;
    return `conic-gradient(var(--twb-up, #16a34a) 0 ${wp}%, var(--twb-down, #dc2626) ${wp}% ${lp}%, var(--twb-muted, #94a3b8) ${lp}% 100%)`;
  }

  strategyBarWidth(net: number): number {
    const rows = this.analytics?.strategies ?? [];
    const max = Math.max(...rows.map((r) => Math.abs(r.netProfit)), 1);
    return Math.max(6, (Math.abs(net) / max) * 100);
  }

  strategyTradeSharePct(trades: number): number {
    const total = (this.analytics?.strategies ?? []).reduce((sum, r) => sum + r.trades, 0);
    if (!total) {
      return 0;
    }
    return (trades / total) * 100;
  }

  winLossCenterLabel(): string {
    const wr = this.analytics?.summary.winRatePct;
    return wr != null ? `${wr.toFixed(0)}%` : '—';
  }

  onFilterChange(): void {
    this.page = 1;
    this.rebuildList();
  }

  tradeHasDetails(t: PaperTradeHistoryItem): boolean {
    return !!t.entryReason || !!paperTradeExecutedAt(t) || !!t.closedAt || (t.legs?.length ?? 0) > 0;
  }

  entryFills(t: PaperTradeHistoryItem): string {
    return (t.legs ?? [])
      .filter((leg) => leg.entryPrice != null)
      .map((leg) => `${leg.side} ${leg.symbol} × ${leg.quantity} @ ₹${leg.entryPrice!.toFixed(2)}`)
      .join(' · ');
  }

  exitFills(t: PaperTradeHistoryItem): string {
    return (t.legs ?? [])
      .filter((leg) => leg.exitPrice != null)
      .map((leg) => `${leg.side} ${leg.symbol} × ${leg.quantity} @ ₹${leg.exitPrice!.toFixed(2)}`)
      .join(' · ');
  }

  durationLabel(t: PaperTradeHistoryItem): string | null {
    const start = paperTradeExecutedAt(t);
    const end = paperTradeClosedAt(t);
    if (!start || !end) {
      return null;
    }
    const ms = Date.parse(end) - Date.parse(start);
    if (!Number.isFinite(ms) || ms < 0) {
      return null;
    }
    const mins = Math.round(ms / 60_000);
    if (mins < 60) {
      return `${mins} min`;
    }
    const hours = Math.floor(mins / 60);
    const rem = mins % 60;
    return rem > 0 ? `${hours}h ${rem}m` : `${hours}h`;
  }
}
