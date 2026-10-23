import { ChartBar } from './board.models';
import {
  drawingStorageKey,
  chartCandlesTailOnlyChange,
  firstChangedCandle,
  istDateTime,
  mergeChartCandlesStable,
  normalizeChartCandles,
  movingAverage,
  parseDrawings,
  sessionStartBars,
} from './chart-ui.util';

function bars(values: number[], start = Date.UTC(2026, 8, 15, 3, 45) / 1000): ChartBar[] {
  return values.map((close, index) => ({
    time: start + index * 60,
    open: close,
    high: close,
    low: close,
    close,
  }));
}

describe('chart UI utilities', () => {
  it('calculates SMA values at the matching candle timestamps', () => {
    const result = movingAverage(bars([10, 20, 30, 40]), 3, 'sma');
    expect(result).toEqual([
      { time: bars([10, 20, 30, 40])[2].time, value: 20 },
      { time: bars([10, 20, 30, 40])[3].time, value: 30 },
    ]);
  });

  it('seeds EMA with SMA and applies the EMA multiplier', () => {
    const result = movingAverage(bars([10, 20, 40, 50]), 3, 'ema');
    expect(result[0].value).toBeCloseTo(70 / 3, 8);
    expect(result[1].value).toBeCloseTo((50 - 70 / 3) * 0.5 + 70 / 3, 8);
  });

  it('formats a unix candle time in India Standard Time', () => {
    const time = Date.UTC(2026, 8, 15, 3, 45) / 1000;
    expect(istDateTime(time)).toContain('09:15');
    expect(istDateTime(time)).toContain('15');
  });

  it('returns one session marker per IST trading date', () => {
    const first = bars([1, 2]);
    const secondDay = bars([3], first[0].time + 86400);
    expect(sessionStartBars([...first, ...secondDay]).map((bar) => bar.time)).toEqual([
      first[0].time,
      secondDay[0].time,
    ]);
  });

  it('does not merge candles when price scale changes (symbol switch)', () => {
    const indexBars = bars([22_000, 22_100], 1_700_000_000);
    const stockBars = bars([865, 870], 1_700_000_000);
    const merged = mergeChartCandlesStable(indexBars, stockBars);
    expect(merged).toEqual(stockBars);
  });

  it('keeps multi-day history when a short today-only payload arrives', () => {
    const day1 = bars([10, 11, 12], 1_700_000_000);
    const day2Start = day1[day1.length - 1].time + 86400;
    const history = [...day1, ...bars([20, 21], day2Start)];
    const todayOnly = bars([22, 23, 24], day2Start + 3600);
    const merged = mergeChartCandlesStable(history, todayOnly);
    expect(merged.length).toBeGreaterThan(todayOnly.length);
    expect(merged[0].time).toBe(history[0].time);
    expect(merged.at(-1)?.close).toBe(24);
  });

  it('normalizes duplicate timestamps and sorts candles', () => {
    const bars = normalizeChartCandles([
      { time: 200, open: 2, high: 3, low: 1, close: 2 },
      { time: 100, open: 1, high: 2, low: 1, close: 1 },
      { time: 200, open: 2, high: 4, low: 1, close: 3 },
    ]);
    expect(bars.map((bar) => bar.time)).toEqual([100, 200]);
    expect(bars[1].close).toBe(3);
  });

  it('detects tail-only candle updates', () => {
    const previous = bars([10, 20]);
    const tailRevise = bars([10, 21]);
    const append = [...bars([10, 20]), bars([30], previous[1].time + 60)[0]];
    const shifted = bars([11, 21], previous[0].time + 120);
    expect(chartCandlesTailOnlyChange(previous, tailRevise)).toBeTrue();
    expect(chartCandlesTailOnlyChange(previous, append)).toBeTrue();
    expect(chartCandlesTailOnlyChange(previous, shifted)).toBeFalse();
  });

  it('finds the first changed candle without treating an append as a full reset', () => {
    const previous = bars([10, 20]);
    const updated = [...bars([10, 21]), bars([30], previous[1].time + 60)[0]];
    expect(firstChangedCandle(previous, updated)).toBe(1);
    expect(firstChangedCandle(previous, previous)).toBe(previous.length);
  });

  it('round-trips valid drawing storage and rejects malformed data', () => {
    const drawing = { id: 'one', kind: 'hline' as const, color: '#fff', price: 100 };
    expect(parseDrawings(JSON.stringify([drawing]))).toEqual([drawing]);
    expect(parseDrawings('{bad')).toEqual([]);
    expect(drawingStorageKey('nifty', '5minute')).toBe('twb-chart-drawings:NIFTY:5minute');
  });
});
