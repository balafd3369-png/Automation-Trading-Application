import { ChartBar } from './board.models';

export type IndicatorKind = 'sma20' | 'sma50' | 'ema9' | 'ema21';

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

/** NSE cash session window for live intraday candle updates (09:15–15:30 IST). */
export function isIstCashSessionOpen(timeSec: number): boolean {
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
