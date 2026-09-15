import { SymbolSnapshot } from './board.models';

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
): { bar: { time: number; open: number; high: number; low: number; close: number }; isNew: boolean } | null {
  if (!candles.length || liveLast <= 0) {
    return null;
  }

  const lastBar = candles[candles.length - 1];
  const step = chartStepSec(interval);
  const nowSec = Math.floor(Date.now() / 1000);
  if (nowSec >= lastBar.time + step) {
    const time = lastBar.time + step;
    return {
      isNew: true,
      bar: { time, open: liveLast, high: liveLast, low: liveLast, close: liveLast },
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
  return feed === 'm.Stock live' || feed === 'm.Stock ltp';
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
