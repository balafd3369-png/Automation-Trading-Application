import { PaperPosition, PaperTradeHistoryItem, PaperTradeHistoryLeg, SymbolSnapshot } from './board.models';
import { isIstCashSessionOpen, istSessionBucketStart } from './chart-ui.util';

export function chartStepSec(interval: string): number {
  switch (interval) {
    case 'minute':
      return 60;
    case '15minute':
      return 900;
    case '60minute':
      return 3600;
    case 'day':
      return 86400;
    default:
      return 300;
  }
}

export function syncLiveCandle(
  candles: Array<{ time: number; open: number; high: number; low: number; close: number }>,
  liveLast: number,
  interval = '5minute',
  nowSec = Math.floor(Date.now() / 1000),
): { bar: { time: number; open: number; high: number; low: number; close: number }; isNew: boolean } | null {
  if (!candles.length || liveLast <= 0) {
    return null;
  }

  if (!isIstCashSessionOpen(nowSec)) {
    return null;
  }

  const lastBar = candles[candles.length - 1];
  const stepMinutes = chartStepSec(interval) / 60;
  const currentBucket = istSessionBucketStart(nowSec, stepMinutes);
  const lastBucket = istSessionBucketStart(lastBar.time, stepMinutes);

  if (currentBucket > lastBucket) {
    return {
      isNew: true,
      bar: { time: currentBucket, open: liveLast, high: liveLast, low: liveLast, close: liveLast },
    };
  }

  return {
    isNew: false,
    bar: {
      time: lastBar.time,
      open: lastBar.open,
      high: Math.max(lastBar.high, liveLast),
      low: Math.min(lastBar.low, liveLast),
      close: liveLast,
    },
  };
}

export function brokerConnectedFromFeed(feed: string): boolean {
  const value = feed.trim().toLowerCase();
  return value.endsWith(' live') || value.endsWith(' ltp');
}

/** True when the OTP send API indicates the user should enter a code (HTTP 200 body). */
export function brokerOtpSendSucceeded(res: { status?: string; message?: string } | null | undefined): boolean {
  if (!res) {
    return false;
  }
  const status = (res.status ?? '').trim().toLowerCase();
  if (status === 'success' || status === 'ok') {
    return true;
  }
  const msg = (res.message ?? '').toLowerCase();
  if (!msg) {
    return false;
  }
  if (msg.includes('fail') || msg.includes('invalid') || msg.includes('error')) {
    return false;
  }
  return msg.includes('otp') || msg.includes('sent');
}

/** Board feed must not cancel an in-progress OTP entry (live board polls every second). */
export function preserveBrokerOtpEntry(feed: string, otpRequested: boolean, brokerBusy: boolean): boolean {
  return (otpRequested || brokerBusy) && feed.toLowerCase().includes('session expired');
}

export function tone(row: SymbolSnapshot | null | undefined): 'up' | 'down' | 'flat' {
  if (!row) {
    return 'flat';
  }
  const base = row.previousClose || row.todayOpen;
  if (!base) {
    return 'flat';
  }
  if (row.last > base) {
    return 'up';
  }
  if (row.last < base) {
    return 'down';
  }
  return 'flat';
}

export function isPaperTargetExit(exitReason: string | null | undefined): boolean {
  return !!exitReason && exitReason.toLowerCase().includes('target');
}

/** Runtime + catalog ids for strategies shown in Trade History (excludes legacy spreads / ad-hoc agent codes). */
const CATALOG_PAPER_STRATEGY_IDS = new Set(
  [
    'TEN_AM_HIGH_FADE_DAY_LOW',
    'TEN_AM_LOW_BREAK_DAY_OPEN',
    'STOCK_B1_REJECTION_TO_S2',
    'ZONE_B1_BREAK_B2',
    'ZONE_S1_BREAK_S2',
    'ZONE_S2_BOUNCE_B1',
    'bala-10am-high-fade',
    'ten-am-low-break-to-day-open',
    'stock-b1-rejection-to-s2',
    'zone-b1-break-b2',
    'zone-s1-break-s2',
    'zone-s2-bounce-b1',
  ].map((s) => s.toLowerCase()),
);

export function isCatalogPaperHistoryTrade(trade: PaperTradeHistoryItem): boolean {
  const key = (trade.strategy ?? '').trim().toLowerCase();
  return key.length > 0 && CATALOG_PAPER_STRATEGY_IDS.has(key);
}

export function filterCatalogPaperHistoryTrades(trades: PaperTradeHistoryItem[]): PaperTradeHistoryItem[] {
  return trades.filter(isCatalogPaperHistoryTrade);
}

const PAPER_HISTORY_STRATEGY_LABELS: Record<string, string> = {
  ten_am_low_break_day_open: 'Ten AM low break → day open',
  'ten-am-low-break-to-day-open': 'Ten AM low break → day open',
  ten_am_high_fade_day_low: 'Ten AM high fade → day low',
  'bala-10am-high-fade': 'Ten AM high fade → day low',
  stock_b1_rejection_to_s2: 'Stock B1 rejection → S2',
  'stock-b1-rejection-to-s2': 'Stock B1 rejection → S2',
  zone_b1_break_b2: 'Zone B1 break → B2',
  'zone-b1-break-b2': 'Zone B1 break → B2',
  zone_s1_break_s2: 'Zone S1 break → S2',
  'zone-s1-break-s2': 'Zone S1 break → S2',
  zone_s2_bounce_b1: 'Zone S2 bounce → B1',
  'zone-s2-bounce-b1': 'Zone S2 bounce → B1',
};

export interface PaperHistoryStrategyBreakdownRow {
  strategy: string;
  label: string;
  closed: number;
  wins: number;
  losses: number;
  open: number;
  realizedPnl: number;
}

export function formatPaperHistoryStrategyLabel(strategy: string): string {
  const key = strategy.trim().toLowerCase();
  if (!key) {
    return 'Unknown strategy';
  }
  const mapped = PAPER_HISTORY_STRATEGY_LABELS[key];
  if (mapped) {
    return mapped;
  }
  return strategy.replace(/_/g, ' ').replace(/-/g, ' ');
}

export function buildPaperHistoryStrategyBreakdown(
  trades: PaperTradeHistoryItem[],
): PaperHistoryStrategyBreakdownRow[] {
  const map = new Map<string, PaperHistoryStrategyBreakdownRow>();
  for (const trade of trades) {
    const strategy = (trade.strategy ?? '').trim() || 'Unknown';
    let row = map.get(strategy);
    if (!row) {
      row = {
        strategy,
        label: formatPaperHistoryStrategyLabel(strategy),
        closed: 0,
        wins: 0,
        losses: 0,
        open: 0,
        realizedPnl: 0,
      };
      map.set(strategy, row);
    }
    if (trade.status === 'OPEN') {
      row.open += 1;
      continue;
    }
    if (trade.status !== 'CLOSED') {
      continue;
    }
    row.closed += 1;
    const pnl = trade.pnl ?? 0;
    row.realizedPnl += pnl;
    if (pnl > 0) {
      row.wins += 1;
    } else if (pnl < 0) {
      row.losses += 1;
    }
  }
  return [...map.values()].sort((a, b) => b.realizedPnl - a.realizedPnl);
}

const IST_TIME_ZONE = 'Asia/Kolkata';

/** Calendar day in IST as `yyyy-MM-dd` (for date pickers and filters). */
export function istCalendarDayKeyFromIso(iso: string | null | undefined): string | null {
  if (!iso) {
    return null;
  }
  const ms = Date.parse(iso);
  if (!Number.isFinite(ms)) {
    return null;
  }
  return new Date(ms).toLocaleDateString('en-CA', { timeZone: IST_TIME_ZONE });
}

/** Day bucket for history: closed day, else opened, else created (all IST). */
export function paperTradeHistoryDayKey(trade: PaperTradeHistoryItem): string | null {
  const iso = trade.closedAt ?? trade.openedAt ?? trade.createdAt ?? null;
  return istCalendarDayKeyFromIso(iso);
}

export function paperTradeHistoryInIstDateRange(
  trade: PaperTradeHistoryItem,
  fromYmd: string,
  toYmd: string,
): boolean {
  const key = paperTradeHistoryDayKey(trade);
  if (!key) {
    return false;
  }
  return key >= fromYmd && key <= toYmd;
}

export function istTodayCalendarYmd(now = new Date()): string {
  return now.toLocaleDateString('en-CA', { timeZone: IST_TIME_ZONE });
}

export function shiftIstCalendarYmdByDays(ymd: string, deltaDays: number): string {
  const anchor = Date.parse(`${ymd}T12:00:00+05:30`);
  if (!Number.isFinite(anchor)) {
    return ymd;
  }
  const shifted = anchor + deltaDays * 86_400_000;
  return new Date(shifted).toLocaleDateString('en-CA', { timeZone: IST_TIME_ZONE });
}

export function shiftIstCalendarYmdByMonths(ymd: string, deltaMonths: number): string {
  const parts = ymd.split('-').map((part) => Number(part));
  if (parts.length !== 3 || parts.some((n) => !Number.isFinite(n))) {
    return ymd;
  }
  let year = parts[0];
  let monthIndex = parts[1] - 1 + deltaMonths;
  const day = parts[2];
  while (monthIndex < 0) {
    monthIndex += 12;
    year -= 1;
  }
  while (monthIndex > 11) {
    monthIndex -= 12;
    year += 1;
  }
  const lastDay = new Date(year, monthIndex + 1, 0).getDate();
  const clampedDay = Math.min(day, lastDay);
  const month = String(monthIndex + 1).padStart(2, '0');
  const dayText = String(clampedDay).padStart(2, '0');
  return `${year}-${month}-${dayText}`;
}

export function isPaperStopLossExit(exitReason: string | null | undefined): boolean {
  if (!exitReason) {
    return false;
  }
  const lower = exitReason.toLowerCase();
  return lower.includes('hard stop') || lower.includes('stop-loss');
}

/** Estimated margin/capital reserved when the trade was opened (live capital from risk engine, or leg notional fallback). */
function legEntryPrice(leg: PaperTradeHistoryLeg): number | null {
  const price = leg.entryPrice ?? leg.proposedPrice;
  return price != null && price > 0 ? price : null;
}

function legExitPrice(leg: PaperTradeHistoryLeg): number | null {
  const price = leg.exitPrice ?? leg.currentPrice;
  return price != null && price > 0 ? price : null;
}

/** Quantity-weighted average entry across legs (single-leg trades = that leg’s fill). */
export function paperTradeEntryPrice(trade: PaperTradeHistoryItem): number | null {
  const legs = trade.legs ?? [];
  if (!legs.length) {
    return null;
  }
  let qty = 0;
  let sum = 0;
  for (const leg of legs) {
    const entry = legEntryPrice(leg);
    if (entry == null) {
      continue;
    }
    sum += entry * leg.quantity;
    qty += leg.quantity;
  }
  return qty > 0 ? sum / qty : null;
}

/** Quantity-weighted average exit across legs when closed. */
export function paperTradeExitPrice(trade: PaperTradeHistoryItem): number | null {
  const legs = trade.legs ?? [];
  if (!legs.length) {
    return null;
  }
  let qty = 0;
  let sum = 0;
  for (const leg of legs) {
    const exit = legExitPrice(leg);
    if (exit == null) {
      continue;
    }
    sum += exit * leg.quantity;
    qty += leg.quantity;
  }
  return qty > 0 ? sum / qty : null;
}

export function paperTradeStopLoss(trade: PaperTradeHistoryItem): number | null {
  const stop = trade.stopLoss;
  return stop != null && stop > 0 ? stop : null;
}

export function paperTradeTarget(trade: PaperTradeHistoryItem): number | null {
  const target = trade.target;
  return target != null && target > 0 ? target : null;
}

export function paperTradeCapitalUsed(trade: PaperTradeHistoryItem): number {
  if (trade.capitalRequired != null && trade.capitalRequired > 0) {
    return trade.capitalRequired;
  }
  const legs = trade.legs ?? [];
  if (!legs.length) {
    return 0;
  }
  return legs.reduce((sum, leg) => {
    const price = leg.entryPrice ?? leg.proposedPrice ?? 0;
    return sum + Math.abs(price * leg.quantity);
  }, 0);
}

export function paperPositionCapitalUsed(
  position: PaperPosition,
  history?: PaperTradeHistoryItem[],
): number {
  if (position.capitalRequired != null && position.capitalRequired > 0) {
    return position.capitalRequired;
  }
  const fromHistory = history?.find((trade) => trade.id === position.id);
  if (fromHistory) {
    return paperTradeCapitalUsed(fromHistory);
  }
  if (position.legs?.length) {
    return position.legs.reduce((sum, leg) => sum + Math.abs(leg.entryPrice * leg.quantity), 0);
  }
  return Math.abs(position.entryPrice * position.quantity);
}

/** Map durable history OPEN rows to paper position cards when the live agent snapshot is empty or unavailable. */
export function paperPositionsFromHistory(trades: PaperTradeHistoryItem[]): PaperPosition[] {
  return trades
    .filter((t) => t.status === 'OPEN')
    .map((trade) => {
      const legs = trade.legs ?? [];
      const first = legs[0];
      const entry = first?.entryPrice ?? first?.proposedPrice ?? 0;
      const current = first?.currentPrice ?? entry;
      const qty = first?.quantity ?? 0;
      const legPnls = legs.map((leg) => leg.pnl ?? 0);
      const pnl =
        trade.pnl ??
        (legPnls.some((v) => v !== 0)
          ? legPnls.reduce((a, b) => a + b, 0)
          : legs.reduce((sum, leg) => {
              const ep = leg.entryPrice ?? leg.proposedPrice ?? 0;
              const cp = leg.currentPrice ?? ep;
              const q = leg.quantity ?? 0;
              const sign = leg.side === 'BUY' ? 1 : -1;
              return sum + sign * (cp - ep) * q;
            }, 0));
      return {
        id: trade.id,
        instrumentType: first?.instrumentType ?? 'STOCK',
        symbol: trade.underlying,
        direction: trade.direction as PaperPosition['direction'],
        triggerReason: trade.entryReason ?? 'Open paper trade (from history)',
        quantity: qty,
        entryPrice: entry,
        currentPrice: current,
        stopLoss: 0,
        target: 0,
        pnl,
        openedAt: trade.openedAt ?? trade.createdAt,
        strategy: trade.strategy,
        underlying: trade.underlying,
        maxRisk: trade.maxRisk,
        capitalRequired: trade.capitalRequired,
        legs: legs.map((leg) => ({
          id: leg.id,
          symbol: leg.symbol,
          side: leg.side,
          quantity: leg.quantity,
          entryPrice: leg.entryPrice ?? leg.proposedPrice ?? 0,
          currentPrice: leg.currentPrice ?? leg.entryPrice ?? leg.proposedPrice ?? 0,
          pnl: leg.pnl ?? 0,
          strike: leg.strike,
          optionRight: leg.optionRight,
          expiry: leg.expiry,
        })),
      } satisfies PaperPosition;
    });
}

export function mergePaperPositions(agent: PaperPosition[] | undefined, history: PaperTradeHistoryItem[]): PaperPosition[] {
  const live = agent ?? [];
  if (live.length > 0) {
    return live;
  }
  return paperPositionsFromHistory(history);
}
