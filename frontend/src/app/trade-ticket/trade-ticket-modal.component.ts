import { DecimalPipe } from '@angular/common';
import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  DestroyRef,
  effect,
  inject,
  OnInit,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { catchError, finalize, interval, of, startWith, switchMap } from 'rxjs';
import { BoardService } from '../core/board.service';
import {
  ManualExecutionMode,
  ManualOrderType,
  ManualPlaceOrderRequest,
  ManualPosition,
  ManualTradingSnapshot,
  ManualTransactionType,
} from '../core/manual-trading.models';
import { TradeTicketService } from '../core/trade-ticket.service';
import { defaultProduct, priceChangeFromClose } from '../core/trade-ticket.util';
import { TradingQuantityRules } from '../core/trading-quantity.models';
import {
  lotsFromQuantity,
  normalizeStockQuantity,
  quantityFromLots,
  validateTradingQuantity,
} from '../core/trading-quantity.util';
import { TradingRefreshService } from '../core/trading-refresh.service';
import { TradeTicketContext, TradeTicketStep } from './trade-ticket.models';

@Component({
  selector: 'app-trade-ticket-modal',
  standalone: true,
  imports: [DecimalPipe, FormsModule],
  templateUrl: './trade-ticket-modal.component.html',
  styleUrl: './trade-ticket-modal.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TradeTicketModalComponent implements OnInit {
  private readonly tickets = inject(TradeTicketService);
  private readonly board = inject(BoardService);
  private readonly tradingRefresh = inject(TradingRefreshService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly cdr = inject(ChangeDetectorRef);

  ctx: TradeTicketContext | null = null;
  step: TradeTicketStep = 'form';
  executionMode: ManualExecutionMode = 'PAPER';
  orderType: ManualOrderType = 'MARKET';
  limitPrice: number | null = null;
  stockQty = 1;
  lots = 1;
  qtyRules: TradingQuantityRules | null = null;
  product = 'MIS';
  loading = false;
  statusMessage = '';
  snapshot: ManualTradingSnapshot | null = null;
  futuresEnabled = false;
  closePartial = false;
  matchedPosition: ManualPosition | null = null;

  constructor() {
    effect(() => {
      const next = this.tickets.context();
      if (!next) {
        this.ctx = null;
        this.step = 'form';
        this.cdr.markForCheck();
        return;
      }
      this.applyContext(next);
    });
  }

  ngOnInit(): void {
    void this.fetchFeatures();
    interval(6_000)
      .pipe(
        startWith(0),
        switchMap(() => {
          if (!this.ctx) {
            return of(null);
          }
          return this.board.manualTrading(this.executionMode).pipe(catchError(() => of(null)));
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((snap) => {
        if (snap && this.ctx) {
          this.snapshot = snap;
          this.refreshMatchedPosition();
          this.cdr.markForCheck();
        }
      });
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

  private applyContext(ctx: TradeTicketContext): void {
    this.ctx = ctx;
    this.step = 'form';
    this.statusMessage = '';
    this.executionMode = ctx.executionMode ?? 'PAPER';
    this.orderType = 'MARKET';
    this.limitPrice = ctx.ltp ?? null;
    this.transactionType = ctx.transactionType;
    this.product = defaultProduct(this.executionMode, ctx.instrumentType);
    this.qtyRules = null;
    this.stockQty = 1;
    this.lots = 1;
    this.closePartial = false;
    this.board
      .quantityRules({
        instrumentType: ctx.instrumentType,
        underlying: ctx.underlying,
        symbol: ctx.symbol,
      })
      .pipe(catchError(() => of(null)))
      .subscribe((rules) => {
        if (rules && this.ctx === ctx) {
          this.qtyRules = rules;
          if (rules.usesLots) {
            this.lots = rules.defaultLots || 1;
          } else {
            this.stockQty = rules.defaultQuantity || 1;
          }
          this.cdr.markForCheck();
        }
      });
    this.board.manualTrading(this.executionMode).subscribe({
      next: (snap) => {
        this.snapshot = snap;
        this.refreshMatchedPosition(ctx);
        this.cdr.markForCheck();
      },
    });
    this.cdr.markForCheck();
  }

  transactionType: ManualTransactionType = 'BUY';

  setMode(mode: ManualExecutionMode): void {
    if (!this.ctx) {
      return;
    }
    this.executionMode = mode;
    this.product = defaultProduct(mode, this.ctx.instrumentType);
    this.board.manualTrading(mode).subscribe({
      next: (snap) => {
        this.snapshot = snap;
        this.refreshMatchedPosition();
        this.cdr.markForCheck();
      },
    });
  }

  setSide(side: ManualTransactionType): void {
    this.transactionType = side;
    this.cdr.markForCheck();
  }

  isBuyTheme(): boolean {
    return this.transactionType === 'BUY';
  }

  usesLots(): boolean {
    return this.qtyRules?.usesLots ?? this.isFnoInstrument();
  }

  isFnoInstrument(): boolean {
    return this.ctx?.instrumentType === 'OPTION' || this.ctx?.instrumentType === 'FUTURE';
  }

  lotSize(): number {
    return this.qtyRules?.lotSize ?? 1;
  }

  instrumentTypeLabel(): string {
    if (!this.ctx) {
      return '';
    }
    switch (this.ctx.instrumentType) {
      case 'STOCK':
        return 'Stock';
      case 'OPTION':
        return 'Option';
      case 'FUTURE':
        return 'Future';
      default:
        return this.ctx.instrumentType;
    }
  }

  quantity(): number {
    if (!this.ctx) {
      return 0;
    }
    if (this.usesLots()) {
      const rules = this.qtyRules;
      if (rules) {
        return quantityFromLots(rules, this.lots);
      }
      return Math.max(1, this.lots) * this.lotSize();
    }
    return normalizeStockQuantity(this.stockQty);
  }

  onStockQtyChange(): void {
    this.stockQty = normalizeStockQuantity(this.stockQty);
    this.cdr.markForCheck();
  }

  effectivePrice(): number {
    if (this.orderType === 'LIMIT' && this.limitPrice != null && this.limitPrice > 0) {
      return this.limitPrice;
    }
    return this.ctx?.ltp ?? 0;
  }

  estimatedTradeValue(): number {
    return this.quantity() * this.effectivePrice();
  }

  changeDisplay(): { points: number; percent: number } | null {
    if (!this.ctx) {
      return null;
    }
    return priceChangeFromClose(this.ctx.ltp, this.ctx.previousClose);
  }

  bumpStockQty(delta: number): void {
    this.stockQty = normalizeStockQuantity(this.stockQty + delta);
    this.cdr.markForCheck();
  }

  bumpLots(delta: number): void {
    this.lots = Math.max(1, this.lots + delta);
    this.cdr.markForCheck();
  }

  paperAvailable(): number {
    return this.snapshot?.capital.availableCapital ?? 0;
  }

  paperRequired(): number {
    return this.estimatedTradeValue();
  }

  paperAfter(): number {
    if (this.transactionType === 'SELL') {
      return this.paperAvailable() + this.paperRequired();
    }
    return this.paperAvailable() - this.paperRequired();
  }

  liveAvailable(): number {
    return this.snapshot?.capital.brokerAvailableFunds ?? 0;
  }

  liveMargin(): number {
    return this.estimatedTradeValue();
  }

  close(): void {
    this.tickets.close();
  }

  goPreview(): void {
    if (!this.validateForm()) {
      return;
    }
    this.step = 'preview';
    this.cdr.markForCheck();
  }

  backToForm(): void {
    this.step = 'form';
    this.cdr.markForCheck();
  }

  private validateForm(): boolean {
    if (!this.ctx) {
      return false;
    }
    if (
      this.ctx.instrumentType === 'FUTURE' &&
      this.executionMode === 'LIVE' &&
      !this.futuresEnabled
    ) {
      this.statusMessage =
        'Live futures are disabled. Use paper mode or enable TradingPlatform:FuturesEnabled.';
      this.cdr.markForCheck();
      return false;
    }
    if (this.orderType === 'LIMIT' && (this.limitPrice == null || this.limitPrice <= 0)) {
      this.statusMessage = 'Enter a valid limit price.';
      this.cdr.markForCheck();
      return false;
    }
    if (this.qtyRules) {
      const qtyCheck = validateTradingQuantity(this.qtyRules, this.quantity());
      if (!qtyCheck.valid) {
        this.statusMessage = qtyCheck.message;
        this.cdr.markForCheck();
        return false;
      }
    }
    if (this.transactionType === 'SELL' && this.matchedPosition) {
      const maxQty = this.matchedPosition.quantity;
      if (this.quantity() > maxQty) {
        this.statusMessage = `Quantity cannot exceed open position (${maxQty}).`;
        this.cdr.markForCheck();
        return false;
      }
    }
    this.statusMessage = '';
    return true;
  }

  confirmOrder(): void {
    if (!this.ctx || !this.validateForm()) {
      return;
    }

    if (
      this.transactionType === 'SELL' &&
      this.matchedPosition &&
      !this.closePartial &&
      this.quantity() === this.matchedPosition.quantity
    ) {
      this.loading = true;
      this.board
        .closeManualPosition(this.executionMode, this.matchedPosition.tradeId)
        .pipe(
          catchError((err) => {
            this.statusMessage = err?.error?.message ?? err?.message ?? 'Close failed';
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
            if (this.executionMode === 'PAPER') {
              this.tradingRefresh.requestPaperRefresh();
            }
            this.tickets.close();
          }
        });
      return;
    }

    const body: ManualPlaceOrderRequest = {
      executionMode: this.executionMode,
      instrumentType: this.ctx.instrumentType,
      symbol: this.ctx.symbol.trim(),
      underlying: (this.ctx.underlying || this.ctx.symbol).trim(),
      transactionType: this.transactionType,
      orderType: this.orderType,
      quantity: this.quantity(),
      product: this.product,
      limitPrice: this.orderType === 'LIMIT' ? this.limitPrice : null,
      strike: this.ctx.instrumentType === 'OPTION' ? this.ctx.strike : null,
      optionRight: this.ctx.instrumentType === 'OPTION' ? this.ctx.optionRight : null,
      expiry: this.ctx.instrumentType === 'OPTION' ? this.ctx.expiry || null : null,
    };

    this.loading = true;
    this.board
      .placeManualOrder(body)
      .pipe(
        catchError((err) => {
          this.statusMessage = err?.error?.message ?? err?.message ?? 'Order failed';
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
          if (this.executionMode === 'PAPER') {
            this.tradingRefresh.requestPaperRefresh();
          }
          this.tickets.close();
        }
      });
  }

  private refreshMatchedPosition(ctx?: TradeTicketContext): void {
    const c = ctx ?? this.ctx;
    if (!c || !this.snapshot) {
      this.matchedPosition = null;
      return;
    }
    if (c.position) {
      const fromCtx = this.snapshot.positions.find((p) => p.tradeId === c.position!.tradeId);
      this.matchedPosition = fromCtx ?? null;
      return;
    }
    const sym = c.symbol.trim().toUpperCase();
    this.matchedPosition =
      this.snapshot.positions.find(
        (p) =>
          p.symbol.trim().toUpperCase() === sym ||
          p.underlying.trim().toUpperCase() === c.underlying.trim().toUpperCase(),
      ) ?? null;
  }

  maxSellQty(): number {
    return this.matchedPosition?.quantity ?? 0;
  }

  setCloseFull(): void {
    if (!this.matchedPosition) {
      return;
    }
    this.closePartial = false;
    if (this.usesLots() && this.qtyRules) {
      this.lots = lotsFromQuantity(this.qtyRules, this.matchedPosition.quantity);
    } else if (this.usesLots()) {
      const lot = this.lotSize();
      this.lots = Math.max(1, Math.round(this.matchedPosition.quantity / lot));
    } else {
      this.stockQty = this.matchedPosition.quantity;
    }
    this.cdr.markForCheck();
  }
}
