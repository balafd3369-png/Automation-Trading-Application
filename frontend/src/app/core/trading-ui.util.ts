import { PaperPosition, PaperTradeHistoryItem, PaperTradeHistoryLeg, SymbolSnapshot } from './board.models';
import { isIstCashSessionOpen, istParts, istSessionBucketStart } from './chart-ui.util';

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
    const stepSec = chartStepSec(interval);
    const gapSec = currentBucket - lastBar.time;
    const lastDate = istParts(lastBar.time).dateKey;
    const currentDate = istParts(currentBucket).dateKey;
    if (lastDate !== currentDate || gapSec > stepSec) {
      return null;
    }
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

/** Login step failed — do not prompt for OTP entry. */
export function brokerLoginRejectedMessage(message: string | null | undefined): boolean {
  const msg = (message ?? '').toLowerCase();
  if (!msg) {
    return false;
  }
  return msg.includes('invalid username') || msg.includes('invalid password') || msg.includes('attempts remaining');
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
  stock_b1_rejection_to_s2: 'Index B1 rejection → S2',
  'stock-b1-rejection-to-s2': 'Index B1 rejection → S2',
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

/** Canonical catalog ids (same order as backend StrategyCatalog.All()). */
export const CATALOG_STRATEGY_CANONICAL_IDS = [
  'zone-b1-break-b2',
  'zone-s1-break-s2',
  'zone-s2-bounce-b1',
  'stock-b1-rejection-to-s2',
  'bala-10am-high-fade',
  'ten-am-low-break-to-day-open',
] as const;

const CATALOG_STRATEGY_ALIAS_TO_CANONICAL: Record<string, string> = {
  zone_b1_break_b2: 'zone-b1-break-b2',
  'zone-b1-break-b2': 'zone-b1-break-b2',
  zone_s1_break_s2: 'zone-s1-break-s2',
  'zone-s1-break-s2': 'zone-s1-break-s2',
  zone_s2_bounce_b1: 'zone-s2-bounce-b1',
  'zone-s2-bounce-b1': 'zone-s2-bounce-b1',
  stock_b1_rejection_to_s2: 'stock-b1-rejection-to-s2',
  'stock-b1-rejection-to-s2': 'stock-b1-rejection-to-s2',
  ten_am_high_fade_day_low: 'bala-10am-high-fade',
  'bala-10am-high-fade': 'bala-10am-high-fade',
  ten_am_low_break_day_open: 'ten-am-low-break-to-day-open',
  'ten-am-low-break-to-day-open': 'ten-am-low-break-to-day-open',
};

export function normalizeCatalogStrategyId(strategy: string | null | undefined): string | null {
  const raw = (strategy ?? '').trim();
  if (!raw) {
    return null;
  }
  const key = raw.toLowerCase().replace(/-/g, '_');
  const canonical = CATALOG_STRATEGY_ALIAS_TO_CANONICAL[key] ?? CATALOG_STRATEGY_ALIAS_TO_CANONICAL[raw.toLowerCase()];
  if (canonical) {
    return canonical;
  }
  const kebab = raw.toLowerCase().replace(/_/g, '-');
  if ((CATALOG_STRATEGY_CANONICAL_IDS as readonly string[]).includes(kebab)) {
    return kebab;
  }
  return isCatalogPaperHistoryTrade({ strategy: raw } as PaperTradeHistoryItem) ? kebab : null;
}

export function tradeMatchesCatalogStrategy(trade: PaperTradeHistoryItem, canonicalStrategyId: string): boolean {
  return normalizeCatalogStrategyId(trade.strategy) === canonicalStrategyId;
}

export function buildPaperHistoryStrategyBreakdown(
  trades: PaperTradeHistoryItem[],
): PaperHistoryStrategyBreakdownRow[] {
  const map = new Map<string, PaperHistoryStrategyBreakdownRow>();
  for (const trade of trades) {
    const canonical =
      normalizeCatalogStrategyId(trade.strategy) ?? ((trade.strategy ?? '').trim() || 'Unknown');
    let row = map.get(canonical);
    if (!row) {
      row = {
        strategy: canonical,
        label: formatPaperHistoryStrategyLabel(canonical),
        closed: 0,
        wins: 0,
        losses: 0,
        open: 0,
        realizedPnl: 0,
      };
      map.set(canonical, row);
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

/** P&L table: every catalog strategy, including zero-trade rows (zone + Ten AM + stock). */
export function buildCatalogPaperHistoryStrategyBreakdown(
  trades: PaperTradeHistoryItem[],
): PaperHistoryStrategyBreakdownRow[] {
  const fromTrades = buildPaperHistoryStrategyBreakdown(trades);
  const byId = new Map(fromTrades.map((row) => [row.strategy, row]));
  const rows: PaperHistoryStrategyBreakdownRow[] = [];
  for (const id of CATALOG_STRATEGY_CANONICAL_IDS) {
    const existing = byId.get(id);
    if (existing) {
      rows.push(existing);
      continue;
    }
    rows.push({
      strategy: id,
      label: formatPaperHistoryStrategyLabel(id),
      closed: 0,
      wins: 0,
      losses: 0,
      open: 0,
      realizedPnl: 0,
    });
  }
  for (const row of fromTrades) {
    if (!(CATALOG_STRATEGY_CANONICAL_IDS as readonly string[]).includes(row.strategy)) {
      rows.push(row);
    }
  }
  return rows;
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

/** Human-readable IST calendar day from `yyyy-MM-dd`. */
export function formatIstCalendarDayLabel(ymd: string): string {
  const [y, m, d] = ymd.split('-').map((part) => Number(part));
  if (!y || !m || !d) {
    return ymd;
  }
  const utc = Date.UTC(y, m - 1, d, 5, 30);
  return new Date(utc).toLocaleDateString('en-IN', {
    day: '2-digit',
    month: 'short',
    year: 'numeric',
    timeZone: 'UTC',
  });
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

/** Reject mock/seed marks (e.g. ₹100) on equity legs whose entry is in thousands. */
export function paperTradePlausibleMark(entry: number, mark: number): boolean {
  if (mark <= 0 || entry <= 0) {
    return false;
  }
  if (entry <= 200) {
    return true;
  }
  if (mark <= 150 && entry / mark > 3) {
    return false;
  }
  return true;
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
    const entry = legEntryPrice(leg);
    if (exit == null || entry == null || !paperTradePlausibleMark(entry, exit)) {
      continue;
    }
    sum += exit * leg.quantity;
    qty += leg.quantity;
  }
  if (qty > 0) {
    return sum / qty;
  }
  const entry = paperTradeEntryPrice(trade);
  const reason = trade.exitReason?.toLowerCase() ?? '';
  if (entry != null && trade.stopLoss && trade.stopLoss > 0 && reason.includes('stop')) {
    return paperTradePlausibleMark(entry, trade.stopLoss) ? trade.stopLoss : null;
  }
  if (entry != null && trade.target && trade.target > 0 && reason.includes('target')) {
    return paperTradePlausibleMark(entry, trade.target) ? trade.target : null;
  }
  return null;
}

/** Capital deployed at entry: Σ (qty × entry price) per leg. */
export function paperTradeCapitalPrice(trade: PaperTradeHistoryItem): number {
  return paperTradeEntryNotional(trade);
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

export function paperTradeExecutedAt(trade: PaperTradeHistoryItem): string | null {
  if (trade.openedAt) {
    return trade.openedAt;
  }
  if (trade.status === 'OPEN' || trade.status === 'CLOSED') {
    return trade.createdAt;
  }
  return null;
}

export function paperTradeClosedAt(trade: PaperTradeHistoryItem): string | null {
  return trade.closedAt;
}

export function paperTradeQuantityTotal(trade: PaperTradeHistoryItem): number {
  return (trade.legs ?? []).reduce((sum, leg) => sum + (leg.quantity ?? 0), 0);
}

export function paperTradeQuantityLabel(trade: PaperTradeHistoryItem): string {
  const legs = trade.legs ?? [];
  if (!legs.length) {
    return '—';
  }
  if (legs.length === 1) {
    return String(legs[0].quantity);
  }
  return String(paperTradeQuantityTotal(trade));
}

export function paperTradeQuantityTitle(trade: PaperTradeHistoryItem): string {
  const legs = trade.legs ?? [];
  if (!legs.length) {
    return '';
  }
  return legs.map((leg) => `${leg.side} ${leg.symbol} × ${leg.quantity}`).join(' · ');
}

export function paperTradeDirectionShort(direction: string): string {
  if (direction === 'BULLISH' || direction === 'LONG') {
    return 'Bull';
  }
  if (direction === 'BEARISH' || direction === 'SHORT') {
    return 'Bear';
  }
  return direction.length > 5 ? direction.slice(0, 5) : direction;
}

export function paperTradeLegPnl(leg: PaperTradeHistoryLeg): number | null {
  const entry = legEntryPrice(leg);
  const rawExit = legExitPrice(leg);
  const exit =
    entry != null && rawExit != null && paperTradePlausibleMark(entry, rawExit) ? rawExit : null;
  if (entry == null || exit == null || entry <= 0 || exit <= 0) {
    if (leg.pnl != null && Number.isFinite(leg.pnl)) {
      return leg.pnl;
    }
    return null;
  }
  const move = leg.side === 'BUY' ? exit - entry : entry - exit;
  return move * leg.quantity;
}

/** P&L from leg fills (entry/exit × qty); falls back to trade-level pnl when fills are missing. */
export function paperTradeRealizedPnl(trade: PaperTradeHistoryItem): number | null {
  const legs = trade.legs ?? [];
  if (!legs.length) {
    return trade.pnl;
  }
  let sum = 0;
  let any = false;
  for (const leg of legs) {
    const legPnl = paperTradeLegPnl(leg);
    if (legPnl != null) {
      sum += legPnl;
      any = true;
    }
  }
  if (any) {
    return sum;
  }
  return trade.pnl;
}

/** Notional at entry (₹) — sum of |entry × qty| across legs. */
export function paperTradeEntryNotional(trade: PaperTradeHistoryItem): number {
  const legs = trade.legs ?? [];
  if (!legs.length) {
    return 0;
  }
  return legs.reduce((sum, leg) => {
    const entry = legEntryPrice(leg);
    if (entry == null) {
      return sum;
    }
    return sum + Math.abs(entry * leg.quantity);
  }, 0);
}

export function paperTradeDurationLabel(trade: PaperTradeHistoryItem): string | null {
  const start = paperTradeExecutedAt(trade);
  const end = trade.closedAt;
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

/** True when the board row may show LTP (broker-verified or offline mock). */
export function symbolHasVerifiedQuote(symbol: SymbolSnapshot | null | undefined): boolean {
  if (!symbol) {
    return false;
  }
  if (symbol.quoteVerified === false) {
    return false;
  }
  return symbol.last > 0;
}
