import { ChartBar } from './board.models';

export type IndicatorKind = 'sma20' | 'sma50' | 'ema9' | 'ema21' | 'bb20' | 'rsi14';

export const CHART_INDICATOR_PREFS_KEY = 'twb-chart-indicators';

const INDICATOR_KINDS: IndicatorKind[] = ['sma20', 'sma50', 'ema9', 'ema21', 'bb20', 'rsi14'];

export function loadChartIndicatorPrefs(): Set<IndicatorKind> {
  try {
    const raw = localStorage.getItem(CHART_INDICATOR_PREFS_KEY);
    if (!raw) {
      return new Set<IndicatorKind>(['sma20', 'ema9']);
    }
    const parsed = JSON.parse(raw) as unknown;
    if (!Array.isArray(parsed)) {
      return new Set<IndicatorKind>(['sma20', 'ema9']);
    }
    const kinds = parsed.filter((k): k is IndicatorKind => INDICATOR_KINDS.includes(k as IndicatorKind));
    return kinds.length ? new Set(kinds) : new Set<IndicatorKind>(['sma20', 'ema9']);
  } catch {
    return new Set<IndicatorKind>(['sma20', 'ema9']);
  }
}

export function saveChartIndicatorPrefs(active: Set<IndicatorKind>): void {
  localStorage.setItem(CHART_INDICATOR_PREFS_KEY, JSON.stringify([...active].sort()));
}

export interface ChartDrawing {
  id: string;
  kind: 'hline' | 'trend';
  color: string;
  price?: number;
  from?: { time: number; value: number };
  to?: { time: number; value: number };
}

export function istDateTime(time: number, includeDate = true): string {
  const options: Intl.DateTimeFormatOptions = {
    timeZone: 'Asia/Kolkata',
    hour12: false,
    hour: '2-digit',
    minute: '2-digit',
  };
  if (includeDate) {
    options.day = '2-digit';
    options.month = 'short';
    options.year = 'numeric';
  }
  return new Intl.DateTimeFormat('en-IN', options).format(new Date(time * 1000));
}

export function istDate(time: number): string {
  return new Intl.DateTimeFormat('en-IN', {
    timeZone: 'Asia/Kolkata',
    day: '2-digit',
    month: 'short',
  }).format(new Date(time * 1000));
}

export function todayIstDateKey(): string {
  const parts = new Intl.DateTimeFormat('en-CA', {
    timeZone: 'Asia/Kolkata',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }).formatToParts(new Date());
  const value = (type: Intl.DateTimeFormatPartTypes) => parts.find((p) => p.type === type)?.value ?? '0';
  return `${value('year')}-${value('month')}-${value('day')}`;
}

export function countCandlesOnIstDate(candles: ChartBar[], dateKey: string): number {
  return candles.filter((c) => istParts(c.time).dateKey === dateKey).length;
}

export function istParts(time: number): { dateKey: string; hour: number; minute: number } {
  const parts = new Intl.DateTimeFormat('en-CA', {
    timeZone: 'Asia/Kolkata',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    hour12: false,
  }).formatToParts(new Date(time * 1000));
  const value = (type: Intl.DateTimeFormatPartTypes) => parts.find((p) => p.type === type)?.value ?? '0';
  return {
    dateKey: `${value('year')}-${value('month')}-${value('day')}`,
    hour: Number(value('hour')) % 24,
    minute: Number(value('minute')),
  };
}

export function isSessionOpen(time: number): boolean {
  const value = istParts(time);
  return value.hour === 9 && value.minute === 15;
}

/** Weekday in IST (Sat/Sun excluded). Exchange holidays are stripped on the server chart API. */
export function isIstTradingWeekday(timeSec: number): boolean {
  const weekday = new Intl.DateTimeFormat('en-US', {
    timeZone: 'Asia/Kolkata',
    weekday: 'short',
  }).format(new Date(timeSec * 1000));
  return weekday !== 'Sat' && weekday !== 'Sun';
}

/** NSE cash session: Mon–Fri + 09:15–15:30 IST (holidays filtered server-side on history). */
const MAX_INDEX_BOARD_DEVIATION = 0.035;
const MAX_STOCK_BOARD_DEVIATION = 0.12;

/** Prefer chart close when board LTP diverges (stale/corrupt broker quote). */
export function boardDisplayLast(
  boardLast: number,
  chartClose: number,
  isIndex: boolean,
  sessionOpen: boolean,
): number | null {
  if (boardLast <= 0 && chartClose <= 0) {
    return null;
  }
  if (!sessionOpen && chartClose > 0) {
    return chartClose;
  }
  if (boardLast <= 0) {
    return chartClose > 0 ? chartClose : null;
  }
  if (chartClose <= 0) {
    return boardLast;
  }
  const anchor = chartClose;
  const deviation = Math.abs(boardLast - anchor) / anchor;
  const max = isIndex ? MAX_INDEX_BOARD_DEVIATION : MAX_STOCK_BOARD_DEVIATION;
  return deviation > max ? chartClose : boardLast;
}

export function isIstCashSessionOpen(timeSec: number): boolean {
  if (!isIstTradingWeekday(timeSec)) {
    return false;
  }
  const value = istParts(timeSec);
  const minutes = value.hour * 60 + value.minute;
  const open = 9 * 60 + 15;
  const close = 15 * 60 + 30;
  return minutes >= open && minutes < close;
}

/** Start of the intraday bucket that contains `timeSec` (matches backend BucketStartIst). */
export function istSessionBucketStart(timeSec: number, stepMinutes: number): number {
  const parts = istParts(timeSec);
  const sessionStartMs = Date.parse(`${parts.dateKey}T09:15:00+05:30`);
  const timeMs = timeSec * 1000;
  const minutesFromSession = Math.max(0, Math.floor((timeMs - sessionStartMs) / 60_000));
  const bucketIndex = Math.floor(minutesFromSession / stepMinutes);
  return Math.floor((sessionStartMs + bucketIndex * stepMinutes * 60_000) / 1000);
}

export function sessionStartBars(candles: ChartBar[]): ChartBar[] {
  const sessions: ChartBar[] = [];
  let previousDate = '';
  for (const candle of candles) {
    const date = istParts(candle.time).dateKey;
    if (date !== previousDate) {
      sessions.push(candle);
      previousDate = date;
    }
  }
  return sessions;
}

export function movingAverage(
  candles: ChartBar[],
  period: number,
  kind: 'sma' | 'ema',
): Array<{ time: number; value: number }> {
  if (period <= 0 || candles.length < period) {
    return [];
  }
  const result: Array<{ time: number; value: number }> = [];
  if (kind === 'sma') {
    let sum = 0;
    for (let i = 0; i < candles.length; i++) {
      sum += candles[i].close;
      if (i >= period) {
        sum -= candles[i - period].close;
      }
      if (i >= period - 1) {
        result.push({ time: candles[i].time, value: sum / period });
      }
    }
    return result;
  }

  let value = candles.slice(0, period).reduce((sum, candle) => sum + candle.close, 0) / period;
  result.push({ time: candles[period - 1].time, value });
  const multiplier = 2 / (period + 1);
  for (let i = period; i < candles.length; i++) {
    value = (candles[i].close - value) * multiplier + value;
    result.push({ time: candles[i].time, value });
  }
  return result;
}

export interface BollingerBandPoint {
  time: number;
  middle: number;
  upper: number;
  lower: number;
}

/** Bollinger bands (SMA ± multiplier × σ) on close. */
export function bollingerBands(
  candles: ChartBar[],
  period = 20,
  multiplier = 2,
): BollingerBandPoint[] {
  if (period <= 1 || candles.length < period) {
    return [];
  }
  const result: BollingerBandPoint[] = [];
  for (let i = period - 1; i < candles.length; i++) {
    const window = candles.slice(i - period + 1, i + 1);
    const mean = window.reduce((sum, bar) => sum + bar.close, 0) / period;
    const variance = window.reduce((sum, bar) => sum + (bar.close - mean) ** 2, 0) / period;
    const std = Math.sqrt(variance);
    result.push({
      time: candles[i].time,
      middle: mean,
      upper: mean + multiplier * std,
      lower: mean - multiplier * std,
    });
  }
  return result;
}

/** Wilder RSI on close (0–100). */
export function relativeStrengthIndex(candles: ChartBar[], period = 14): Array<{ time: number; value: number }> {
  if (period <= 0 || candles.length <= period) {
    return [];
  }
  const result: Array<{ time: number; value: number }> = [];
  let avgGain = 0;
  let avgLoss = 0;
  for (let i = 1; i <= period; i++) {
    const change = candles[i].close - candles[i - 1].close;
    if (change >= 0) {
      avgGain += change;
    } else {
      avgLoss -= change;
    }
  }
  avgGain /= period;
  avgLoss /= period;
  const firstRs = avgLoss === 0 ? 100 : avgGain / avgLoss;
  result.push({ time: candles[period].time, value: 100 - 100 / (1 + firstRs) });

  for (let i = period + 1; i < candles.length; i++) {
    const change = candles[i].close - candles[i - 1].close;
    const gain = change > 0 ? change : 0;
    const loss = change < 0 ? -change : 0;
    avgGain = (avgGain * (period - 1) + gain) / period;
    avgLoss = (avgLoss * (period - 1) + loss) / period;
    const rs = avgLoss === 0 ? 100 : avgGain / avgLoss;
    result.push({ time: candles[i].time, value: 100 - 100 / (1 + rs) });
  }
  return result;
}

/** Extend a trend segment across the visible candle time range (ray). */
export function extendTrendSegment(
  from: { time: number; value: number },
  to: { time: number; value: number },
  timeMin: number,
  timeMax: number,
): Array<{ time: number; value: number }> {
  if (timeMin > timeMax) {
    return [];
  }
  if (from.time === to.time) {
    return [{ time: timeMin, value: from.value }, { time: timeMax, value: to.value }];
  }
  const slope = (to.value - from.value) / (to.time - from.time);
  const valueAt = (t: number) => from.value + slope * (t - from.time);
  return [
    { time: timeMin, value: valueAt(timeMin) },
    { time: timeMax, value: valueAt(timeMax) },
  ];
}

export function drawingStorageKey(symbol: string, interval: string): string {
  return `twb-chart-drawings:${symbol.toUpperCase()}:${interval}`;
}

export function parseDrawings(raw: string | null): ChartDrawing[] {
  if (!raw) {
    return [];
  }
  try {
    const value = JSON.parse(raw);
    if (!Array.isArray(value)) {
      return [];
    }
    return value.filter(
      (drawing): drawing is ChartDrawing =>
        drawing
        && typeof drawing.id === 'string'
        && (drawing.kind === 'hline' || drawing.kind === 'trend')
        && typeof drawing.color === 'string',
    );
  } catch {
    return [];
  }
}

/** Remove Sat/Sun intraday bars (phantom candles when the app still receives broker LTP). */
export function filterIntradayToTradingWeekdays(candles: ChartBar[]): ChartBar[] {
  return candles.filter((c) => isIstTradingWeekday(c.time));
}

/** Clamp corrupt API wicks (matches server ChartBarSanitizer). */
export function clampIntradayBarWicks(bar: ChartBar, interval = '5minute'): ChartBar {
  const open = bar.open > 0 ? bar.open : bar.close;
  const close = bar.close > 0 ? bar.close : open;
  const bodyHigh = Math.max(open, close);
  const bodyLow = Math.min(open, close);
  const mid = (bodyHigh + bodyLow) / 2;
  if (mid <= 0) {
    return bar;
  }
  const maxPct =
    interval === 'minute'
      ? 0.0035
      : interval === '15minute'
        ? 0.01
        : interval === '60minute'
          ? 0.02
          : interval === 'day'
            ? 0.05
            : 0.007;
  const limit = Math.max(mid * maxPct, 2);
  const high = Math.min(bar.high > 0 ? bar.high : bodyHigh, bodyHigh + limit);
  const low = Math.max(bar.low > 0 ? bar.low : bodyLow, bodyLow - limit);
  if (high <= low) {
    return { ...bar, open, close, high: bodyHigh + 0.05, low: bodyLow - 0.05 };
  }
  return { time: bar.time, open, high, low, close };
}

/** Calendar filter + OHLC normalize + wick clamp (no live LTP on closed sessions). */
export function sanitizeChartCandles(candles: ChartBar[], interval = '5minute'): ChartBar[] {
  return filterIntradayToTradingWeekdays(normalizeChartCandles(candles)).map((bar) =>
    clampIntradayBarWicks(bar, interval),
  );
}

/** Sort ascending, drop invalid OHLC, collapse duplicate timestamps (keep last). */
export function normalizeChartCandles(candles: ChartBar[]): ChartBar[] {
  const sorted = candles
    .filter((c) => c.open > 0 && c.high >= c.low && Number.isFinite(c.time))
    .sort((a, b) => a.time - b.time);
  const out: ChartBar[] = [];
  for (const candle of sorted) {
    const last = out.at(-1);
    if (last && last.time === candle.time) {
      out[out.length - 1] = candle;
    } else {
      out.push(candle);
    }
  }
  return out;
}

/**
 * Combine chart API payloads without letting a short "today only" response erase multi-day history.
 * Per-timestamp OHLC from `incoming` wins.
 */
function chartBarsSharePriceScale(cached: ChartBar[], incoming: ChartBar[]): boolean {
  const prevClose = cached.at(-1)?.close ?? 0;
  const nextClose = incoming[0]?.close ?? 0;
  if (prevClose <= 0 || nextClose <= 0) {
    return true;
  }
  const ratio = prevClose / nextClose;
  return ratio >= 0.25 && ratio <= 4;
}

export function mergeChartCandlesStable(cached: ChartBar[], incoming: ChartBar[]): ChartBar[] {
  const next = normalizeChartCandles(incoming);
  if (!cached.length) {
    return next;
  }
  const prev = normalizeChartCandles(cached);
  if (!next.length) {
    return prev;
  }
  if (!chartBarsSharePriceScale(prev, next)) {
    return next;
  }
  if (next.length >= prev.length && next[0].time <= prev[0].time) {
    return next;
  }
  const byTime = new Map<number, ChartBar>();
  for (const bar of prev) {
    byTime.set(bar.time, bar);
  }
  for (const bar of next) {
    byTime.set(bar.time, bar);
  }
  return normalizeChartCandles([...byTime.values()]);
}

/** True when `next` only extends or revises the tail of `previous` (same prefix times). */
export function chartCandlesTailOnlyChange(previous: ChartBar[], next: ChartBar[]): boolean {
  if (!previous.length || next.length < previous.length || next.length > previous.length + 1) {
    return false;
  }
  for (let i = 0; i < previous.length - 1; i++) {
    if (previous[i].time !== next[i].time) {
      return false;
    }
  }
  if (next.length === previous.length) {
    for (let i = 0; i < previous.length; i++) {
      if (previous[i].time !== next[i].time) {
        return false;
      }
    }
    return true;
  }
  for (let i = 0; i < previous.length; i++) {
    if (previous[i].time !== next[i].time) {
      return false;
    }
  }
  return next[next.length - 1].time > previous[previous.length - 1].time;
}

export function firstChangedCandle(previous: ChartBar[], next: ChartBar[]): number {
  if (previous.length === 0 || next.length < previous.length) {
    return 0;
  }
  for (let i = 0; i < next.length; i++) {
    const old = previous[i];
    const current = next[i];
    if (
      !old
      || old.time !== current.time
      || old.open !== current.open
      || old.high !== current.high
      || old.low !== current.low
      || old.close !== current.close
    ) {
      return i;
    }
  }
  return next.length;
}
