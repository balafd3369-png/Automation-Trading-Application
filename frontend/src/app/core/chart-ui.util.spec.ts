import { ChartBar } from './board.models';
import {
  drawingStorageKey,
  firstChangedCandle,
  istDateTime,
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
