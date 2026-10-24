import { PaperTradeHistoryItem } from '../core/board.models';
import { buildTradeHistoryAnalytics, classifyPaperOutcome } from './trade-history-analytics.util';

function closedTrade(partial: Partial<PaperTradeHistoryItem>): PaperTradeHistoryItem {
  return {
    id: partial.id ?? 't1',
    status: partial.status ?? 'CLOSED',
    strategy: partial.strategy ?? 'TEN_AM_HIGH_FADE_DAY_LOW',
    underlying: partial.underlying ?? 'NIFTY',
    direction: partial.direction ?? 'BULLISH',
    maxRisk: 0,
    maxReward: 0,
    pnl: partial.pnl ?? 100,
    exitReason: partial.exitReason ?? 'Target hit',
    createdAt: partial.createdAt ?? '2026-10-01T04:00:00.000Z',
    openedAt: partial.openedAt ?? '2026-10-01T04:00:00.000Z',
    closedAt: partial.closedAt ?? '2026-10-01T05:00:00.000Z',
    legs: partial.legs ?? [],
    capitalRequired: partial.capitalRequired ?? 10000,
    quantity: partial.quantity ?? 10,
    entryPrice: partial.entryPrice ?? 1000,
  };
}

describe('trade-history-analytics.util', () => {
  it('classifies outcomes into badge kinds', () => {
    expect(classifyPaperOutcome('Target reached').kind).toBe('target');
    expect(classifyPaperOutcome('Hard stop triggered').kind).toBe('stop');
    expect(classifyPaperOutcome('Confirmation timed out').kind).toBe('timeout');
  });

  it('computes win rate and net PnL for closed trades', () => {
    const analytics = buildTradeHistoryAnalytics([
      closedTrade({ id: 'a', pnl: 200, closedAt: '2026-10-01T05:00:00.000Z' }),
      closedTrade({ id: 'b', pnl: -50, closedAt: '2026-10-02T05:00:00.000Z', exitReason: 'Hard stop' }),
    ]);
    expect(analytics.summary.closedTrades).toBe(2);
    expect(analytics.summary.netRealizedPnl).toBe(150);
    expect(analytics.summary.winRatePct).toBe(50);
  });
});
