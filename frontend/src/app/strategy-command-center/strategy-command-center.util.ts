import {
  LiveExecutionEvent,
  LiveExecutionSnapshot,
  LiveTrade,
  PaperAgentSnapshot,
  PaperTradeEvent,
  PaperTradeHistoryItem,
  RankedDecision,
} from '../core/board.models';
import { ZoneStrategyRolloutRow } from '../core/strategies.models';
import {
  decisionMatchesCatalogStrategy,
  engineStrategyMatchesCatalog,
  isZoneCatalogStrategy,
  rolloutCodeForZoneCatalog,
} from '../core/strategy-engine.util';
import { paperTradeRealizedPnl, tradeMatchesCatalogStrategy } from '../core/trading-ui.util';

export type RolloutPhaseLabel = 'MONITOR ONLY' | 'PAPER AUTO' | 'ZONE AUTO ELIGIBLE' | 'DISABLED' | 'N/A';

export interface IndexRolloutRow {
  index: string;
  phase: RolloutPhaseLabel;
  detail: string;
}

export interface ActivityFeedItem {
  at: string;
  title: string;
  detail: string;
  tone: 'neutral' | 'positive' | 'negative' | 'warn';
}

export type RiskHealth = 'low' | 'moderate' | 'high';

export function rolloutPhaseLabel(phase: string, zoneAutoLiveArmed: boolean): RolloutPhaseLabel {
  switch (phase) {
    case 'paper_auto':
      return zoneAutoLiveArmed ? 'ZONE AUTO ELIGIBLE' : 'PAPER AUTO';
    case 'monitor':
      return 'MONITOR ONLY';
    case 'live_blocked':
      return 'DISABLED';
    default:
      return 'N/A';
  }
}

export function indexRolloutForStrategy(
  canonicalStrategyId: string,
  rows: ZoneStrategyRolloutRow[],
  zoneAutoLiveArmed: boolean,
): IndexRolloutRow[] {
  const code = rolloutCodeForZoneCatalog(canonicalStrategyId);
  const indices = ['NIFTY', 'BANKNIFTY', 'FINNIFTY', 'SENSEX'];
  if (!code) {
    return indices.map((index) => ({
      index,
      phase: 'N/A',
      detail: 'Rollout matrix applies to zone board strategies only.',
    }));
  }
  return indices.map((index) => {
    const row = rows.find(
      (r) => r.strategyCode === code && r.index.toUpperCase() === index,
    );
    if (!row) {
      return { index, phase: 'MONITOR ONLY' as RolloutPhaseLabel, detail: 'No rollout row.' };
    }
    return {
      index,
      phase: rolloutPhaseLabel(row.phase, zoneAutoLiveArmed),
      detail: row.reason,
    };
  });
}

export function filterDecisionsForStrategy(
  decisions: RankedDecision[],
  canonicalStrategyId: string,
): RankedDecision[] {
  return decisions
    .filter((d) => decisionMatchesCatalogStrategy(d, canonicalStrategyId))
    .sort((a, b) => Date.parse(b.evaluatedAt) - Date.parse(a.evaluatedAt));
}

export function filterTradesForStrategy(
  trades: PaperTradeHistoryItem[],
  canonicalStrategyId: string,
): PaperTradeHistoryItem[] {
  return trades.filter((t) => tradeMatchesCatalogStrategy(t, canonicalStrategyId));
}

export function filterLiveTradesForStrategy(trades: LiveTrade[], canonicalStrategyId: string): LiveTrade[] {
  return trades.filter((t) => engineStrategyMatchesCatalog(t.strategy, canonicalStrategyId));
}

export function computeRiskHealth(openRisk: number, maxDailyLoss: number, openCount: number, maxOpen: number): RiskHealth {
  if (maxDailyLoss <= 0) {
    return openCount >= maxOpen ? 'high' : 'moderate';
  }
  const riskRatio = openRisk / maxDailyLoss;
  const slotRatio = maxOpen > 0 ? openCount / maxOpen : 0;
  if (riskRatio >= 0.75 || slotRatio >= 1) {
    return 'high';
  }
  if (riskRatio >= 0.4 || slotRatio >= 0.66) {
    return 'moderate';
  }
  return 'low';
}

export function buildActivityFeed(
  canonicalStrategyId: string,
  paper: PaperAgentSnapshot | null,
  live: LiveExecutionSnapshot | null,
  trades: PaperTradeHistoryItem[],
): ActivityFeedItem[] {
  const items: ActivityFeedItem[] = [];

  for (const d of filterDecisionsForStrategy(paper?.recentDecisions ?? [], canonicalStrategyId).slice(0, 12)) {
    items.push({
      at: d.evaluatedAt,
      title: d.accepted ? 'Signal accepted' : 'Signal rejected',
      detail: `${d.direction} on ${d.underlying} · score ${d.evidenceScore.toFixed(0)}${
        d.rejections.length ? ` · ${d.rejections.join('; ')}` : ''
      }`,
      tone: d.accepted ? 'positive' : 'warn',
    });
  }

  for (const e of (paper?.recentEvents ?? []).slice(0, 20)) {
    const trade = trades.find((t) => t.id === e.tradeId);
    if (trade && !tradeMatchesCatalogStrategy(trade, canonicalStrategyId)) {
      continue;
    }
    if (!trade && e.tradeId) {
      continue;
    }
    const label = eventTypeLabel(e.type);
    items.push({
      at: e.at,
      title: label,
      detail: e.reason || (e.pnl != null ? `P&L ₹${e.pnl.toFixed(2)}` : ''),
      tone: e.pnl != null && e.pnl < 0 ? 'negative' : e.pnl != null && e.pnl > 0 ? 'positive' : 'neutral',
    });
  }

  for (const e of (live?.events ?? []).slice(0, 15)) {
    const trade = live?.trades.find((t) => t.id === e.tradeId);
    if (trade && !engineStrategyMatchesCatalog(trade.strategy, canonicalStrategyId)) {
      continue;
    }
    items.push({
      at: e.at,
      title: liveEventTitle(e.type),
      detail: e.message,
      tone: e.type.toLowerCase().includes('fail') ? 'negative' : 'neutral',
    });
  }

  return items
    .sort((a, b) => Date.parse(b.at) - Date.parse(a.at))
    .slice(0, 40);
}

function eventTypeLabel(type: string): string {
  const t = type.toUpperCase();
  if (t.includes('OPEN')) return 'Paper trade opened';
  if (t.includes('CLOSE')) return 'Paper trade closed';
  if (t.includes('PROPOS')) return 'Proposal updated';
  return type.replace(/_/g, ' ');
}

function liveEventTitle(type: string): string {
  const t = type.toLowerCase();
  if (t.includes('submit') || t.includes('order')) return 'Broker order update';
  if (t.includes('fill')) return 'Broker filled';
  if (t.includes('proposal')) return 'Live proposal';
  return type.replace(/_/g, ' ');
}

export function sumLiveRealizedPnl(trades: LiveTrade[], brokerPnl: LiveExecutionSnapshot['brokerPnl']): number {
  if (brokerPnl?.realisedPnl != null && trades.length === 0) {
    return brokerPnl.realisedPnl;
  }
  return trades.reduce((sum, t) => {
    const closed = t.status === 'CLOSED' || t.status === 'FAILED';
    if (!closed) {
      return sum;
    }
    return sum;
  }, brokerPnl?.realisedPnl ?? 0);
}

export function strategyPaperOpenRisk(
  paper: PaperAgentSnapshot | null,
  canonicalStrategyId: string,
): number {
  return (paper?.positions ?? [])
    .filter((p) => engineStrategyMatchesCatalog(p.strategy ?? '', canonicalStrategyId))
    .reduce((sum, p) => sum + (p.maxRisk ?? 0), 0);
}

export function closedTradesInEquityWindow(
  trades: PaperTradeHistoryItem[],
  window: 'today' | '1w' | '1m' | 'all',
): PaperTradeHistoryItem[] {
  const closed = trades.filter((t) => t.status === 'CLOSED');
  if (window === 'all') {
    return closed;
  }
  const now = Date.now();
  const dayMs = 86_400_000;
  const cut =
    window === 'today'
      ? now - dayMs
      : window === '1w'
        ? now - 7 * dayMs
        : now - 31 * dayMs;
  return closed.filter((t) => {
    const at = Date.parse(t.closedAt ?? t.openedAt ?? t.createdAt);
    return at >= cut;
  });
}

export function averageHoldMinutes(trades: PaperTradeHistoryItem[]): number | null {
  const spans: number[] = [];
  for (const t of trades) {
    if (t.status !== 'CLOSED' || !t.openedAt || !t.closedAt) {
      continue;
    }
    const ms = Date.parse(t.closedAt) - Date.parse(t.openedAt);
    if (ms > 0) {
      spans.push(ms / 60_000);
    }
  }
  if (!spans.length) {
    return null;
  }
  return spans.reduce((a, b) => a + b, 0) / spans.length;
}

export function netPnlForTrades(trades: PaperTradeHistoryItem[]): number {
  return trades.reduce((sum, t) => sum + (paperTradeRealizedPnl(t) ?? t.pnl ?? 0), 0);
}

export function zoneAutoEligibleBadge(
  canonicalStrategyId: string,
  rolloutRows: ZoneStrategyRolloutRow[],
  zoneAutoLiveArmed: boolean,
): boolean {
  if (!isZoneCatalogStrategy(canonicalStrategyId) || !zoneAutoLiveArmed) {
    return false;
  }
  const code = rolloutCodeForZoneCatalog(canonicalStrategyId);
  if (!code) {
    return false;
  }
  return rolloutRows.some((r) => r.strategyCode === code && r.phase === 'paper_auto');
}
