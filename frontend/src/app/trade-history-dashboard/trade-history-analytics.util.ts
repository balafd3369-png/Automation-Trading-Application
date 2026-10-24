import { PaperTradeHistoryItem } from '../core/board.models';
import {
  formatPaperHistoryStrategyLabel,
  isPaperStopLossExit,
  isPaperTargetExit,
  paperTradeCapitalUsed,
  paperTradeClosedAt,
  paperTradeExecutedAt,
  paperTradeExitPrice,
  paperTradeEntryPrice,
  paperTradeQuantityTotal,
  paperTradeRealizedPnl,
} from '../core/trading-ui.util';

export type OutcomeBadgeKind = 'target' | 'stop' | 'timeout' | 'manual' | 'agent' | 'other';

export interface OutcomeBadge {
  kind: OutcomeBadgeKind;
  label: string;
}

export interface PerformanceSummary {
  netRealizedPnl: number;
  winRatePct: number | null;
  profitFactor: number | null;
  closedTrades: number;
  openTrades: number;
  avgProfitPerTrade: number | null;
  avgLossPerTrade: number | null;
  bestTradePnl: number | null;
  worstTradePnl: number | null;
  wins: number;
  losses: number;
  breakeven: number;
}

export interface TrendDelta {
  pct: number | null;
  direction: 'up' | 'down' | 'flat';
}

export interface EquityPoint {
  dateKey: string;
  cumulativePnl: number;
}

export interface DailyPnlBar {
  dateKey: string;
  pnl: number;
}

export interface StrategyPerformanceRow {
  strategyId: string;
  label: string;
  trades: number;
  closed: number;
  winRatePct: number | null;
  netProfit: number;
  avgRoiPct: number | null;
  maxDrawdown: number;
}

export interface RiskAnalytics {
  maxDrawdown: number;
  consecutiveWins: number;
  consecutiveLosses: number;
  avgHoldingMinutes: number | null;
  riskRewardRatio: number | null;
  expectancy: number | null;
  sharpeRatio: number | null;
}

export interface AiInsightLine {
  text: string;
  tone: 'positive' | 'negative' | 'neutral';
}

export interface TradeHistoryAnalytics {
  summary: PerformanceSummary;
  summaryTrend: {
    netPnl: TrendDelta;
    winRate: TrendDelta;
    closed: TrendDelta;
  };
  equityCurve: EquityPoint[];
  dailyPnl: DailyPnlBar[];
  winLoss: { wins: number; losses: number; breakeven: number };
  strategies: StrategyPerformanceRow[];
  risk: RiskAnalytics;
  insights: AiInsightLine[];
  bestStrategyId: string | null;
  worstStrategyId: string | null;
}

export function classifyPaperOutcome(exitReason: string | null | undefined, status?: string): OutcomeBadge {
  const reason = (exitReason ?? '').trim();
  const lower = reason.toLowerCase();
  if (isPaperTargetExit(reason)) {
    return { kind: 'target', label: 'Target hit' };
  }
  if (isPaperStopLossExit(reason)) {
    return { kind: 'stop', label: 'Stop loss' };
  }
  if (lower.includes('confirmation') && lower.includes('timeout')) {
    return { kind: 'timeout', label: 'Confirmation timeout' };
  }
  if (lower.includes('manual') || lower.includes('user')) {
    return { kind: 'manual', label: 'Manual exit' };
  }
  if (lower.includes('agent') || lower.includes('eod') || lower.includes('session')) {
    return { kind: 'agent', label: 'Agent closed' };
  }
  if (status === 'EXPIRED' || lower.includes('expired') || lower.includes('timed out')) {
    return { kind: 'timeout', label: 'Confirmation timeout' };
  }
  if (!reason) {
    return { kind: 'other', label: '—' };
  }
  const short = reason.length > 28 ? `${reason.slice(0, 26)}…` : reason;
  return { kind: 'other', label: short };
}

function closedTrades(trades: PaperTradeHistoryItem[]): PaperTradeHistoryItem[] {
  return trades.filter((t) => t.status === 'CLOSED' && t.pnl != null);
}

function tradeCloseMs(trade: PaperTradeHistoryItem): number {
  return Date.parse(paperTradeClosedAt(trade) ?? trade.openedAt ?? trade.createdAt ?? '') || 0;
}

function istDateKey(iso: string | null | undefined): string | null {
  if (!iso) {
    return null;
  }
  const d = new Date(iso);
  if (!Number.isFinite(d.getTime())) {
    return null;
  }
  const parts = new Intl.DateTimeFormat('en-CA', {
    timeZone: 'Asia/Kolkata',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }).formatToParts(d);
  const y = parts.find((p) => p.type === 'year')?.value;
  const m = parts.find((p) => p.type === 'month')?.value;
  const day = parts.find((p) => p.type === 'day')?.value;
  return y && m && day ? `${y}-${m}-${day}` : null;
}

function profitFactor(wins: number[], losses: number[]): number | null {
  const grossWin = wins.reduce((s, v) => s + v, 0);
  const grossLoss = Math.abs(losses.reduce((s, v) => s + v, 0));
  if (grossLoss === 0) {
    return grossWin > 0 ? null : null;
  }
  return grossWin / grossLoss;
}

function trendDelta(current: number, previous: number): TrendDelta {
  if (!Number.isFinite(current) || !Number.isFinite(previous)) {
    return { pct: null, direction: 'flat' };
  }
  if (previous === 0) {
    if (current === 0) {
      return { pct: 0, direction: 'flat' };
    }
    return { pct: null, direction: current > 0 ? 'up' : 'down' };
  }
  const pct = ((current - previous) / Math.abs(previous)) * 100;
  return {
    pct,
    direction: pct > 0.5 ? 'up' : pct < -0.5 ? 'down' : 'flat',
  };
}

function splitEarlierLater(closed: PaperTradeHistoryItem[]): { earlier: PaperTradeHistoryItem[]; later: PaperTradeHistoryItem[] } {
  if (closed.length < 4) {
    return { earlier: closed, later: [] };
  }
  const sorted = [...closed].sort((a, b) => tradeCloseMs(a) - tradeCloseMs(b));
  const mid = Math.floor(sorted.length / 2);
  return { earlier: sorted.slice(0, mid), later: sorted.slice(mid) };
}

function summarizeTrades(trades: PaperTradeHistoryItem[]): PerformanceSummary {
  const closed = closedTrades(trades);
  const open = trades.filter((t) => t.status === 'OPEN');
  const pnls = closed.map((t) => t.pnl ?? 0);
  const wins = pnls.filter((p) => p > 0);
  const losses = pnls.filter((p) => p < 0);
  const breakeven = pnls.filter((p) => p === 0).length;
  const net = pnls.reduce((s, p) => s + p, 0);
  const winRate = closed.length ? (wins.length / closed.length) * 100 : null;
  const pf = profitFactor(wins, losses);
  const avgWin = wins.length ? wins.reduce((s, v) => s + v, 0) / wins.length : null;
  const avgLoss = losses.length ? losses.reduce((s, v) => s + v, 0) / losses.length : null;
  return {
    netRealizedPnl: net,
    winRatePct: winRate,
    profitFactor: pf,
    closedTrades: closed.length,
    openTrades: open.length,
    avgProfitPerTrade: avgWin,
    avgLossPerTrade: avgLoss,
    bestTradePnl: pnls.length ? Math.max(...pnls) : null,
    worstTradePnl: pnls.length ? Math.min(...pnls) : null,
    wins: wins.length,
    losses: losses.length,
    breakeven,
  };
}

function buildEquityAndDaily(closed: PaperTradeHistoryItem[]): { equity: EquityPoint[]; daily: DailyPnlBar[] } {
  const byDay = new Map<string, number>();
  const sorted = [...closed].sort((a, b) => tradeCloseMs(a) - tradeCloseMs(b));
  for (const t of sorted) {
    const key = istDateKey(paperTradeClosedAt(t));
    if (!key) {
      continue;
    }
    byDay.set(key, (byDay.get(key) ?? 0) + (t.pnl ?? 0));
  }
  const daily: DailyPnlBar[] = [...byDay.entries()]
    .sort(([a], [b]) => a.localeCompare(b))
    .map(([dateKey, pnl]) => ({ dateKey, pnl }));
  let cum = 0;
  const equity: EquityPoint[] = daily.map((d) => {
    cum += d.pnl;
    return { dateKey: d.dateKey, cumulativePnl: cum };
  });
  return { equity, daily };
}

function strategyRows(trades: PaperTradeHistoryItem[]): StrategyPerformanceRow[] {
  const map = new Map<string, PaperTradeHistoryItem[]>();
  for (const t of trades) {
    const id = (t.strategy ?? 'unknown').trim();
    const list = map.get(id) ?? [];
    list.push(t);
    map.set(id, list);
  }
  const rows: StrategyPerformanceRow[] = [];
  for (const [strategyId, list] of map) {
    const closed = closedTrades(list);
    const pnls = closed.map((t) => t.pnl ?? 0);
    const wins = pnls.filter((p) => p > 0).length;
    const net = pnls.reduce((s, p) => s + p, 0);
    const rois: number[] = [];
    for (const t of closed) {
      const cap = paperTradeCapitalUsed(t);
      const pnl = t.pnl ?? 0;
      if (cap > 0) {
        rois.push((pnl / cap) * 100);
      }
    }
    const { equity } = buildEquityAndDaily(closed);
    let peak = 0;
    let maxDd = 0;
    for (const pt of equity) {
      peak = Math.max(peak, pt.cumulativePnl);
      maxDd = Math.min(maxDd, pt.cumulativePnl - peak);
    }
    rows.push({
      strategyId,
      label: formatPaperHistoryStrategyLabel(strategyId),
      trades: list.length,
      closed: closed.length,
      winRatePct: closed.length ? (wins / closed.length) * 100 : null,
      netProfit: net,
      avgRoiPct: rois.length ? rois.reduce((s, r) => s + r, 0) / rois.length : null,
      maxDrawdown: maxDd,
    });
  }
  return rows.sort((a, b) => b.netProfit - a.netProfit);
}

function consecutiveStreaks(closed: PaperTradeHistoryItem[]): { wins: number; losses: number } {
  const sorted = [...closed].sort((a, b) => tradeCloseMs(a) - tradeCloseMs(b));
  let bestWin = 0;
  let bestLoss = 0;
  let curWin = 0;
  let curLoss = 0;
  for (const t of sorted) {
    const p = t.pnl ?? 0;
    if (p > 0) {
      curWin += 1;
      curLoss = 0;
      bestWin = Math.max(bestWin, curWin);
    } else if (p < 0) {
      curLoss += 1;
      curWin = 0;
      bestLoss = Math.max(bestLoss, curLoss);
    } else {
      curWin = 0;
      curLoss = 0;
    }
  }
  return { wins: bestWin, losses: bestLoss };
}

function riskBlock(closed: PaperTradeHistoryItem[]): RiskAnalytics {
  const { equity, daily } = buildEquityAndDaily(closed);
  let peak = 0;
  let maxDd = 0;
  for (const pt of equity) {
    peak = Math.max(peak, pt.cumulativePnl);
    maxDd = Math.min(maxDd, pt.cumulativePnl - peak);
  }
  const streaks = consecutiveStreaks(closed);
  const holding: number[] = [];
  const winPnls: number[] = [];
  const lossPnls: number[] = [];
  for (const t of closed) {
    const start = paperTradeExecutedAt(t);
    const end = paperTradeClosedAt(t);
    if (start && end) {
      const ms = Date.parse(end) - Date.parse(start);
      if (Number.isFinite(ms) && ms >= 0) {
        holding.push(ms / 60_000);
      }
    }
    const p = t.pnl ?? 0;
    if (p > 0) {
      winPnls.push(p);
    } else if (p < 0) {
      lossPnls.push(p);
    }
  }
  const avgWin = winPnls.length ? winPnls.reduce((s, v) => s + v, 0) / winPnls.length : null;
  const avgLoss = lossPnls.length ? Math.abs(lossPnls.reduce((s, v) => s + v, 0) / lossPnls.length) : null;
  const winRate = closed.length ? winPnls.length / closed.length : null;
  const lossRate = closed.length ? lossPnls.length / closed.length : null;
  const expectancy =
    winRate != null && lossRate != null && avgWin != null && avgLoss != null
      ? winRate * avgWin - lossRate * avgLoss
      : null;
  const rr = avgWin != null && avgLoss != null && avgLoss > 0 ? avgWin / avgLoss : null;

  const dailyReturns = daily.map((d) => d.pnl);
  let sharpe: number | null = null;
  if (dailyReturns.length >= 3) {
    const mean = dailyReturns.reduce((s, v) => s + v, 0) / dailyReturns.length;
    const variance =
      dailyReturns.reduce((s, v) => s + (v - mean) ** 2, 0) / (dailyReturns.length - 1);
    const stdev = Math.sqrt(variance);
    if (stdev > 0) {
      sharpe = (mean / stdev) * Math.sqrt(252);
    }
  }

  return {
    maxDrawdown: maxDd,
    consecutiveWins: streaks.wins,
    consecutiveLosses: streaks.losses,
    avgHoldingMinutes: holding.length ? holding.reduce((s, v) => s + v, 0) / holding.length : null,
    riskRewardRatio: rr,
    expectancy,
    sharpeRatio: sharpe,
  };
}

function buildInsights(
  trades: PaperTradeHistoryItem[],
  closed: PaperTradeHistoryItem[],
  strategies: StrategyPerformanceRow[],
  summary: PerformanceSummary,
): AiInsightLine[] {
  const lines: AiInsightLine[] = [];
  if (!closed.length) {
    lines.push({ text: 'No closed trades in this period — widen the date range or wait for fills.', tone: 'neutral' });
    return lines;
  }
  const { daily } = buildEquityAndDaily(closed);
  if (daily.length) {
    const bestDay = [...daily].sort((a, b) => b.pnl - a.pnl)[0];
    const worstDay = [...daily].sort((a, b) => a.pnl - b.pnl)[0];
    lines.push({
      text: `Best day: ${bestDay.dateKey} (+₹${Math.round(bestDay.pnl).toLocaleString('en-IN')}).`,
      tone: 'positive',
    });
    if (worstDay.pnl < 0) {
      lines.push({
        text: `Toughest day: ${worstDay.dateKey} (₹${Math.round(worstDay.pnl).toLocaleString('en-IN')}).`,
        tone: 'negative',
      });
    }
  }
  const bySymbol = new Map<string, number>();
  for (const t of closed) {
    const sym = t.underlying ?? '?';
    bySymbol.set(sym, (bySymbol.get(sym) ?? 0) + (t.pnl ?? 0));
  }
  const topSym = [...bySymbol.entries()].sort((a, b) => b[1] - a[1])[0];
  if (topSym && topSym[1] > 0) {
    lines.push({ text: `Most profitable symbol: ${topSym[0]} (₹${Math.round(topSym[1]).toLocaleString('en-IN')}).`, tone: 'positive' });
  }
  if (strategies.length) {
    const best = strategies[0];
    const totalProfit = summary.netRealizedPnl;
    if (totalProfit > 0 && best.netProfit > 0) {
      const share = Math.round((best.netProfit / totalProfit) * 100);
      lines.push({
        text: `${best.label} contributed ~${share}% of net profit in this range.`,
        tone: 'positive',
      });
    }
    const byWin = [...strategies].filter((s) => s.closed >= 3).sort((a, b) => (b.winRatePct ?? 0) - (a.winRatePct ?? 0))[0];
    if (byWin && byWin.winRatePct != null) {
      lines.push({
        text: `${byWin.label} leads on win rate (${byWin.winRatePct.toFixed(0)}% over ${byWin.closed} closes).`,
        tone: 'neutral',
      });
    }
  }
  if (summary.worstTradePnl != null && summary.worstTradePnl < 0) {
    lines.push({
      text: `Largest single loss: ₹${Math.abs(summary.worstTradePnl).toFixed(0)} — review sizing on repeat losers.`,
      tone: 'negative',
    });
  }
  const expired = trades.filter((t) => t.status === 'EXPIRED').length;
  if (expired > trades.length * 0.2 && trades.length >= 5) {
    lines.push({
      text: `${expired} proposals expired without fill — consider tighter entry confirmation or shorter windows.`,
      tone: 'neutral',
    });
  }
  return lines.slice(0, 6);
}

export function buildTradeHistoryAnalytics(trades: PaperTradeHistoryItem[]): TradeHistoryAnalytics {
  const closed = closedTrades(trades);
  const summary = summarizeTrades(trades);
  const { earlier, later } = splitEarlierLater(closed);
  const prevSummary = summarizeTrades([...earlier]);
  const lateSummary = summarizeTrades([...later]);
  const summaryTrend = {
    netPnl: trendDelta(lateSummary.netRealizedPnl, prevSummary.netRealizedPnl),
    winRate: trendDelta(lateSummary.winRatePct ?? 0, prevSummary.winRatePct ?? 0),
    closed: trendDelta(lateSummary.closedTrades, prevSummary.closedTrades),
  };
  const { equity, daily } = buildEquityAndDaily(closed);
  const strategies = strategyRows(trades);
  const bestStrategyId = strategies.length ? strategies[0].strategyId : null;
  const worstStrategyId = strategies.length ? strategies[strategies.length - 1].strategyId : null;
  return {
    summary,
    summaryTrend,
    equityCurve: equity,
    dailyPnl: daily,
    winLoss: { wins: summary.wins, losses: summary.losses, breakeven: summary.breakeven },
    strategies,
    risk: riskBlock(closed),
    insights: buildInsights(trades, closed, strategies, summary),
    bestStrategyId,
    worstStrategyId,
  };
}

export function tradeRoiPct(trade: PaperTradeHistoryItem): number | null {
  const cap = paperTradeCapitalUsed(trade);
  const pnl = paperTradeRealizedPnl(trade);
  if (cap <= 0 || pnl == null) {
    return null;
  }
  return (pnl / cap) * 100;
}

export function tradeGrossPnl(trade: PaperTradeHistoryItem): number | null {
  return paperTradeRealizedPnl(trade);
}

/** Paper ledger has no charge breakdown yet — net equals gross. */
export function tradeCharges(trade: PaperTradeHistoryItem): number {
  return 0;
}

export function tradeNetPnl(trade: PaperTradeHistoryItem): number | null {
  const gross = tradeGrossPnl(trade);
  if (gross == null) {
    return null;
  }
  return gross - tradeCharges(trade);
}

export function exportTradesCsv(rows: PaperTradeHistoryItem[]): string {
  const header = [
    'Symbol',
    'Direction',
    'EntryTime',
    'ExitTime',
    'EntryPrice',
    'ExitPrice',
    'Quantity',
    'Capital',
    'GrossPnl',
    'Charges',
    'NetPnl',
    'RoiPct',
    'Strategy',
    'Status',
    'Outcome',
  ];
  const esc = (v: string | number) => {
    const s = String(v);
    if (s.includes(',') || s.includes('"') || s.includes('\n')) {
      return `"${s.replace(/"/g, '""')}"`;
    }
    return s;
  };
  const lines = [header.join(',')];
  for (const t of rows) {
    const badge = classifyPaperOutcome(t.exitReason, t.status);
    lines.push(
      [
        t.underlying,
        t.direction,
        paperTradeExecutedAt(t) ?? '',
        paperTradeClosedAt(t) ?? '',
        paperTradeEntryPrice(t) ?? '',
        paperTradeExitPrice(t) ?? '',
        paperTradeQuantityTotal(t),
        paperTradeCapitalUsed(t),
        tradeGrossPnl(t) ?? '',
        tradeCharges(t),
        tradeNetPnl(t) ?? '',
        tradeRoiPct(t) ?? '',
        formatPaperHistoryStrategyLabel(t.strategy),
        t.status,
        badge.label,
      ]
        .map(esc)
        .join(','),
    );
  }
  return lines.join('\n');
}
