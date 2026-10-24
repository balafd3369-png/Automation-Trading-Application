import { DatePipe, DecimalPipe } from '@angular/common';
import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  DestroyRef,
  OnInit,
  inject,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { catchError, finalize, interval, of, startWith, switchMap, take } from 'rxjs';
import { Router } from '@angular/router';
import { BoardResponse, LiveExecutionSnapshot, SymbolSnapshot } from '../core/board.models';
import { contextFromStock, priceChangeFromClose, tradeTicketExchange } from '../core/trade-ticket.util';
import { WatchlistHoverRowComponent } from '../shared/watchlist-hover-row.component';
import { BoardService } from '../core/board.service';
import {
  ManualExecutionMode,
  ManualInstrumentType,
  ManualOrderType,
  ManualPlaceOrderRequest,
  ManualPosition,
  ManualTradeHistoryItem,
  ManualTradingSnapshot,
  ManualTransactionType,
} from '../core/manual-trading.models';
import { TradeTicketService } from '../core/trade-ticket.service';
import { InstrumentKind, normalizeInstrumentType } from '../core/instrument-type.util';

export interface DeskWatchlistRow {
  trackId: string;
  label: string;
  kind: 'equity' | 'index' | 'index-option';
  snap?: SymbolSnapshot;
  underlying: string;
  optionSide?: 'CE' | 'PE';
}

const WATCHLIST_EQUITY = ['RELIANCE', 'INFY', 'TCS', 'HDFCBANK'];
const WATCHLIST_INDEX = ['NIFTY', 'BANKNIFTY', 'FINNIFTY'];
const WATCHLIST_INDEX_OPTIONS: Array<{ underlying: string; side: 'CE' | 'PE' }> = [
  { underlying: 'NIFTY', side: 'CE' },
  { underlying: 'BANKNIFTY', side: 'PE' },
];

@Component({
  selector: 'app-manual-trading-page',
  standalone: true,
  imports: [DatePipe, DecimalPipe, FormsModule, WatchlistHoverRowComponent],
  templateUrl: './manual-trading-page.component.html',
  styleUrl: './manual-trading-page.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ManualTradingPageComponent implements OnInit {
  private readonly board = inject(BoardService);
  private readonly tradeTickets = inject(TradeTicketService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly cdr = inject(ChangeDetectorRef);

  executionMode: ManualExecutionMode = 'PAPER';
  instrumentType: ManualInstrumentType = 'STOCK';
  symbol = 'RELIANCE';
  underlying = 'RELIANCE';
  transactionType: ManualTransactionType = 'BUY';
  orderType: ManualOrderType = 'MARKET';
  quantity = 1;
  product = 'MIS';
  limitPrice: number | null = null;
  strike: number | null = null;
  optionRight: 'CE' | 'PE' = 'CE';
  expiry = '';

  snapshot: ManualTradingSnapshot | null = null;
  liveBook: LiveExecutionSnapshot | null = null;
  statusMessage = '';
  loading = false;
  futuresEnabled = false;
  bookTab: 'positions' | 'orders' | 'holdings' | 'history' = 'positions';
  quoteSnap: SymbolSnapshot | null = null;
  boardData: BoardResponse | null = null;

  ngOnInit(): void {
    void this.fetchFeatures();
    this.refreshQuote();
    interval(6_000)
      .pipe(
        startWith(0),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe(() => this.refreshQuote());
    interval(8_000)
      .pipe(
        startWith(0),
        switchMap(() => this.board.manualTrading(this.executionMode)),
        catchError(() => of(null)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((snap) => {
        if (snap) {
          this.snapshot = snap;
          this.cdr.markForCheck();
        }
      });

    interval(8_000)
      .pipe(
        startWith(0),
        switchMap(() =>
          this.executionMode === 'LIVE'
            ? this.board.liveAgent().pipe(catchError(() => of(null)))
            : of(null),
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((live) => {
        this.liveBook = live;
        this.cdr.markForCheck();
      });

    interval(10_000)
      .pipe(
        startWith(0),
        switchMap(() => this.board.fetchBoard()),
        catchError(() => of(null)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((board) => {
        this.boardData = board;
        this.cdr.markForCheck();
      });
  }

  watchlistRows(): DeskWatchlistRow[] {
    const symbols = this.boardData?.symbols ?? [];
    const find = (name: string) => symbols.find((s) => s.name === name);
    const rows: DeskWatchlistRow[] = [];
    for (const name of WATCHLIST_EQUITY) {
      const snap = find(name);
      rows.push({
        trackId: `eq-${name}`,
        label: name,
        kind: 'equity',
        underlying: name,
        snap,
      });
    }
    for (const name of WATCHLIST_INDEX) {
      const snap = find(name);
      rows.push({
        trackId: `idx-${name}`,
        label: name,
        kind: 'index',
        underlying: name,
        snap,
      });
    }
    for (const opt of WATCHLIST_INDEX_OPTIONS) {
      const snap = find(opt.underlying);
      rows.push({
        trackId: `opt-${opt.underlying}-${opt.side}`,
        label: `${opt.underlying} ${opt.side}`,
        kind: 'index-option',
        underlying: opt.underlying,
        optionSide: opt.side,
        snap,
      });
    }
    return rows;
  }

  indexSnapshots(): SymbolSnapshot[] {
    const symbols = this.boardData?.symbols ?? [];
    return WATCHLIST_INDEX
      .map((name) => symbols.find((s) => s.name === name))
      .filter((s): s is SymbolSnapshot => !!s);
  }

  indexChange(snap: SymbolSnapshot): { points: number; percent: number } | null {
    return priceChangeFromClose(snap.last, snap.previousClose);
  }

  rowLtp(row: DeskWatchlistRow): number | null {
    return row.snap?.last ?? null;
  }

  rowChange(row: DeskWatchlistRow): { points: number; percent: number } | null {
    if (!row.snap) {
      return null;
    }
    return priceChangeFromClose(row.snap.last, row.snap.previousClose);
  }

  rowTone(row: DeskWatchlistRow): 'up' | 'down' | 'flat' {
    const ch = this.rowChange(row);
    if (!ch) {
      return 'flat';
    }
    if (ch.points > 0) {
      return 'up';
    }
    if (ch.points < 0) {
      return 'down';
    }
    return 'flat';
  }

  selectWatchlistRow(row: DeskWatchlistRow): void {
    if (row.kind === 'index-option') {
      this.instrumentType = 'OPTION';
      this.underlying = row.underlying;
      this.symbol = row.underlying;
      this.optionRight = row.optionSide ?? 'CE';
    } else if (row.kind === 'index') {
      this.instrumentType = 'FUTURE';
      this.symbol = row.underlying;
      this.underlying = row.underlying;
    } else {
      this.instrumentType = 'STOCK';
      this.symbol = row.underlying;
      this.underlying = row.underlying;
    }
    this.onInstrumentChange();
    this.cdr.markForCheck();
  }

  openWatchlistTicket(row: DeskWatchlistRow, side: ManualTransactionType): void {
    this.selectWatchlistRow(row);
    if (row.kind === 'index-option') {
      this.tradeTickets.open({
        instrumentType: 'OPTION',
        displayName: row.label,
        exchange: tradeTicketExchange('OPTION', row.underlying),
        symbol: row.underlying,
        underlying: row.underlying,
        transactionType: side,
        ltp: row.snap?.last ?? null,
        previousClose: row.snap?.previousClose ?? null,
        optionRight: row.optionSide ?? 'CE',
        strike: this.strike,
        expiry: this.expiry || null,
        executionMode: this.executionMode,
      });
    } else if (row.snap) {
      this.tradeTickets.open(
        contextFromStock(row.snap, side, this.executionMode),
      );
    } else {
      this.openTradeTicket(side);
    }
    this.cdr.markForCheck();
  }

  openMarketChart(): void {
    void this.router.navigateByUrl('/market');
  }

  openWatchlistChart(row: DeskWatchlistRow): void {
    this.selectWatchlistRow(row);
    void this.router.navigateByUrl('/market');
  }

  private todayIstKey(): string {
    return new Date().toLocaleDateString('en-CA', { timeZone: 'Asia/Kolkata' });
  }

  private closedToday(): ManualTradeHistoryItem[] {
    const key = this.todayIstKey();
    return (this.snapshot?.tradeHistory ?? []).filter(
      (t) => t.closedAt && this.istDayKey(t.closedAt) === key,
    );
  }

  private istDayKey(iso: string): string {
    return new Date(iso).toLocaleDateString('en-CA', { timeZone: 'Asia/Kolkata' });
  }

  todayTradesCount(): number {
    return this.closedToday().length;
  }

  todayWinRate(): number | null {
    const closed = this.closedToday();
    if (!closed.length) {
      return null;
    }
    const wins = closed.filter((t) => (t.pnl ?? 0) > 0).length;
    return (wins / closed.length) * 100;
  }

  todayRealizedPnl(): number {
    return this.closedToday().reduce((sum, t) => sum + (t.pnl ?? 0), 0);
  }

  openOrdersCount(): number {
    return (this.snapshot?.orders ?? []).filter(
      (o) => o.status === 'PENDING' || o.status === 'SUBMITTED',
    ).length;
  }

  positionsCount(): number {
    return this.snapshot?.positions.length ?? 0;
  }

  holdingsCount(): number {
    return this.snapshot?.holdings.length ?? 0;
  }

  historyCount(): number {
    return this.snapshot?.tradeHistory.length ?? 0;
  }

  ordersCount(): number {
    return this.snapshot?.orders.length ?? 0;
  }

  private async fetchFeatures(): Promise<void> {
    try {
      const res = await fetch('/api/platform/features');
      if (res.ok) {
        const json = (await res.json()) as { futuresEnabled?: boolean };
        this.futuresEnabled = !!json.futuresEnabled;
        this.cdr.markForCheck();
      }
    } catch {
      /* ignore */
    }
  }

  setMode(mode: ManualExecutionMode): void {
    this.executionMode = mode;
    this.product = mode === 'LIVE' && this.instrumentType === 'STOCK' ? 'CNC' : 'MIS';
    this.liveBook = null;
    this.refresh();
  }

  onInstrumentChange(): void {
    if (this.instrumentType === 'STOCK') {
      this.underlying = this.symbol;
      this.product = this.executionMode === 'LIVE' ? 'CNC' : 'MIS';
    } else if (this.instrumentType === 'FUTURE') {
      this.product = 'NRML';
    } else {
      this.product = 'MIS';
    }
    this.refreshQuote();
  }

  setInstrumentType(type: ManualInstrumentType): void {
    this.instrumentType = type;
    this.onInstrumentChange();
    this.cdr.markForCheck();
  }

  onSymbolEdited(): void {
    if (this.instrumentType === 'STOCK') {
      this.underlying = this.symbol;
    }
    this.refreshQuote();
  }

  refreshQuote(): void {
    const name = (this.instrumentType === 'STOCK' ? this.symbol : this.underlying).trim();
    if (!name) {
      this.quoteSnap = null;
      this.cdr.markForCheck();
      return;
    }
    this.board
      .ensureSymbol(name)
      .pipe(take(1), catchError(() => of(null)))
      .subscribe((snap) => {
        this.quoteSnap = snap;
        this.cdr.markForCheck();
      });
  }

  quoteLtp(): number | null {
    return this.quoteSnap?.last ?? null;
  }

  quoteExchange(): string {
    if (!this.instrumentType) {
      return 'NSE';
    }
    return tradeTicketExchange(this.instrumentType, this.underlying || this.symbol);
  }

  quoteChange(): { points: number; percent: number } | null {
    if (!this.quoteSnap) {
      return null;
    }
    return priceChangeFromClose(this.quoteSnap.last, this.quoteSnap.previousClose);
  }

  quoteTone(): 'up' | 'down' | 'flat' {
    const ch = this.quoteChange();
    if (!ch) {
      return 'flat';
    }
    if (ch.points > 0) {
      return 'up';
    }
    if (ch.points < 0) {
      return 'down';
    }
    return 'flat';
  }

  openPnl(): number {
    return (this.snapshot?.positions ?? []).reduce((sum, p) => sum + p.pnl, 0);
  }

  setBookTab(tab: 'positions' | 'orders' | 'holdings' | 'history'): void {
    this.bookTab = tab;
    this.cdr.markForCheck();
  }

  openTradeTicket(side: ManualTransactionType): void {
    const underlying = (this.underlying || this.symbol).trim();
    const sym = this.symbol.trim();
    this.tradeTickets.open({
      instrumentType: this.instrumentType,
      displayName: sym || underlying,
      exchange: tradeTicketExchange(this.instrumentType, underlying),
      symbol: sym || underlying,
      underlying,
      transactionType: side,
      ltp: this.quoteSnap?.last ?? null,
      previousClose: this.quoteSnap?.previousClose ?? null,
      strike: this.instrumentType === 'OPTION' ? this.strike : null,
      optionRight: this.instrumentType === 'OPTION' ? this.optionRight : null,
      expiry: this.instrumentType === 'OPTION' ? this.expiry || null : null,
      executionMode: this.executionMode,
    });
    this.cdr.markForCheck();
  }

  refresh(): void {
    this.board.manualTrading(this.executionMode).subscribe({
      next: (snap) => {
        this.snapshot = snap;
        this.cdr.markForCheck();
      },
    });
  }

  placeOrder(): void {
    if (this.instrumentType === 'FUTURE' && this.executionMode === 'LIVE' && !this.futuresEnabled) {
      this.statusMessage = 'Live futures are disabled. Enable TradingPlatform:FuturesEnabled or use paper mode.';
      this.cdr.markForCheck();
      return;
    }

    const body: ManualPlaceOrderRequest = {
      executionMode: this.executionMode,
      instrumentType: this.instrumentType,
      symbol: this.symbol.trim(),
      underlying: (this.underlying || this.symbol).trim(),
      transactionType: this.transactionType,
      orderType: this.orderType,
      quantity: this.quantity,
      product: this.product,
      limitPrice: this.orderType === 'LIMIT' ? this.limitPrice : null,
      strike: this.instrumentType === 'OPTION' ? this.strike : null,
      optionRight: this.instrumentType === 'OPTION' ? this.optionRight : null,
      expiry: this.instrumentType === 'OPTION' ? this.expiry || null : null,
    };

    this.loading = true;
    this.board
      .placeManualOrder(body)
      .pipe(
        catchError((err) => {
          const msg = err?.error?.message ?? err?.message ?? 'Order failed';
          this.statusMessage = msg;
          return of(null);
        }),
        finalize(() => {
          this.loading = false;
          this.cdr.markForCheck();
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((res) => {
        if (res) {
          this.statusMessage = res.message;
          this.refresh();
        }
      });
  }

  cancelOrder(orderId: string): void {
    this.board.cancelManualOrder(orderId).subscribe({
      next: (res) => {
        this.statusMessage = res.message;
        this.refresh();
      },
      error: (err) => {
        this.statusMessage = err?.error?.message ?? 'Cancel failed';
        this.cdr.markForCheck();
      },
    });
  }

  openTicketFromPosition(p: ManualPosition, side: ManualTransactionType): void {
    const kind = normalizeInstrumentType(p.instrumentType);
    let instrumentType: ManualInstrumentType = 'STOCK';
    if (kind === InstrumentKind.Future) {
      instrumentType = 'FUTURE';
    } else if (kind === InstrumentKind.Option) {
      instrumentType = 'OPTION';
    }
    const underlying = (p.underlying || p.symbol).trim();
    this.tradeTickets.open({
      instrumentType,
      displayName: p.symbol,
      exchange: tradeTicketExchange(instrumentType, underlying),
      symbol: p.symbol,
      underlying,
      transactionType: side,
      ltp: p.currentPrice,
      previousClose: p.entryPrice,
      executionMode: this.executionMode,
      position: {
        tradeId: p.tradeId,
        quantity: p.quantity,
        entryPrice: p.entryPrice,
        currentPrice: p.currentPrice,
        pnl: p.pnl,
        direction: p.direction,
      },
    });
    this.cdr.markForCheck();
  }

  closePosition(tradeId: string): void {
    this.board.closeManualPosition(this.executionMode, tradeId).subscribe({
      next: (res) => {
        this.statusMessage = res.message;
        this.refresh();
      },
      error: (err) => {
        this.statusMessage = err?.error?.message ?? 'Close failed';
        this.cdr.markForCheck();
      },
    });
  }
}
