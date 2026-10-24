import { DatePipe, DecimalPipe } from '@angular/common';
import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  EventEmitter,
  Input,
  OnChanges,
  Output,
  SimpleChanges,
  inject,
} from '@angular/core';
import { PaperTradeHistoryItem } from '../core/board.models';
import {
  filterExecutedPaperHistoryTrades,
  paperTradeCapitalPrice,
  paperTradeClosedAt,
  paperTradeDirectionShort,
  paperTradeEntryPrice,
  paperTradeExecutedAt,
  paperTradeExitPrice,
  paperTradeQuantityLabel,
  paperTradeQuantityTitle,
  paperTradeRealizedPnl,
  tradeMatchesCatalogStrategy,
} from '../core/trading-ui.util';

@Component({
  selector: 'app-strategy-trades-panel',
  standalone: true,
  imports: [DecimalPipe, DatePipe],
  templateUrl: './strategy-trades-panel.component.html',
  styleUrl: './strategy-trades-panel.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class StrategyTradesPanelComponent implements OnChanges {
  private readonly cdr = inject(ChangeDetectorRef);

  @Input({ required: true }) strategyId!: string;
  @Input({ required: true }) strategyLabel!: string;
  @Input({ required: true }) periodLabel!: string;
  @Input() trades: PaperTradeHistoryItem[] = [];
  @Input() periodHint =
    'Change the period on Strategies (1 Day / 1 Week / 1 Month / Custom date), then click the strategy again.';
  @Input() contextMode = false;

  @Output() closed = new EventEmitter<void>();

  outcomeFilter: 'all' | 'positive' | 'negative' = 'all';
  page = 1;
  readonly pageSize = 20;

  readonly tradeCapitalPrice = paperTradeCapitalPrice;
  readonly tradeEntryPrice = paperTradeEntryPrice;
  readonly tradeExitPrice = paperTradeExitPrice;
  readonly tradeRealizedPnl = paperTradeRealizedPnl;
  readonly tradeExecutedAt = paperTradeExecutedAt;
  readonly tradeClosedAt = paperTradeClosedAt;
  readonly tradeQuantityLabel = paperTradeQuantityLabel;
  readonly tradeQuantityTitle = paperTradeQuantityTitle;
  readonly tradeDirectionShort = paperTradeDirectionShort;

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['strategyId'] || changes['trades']) {
      this.resetList();
    }
  }

  filteredTrades(): PaperTradeHistoryItem[] {
    let items = filterExecutedPaperHistoryTrades([...this.trades])
      .filter((t) => tradeMatchesCatalogStrategy(t, this.strategyId))
      .sort((a, b) => {
        const at = Date.parse(a.closedAt ?? a.openedAt ?? a.createdAt ?? '') || 0;
        const bt = Date.parse(b.closedAt ?? b.openedAt ?? b.createdAt ?? '') || 0;
        return bt - at;
      });

    if (this.outcomeFilter === 'positive') {
      items = items.filter((t) => t.status === 'CLOSED' && (paperTradeRealizedPnl(t) ?? 0) > 0);
    } else if (this.outcomeFilter === 'negative') {
      items = items.filter((t) => t.status === 'CLOSED' && (paperTradeRealizedPnl(t) ?? 0) < 0);
    }

    return items;
  }

  pageItems(): PaperTradeHistoryItem[] {
    const items = this.filteredTrades();
    const start = (this.page - 1) * this.pageSize;
    return items.slice(start, start + this.pageSize);
  }

  pageCount(): number {
    const total = this.filteredTrades().length;
    return Math.max(1, Math.ceil(total / this.pageSize));
  }

  summary(): {
    closed: number;
    wins: number;
    losses: number;
    open: number;
    realizedPnl: number;
    capitalPrice: number;
    totalTrades: number;
    winRatePercent: number;
    avgProfit: number;
    avgLoss: number;
  } {
    const items = [...this.trades].filter((t) => tradeMatchesCatalogStrategy(t, this.strategyId));
    let wins = 0;
    let losses = 0;
    let realizedPnl = 0;
    let capitalPrice = 0;
    let open = 0;
    let closed = 0;
    let profitSum = 0;
    let lossSum = 0;

    for (const t of items) {
      capitalPrice += paperTradeCapitalPrice(t);
      if (t.status === 'OPEN') {
        open++;
        continue;
      }
      if (t.status === 'CLOSED') {
        closed++;
        const pnl = paperTradeRealizedPnl(t) ?? t.pnl ?? 0;
        realizedPnl += pnl;
        if (pnl > 0) {
          wins++;
          profitSum += pnl;
        } else if (pnl < 0) {
          losses++;
          lossSum += pnl;
        }
      }
    }

    const totalTrades = closed + open;
    const winRatePercent = closed > 0 ? (wins / closed) * 100 : 0;
    const avgProfit = wins > 0 ? profitSum / wins : 0;
    const avgLoss = losses > 0 ? lossSum / losses : 0;

    return {
      closed,
      wins,
      losses,
      open,
      realizedPnl,
      capitalPrice,
      totalTrades,
      winRatePercent,
      avgProfit,
      avgLoss,
    };
  }

  equityCurve(): { linePath: string; areaPath: string; positive: boolean } | null {
    const closed = [...this.trades]
      .filter((trade) => tradeMatchesCatalogStrategy(trade, this.strategyId) && trade.status === 'CLOSED')
      .sort(
        (left, right) =>
          Date.parse(left.closedAt ?? left.openedAt ?? left.createdAt) -
          Date.parse(right.closedAt ?? right.openedAt ?? right.createdAt),
      );
    if (closed.length < 2) {
      return null;
    }
    let cumulative = 0;
    const values: number[] = [];
    for (const trade of closed) {
      cumulative += paperTradeRealizedPnl(trade) ?? trade.pnl ?? 0;
      values.push(cumulative);
    }
    const min = Math.min(...values, 0);
    const max = Math.max(...values, 0);
    const span = max - min || 1;
    const width = 560;
    const height = 120;
    const step = values.length > 1 ? width / (values.length - 1) : width;
    const points = values.map((value, index) => {
      const x = index * step;
      const y = height - ((value - min) / span) * (height - 8) - 4;
      return `${x.toFixed(1)},${y.toFixed(1)}`;
    });
    const linePath = `M ${points.join(' L ')}`;
    const areaPath = `${linePath} L ${width} ${height} L 0 ${height} Z`;
    return { linePath, areaPath, positive: cumulative >= 0 };
  }

  setOutcomeFilter(filter: 'all' | 'positive' | 'negative'): void {
    this.outcomeFilter = filter;
    this.page = 1;
    this.cdr.markForCheck();
  }

  setPage(page: number): void {
    this.page = Math.min(Math.max(1, page), this.pageCount());
    this.cdr.markForCheck();
  }

  tradePnlMismatchHint(trade: PaperTradeHistoryItem): string | null {
    const computed = paperTradeRealizedPnl(trade);
    if (trade.pnl == null || computed == null) {
      return null;
    }
    if (Math.abs(trade.pnl - computed) <= 1) {
      return null;
    }
    return `P&L from entry/exit × qty (stored total was ₹${trade.pnl.toFixed(2)})`;
  }

  onClose(): void {
    this.closed.emit();
  }

  private resetList(): void {
    this.page = 1;
    this.outcomeFilter = 'all';
    this.cdr.markForCheck();
  }
}
