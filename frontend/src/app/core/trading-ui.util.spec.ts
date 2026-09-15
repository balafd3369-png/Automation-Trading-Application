import { SymbolSnapshot } from './board.models';
import { brokerConnectedFromFeed, syncLiveCandle, tone } from './trading-ui.util';

describe('trading-ui.util', () => {
  const row: SymbolSnapshot = {
    name: 'NIFTY',
    last: 23336.2,
    todayOpen: 23576.15,
    yesterdayHigh: 23600,
    yesterdayLow: 23200,
    highTwoDaysAgo: 23700,
    lowTwoDaysAgo: 23100,
    b2: 23650,
    b1: 23588.08,
    s: 23458.72,
    s1: 23388.08,
    s2: 23150,
    mark: 'SELLING',
    tightRange: false,
    previousClose: 23576.15,
  };

  it('marks broker connected only for live feeds', () => {
    expect(brokerConnectedFromFeed('m.Stock live')).toBeTrue();
    expect(brokerConnectedFromFeed('m.Stock ltp')).toBeTrue();
    expect(brokerConnectedFromFeed('awaiting m.Stock OTP')).toBeFalse();
    expect(brokerConnectedFromFeed('mock-1s')).toBeFalse();
  });

  it('syncs last candle close to live board price', () => {
    const nowSec = Math.floor(Date.now() / 1000);
    const candles = [{ time: nowSec - 60, open: 23500, high: 23520, low: 23480, close: 23335.45 }];
    const synced = syncLiveCandle(candles, 23336.2, '5minute');
    expect(synced).not.toBeNull();
    expect(synced!.bar.close).toBe(23336.2);
    expect(synced!.bar.high).toBe(23520);
    expect(synced!.bar.low).toBe(23336.2);
    expect(synced!.isNew).toBeFalse();
  });

  it('returns flat tone when last equals base', () => {
    const flatRow = { ...row, last: row.todayOpen, previousClose: row.todayOpen };
    expect(tone(flatRow)).toBe('flat');
  });

  it('returns down tone when last is below base', () => {
    expect(tone(row)).toBe('down');
  });

  it('returns up tone when last is above base', () => {
    const upRow = { ...row, last: row.todayOpen + 10 };
    expect(tone(upRow)).toBe('up');
  });
});
