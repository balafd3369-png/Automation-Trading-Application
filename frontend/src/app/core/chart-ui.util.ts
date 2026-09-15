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
