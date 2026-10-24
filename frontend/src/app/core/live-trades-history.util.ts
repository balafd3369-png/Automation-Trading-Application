import { BrokerPosition, LiveTrade, PaperTradeHistoryItem } from './board.models';
import { paperTradeRealizedPnl } from './trading-ui.util';

function mapLiveStatus(status: string): PaperTradeHistoryItem['status'] {
  const normalized = status.trim().toUpperCase();
  if (normalized === 'CLOSED') {
    return 'CLOSED';
  }
  if (normalized.includes('FAIL') || normalized === 'REJECTED') {
    return 'REJECTED';
  }
  if (normalized === 'CANCELLED') {
    return 'CANCELLED';
  }
  return 'OPEN';
}

function liveTradePnl(trade: LiveTrade, brokerPositions: BrokerPosition[]): number | null {
  const symbols = new Set(trade.orders.map((order) => order.symbol));
  const rows = brokerPositions.filter((row) => symbols.has(row.tradingSymbol) && row.quantity !== 0);
  if (!rows.length) {
    return null;
  }
  return rows.reduce((sum, row) => sum + row.pnl, 0);
}

function liveEntryQtyPrice(trade: LiveTrade): { qty: number; entry: number } {
  const entryOrders = trade.orders.filter((order) => order.filledQuantity > 0 && order.averagePrice > 0);
  if (!entryOrders.length) {
    return { qty: 0, entry: 0 };
  }
  let qty = 0;
  let value = 0;
  for (const order of entryOrders) {
    qty += order.filledQuantity;
    value += order.averagePrice * order.filledQuantity;
  }
  return { qty, entry: qty > 0 ? value / qty : 0 };
}

export function mapLiveTradesToHistoryItems(
  trades: LiveTrade[],
  brokerPositions: BrokerPosition[],
): PaperTradeHistoryItem[] {
  return trades.map((trade) => {
    const { qty, entry } = liveEntryQtyPrice(trade);
    const status = mapLiveStatus(trade.status);
    const pnl = liveTradePnl(trade, brokerPositions);
    return {
      id: `live:${trade.id}`,
      executionSource: 'LIVE',
      status,
      strategy: trade.strategy,
      underlying: trade.underlying,
      direction: 'LONG',
      stopLoss: 0,
      target: 0,
      maxRisk: 0,
      maxReward: 0,
      quantity: qty,
      entryPrice: entry > 0 ? entry : undefined,
      exitPrice: null,
      capitalRequired: entry > 0 && qty > 0 ? entry * qty : 0,
      pnl: pnl ?? (status === 'CLOSED' ? 0 : null),
      exitReason: trade.failureReason,
      entryReason: 'Live execution',
      createdAt: trade.createdAt,
      openedAt: trade.createdAt,
      closedAt: status === 'CLOSED' ? trade.createdAt : null,
      legs: trade.orders.map((order) => ({
        id: order.id,
        symbol: order.symbol,
        instrumentType: 'OPTION',
        side: order.transactionType,
        quantity: order.quantity,
        proposedPrice: order.averagePrice,
        entryPrice: order.averagePrice > 0 ? order.averagePrice : null,
        currentPrice: null,
        exitPrice: null,
        pnl: null,
        strike: null,
        optionRight: null,
        expiry: null,
      })),
    };
  });
}

export function realizedPnlTotal(trades: PaperTradeHistoryItem[]): number {
  let total = 0;
  for (const trade of trades) {
    if (trade.status !== 'CLOSED') {
      continue;
    }
    const pnl = paperTradeRealizedPnl(trade) ?? trade.pnl;
    if (pnl != null) {
      total += pnl;
    }
  }
  return total;
}
