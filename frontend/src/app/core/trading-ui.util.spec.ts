import { SymbolSnapshot } from './board.models';
import { istSessionBucketStart } from './chart-ui.util';
import {
  brokerConnectedFromFeed,
  brokerOtpSendSucceeded,
  preserveBrokerOtpEntry,
  syncLiveCandle,
  tone,
} from './trading-ui.util';

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

  it('accepts OTP send when API status or message indicates delivery', () => {
    expect(brokerOtpSendSucceeded({ status: 'success' })).toBeTrue();
    expect(brokerOtpSendSucceeded({ status: 'error', message: 'OTP sent to registered mobile' })).toBeTrue();
    expect(brokerOtpSendSucceeded({ status: 'error', message: 'Invalid credentials' })).toBeFalse();
  });

  it('keeps OTP entry active while board feed says session expired', () => {
    expect(preserveBrokerOtpEntry('session expired — request OTP', true, false)).toBeTrue();
    expect(preserveBrokerOtpEntry('session expired — request OTP', false, false)).toBeFalse();
  });

  it('marks broker connected only for live feeds', () => {
    expect(brokerConnectedFromFeed('m.Stock live')).toBeTrue();
    expect(brokerConnectedFromFeed('m.Stock ltp')).toBeTrue();
    expect(brokerConnectedFromFeed('awaiting broker login')).toBeFalse();
    expect(brokerConnectedFromFeed('mock-1s')).toBeFalse();
  });

  it('syncs last candle close to live board price', () => {
    const nowSec = Math.floor(Date.parse('2026-09-29T10:03:00+05:30') / 1000);
    const bucket = istSessionBucketStart(nowSec, 5);
    const candles = [{ time: bucket, open: 23500, high: 23520, low: 23480, close: 23335.45 }];
    const synced = syncLiveCandle(candles, 23336.2, '5minute', nowSec);
    expect(synced).not.toBeNull();
    expect(synced!.bar.close).toBe(23336.2);
    expect(synced!.bar.high).toBe(23520);
    expect(synced!.bar.low).toBe(23336.2);
    expect(synced!.isNew).toBeFalse();
  });

  it('does not append overnight candles before the next session open', () => {
    const lastSept29 = Math.floor(Date.parse('2026-09-29T15:25:00+05:30') / 1000);
    const midnightSept30 = Math.floor(Date.parse('2026-09-30T00:15:00+05:30') / 1000);
    const candles = [{ time: lastSept29, open: 25100, high: 25180, low: 25090, close: 25150 }];
    expect(syncLiveCandle(candles, 25180, '5minute', midnightSept30)).toBeNull();
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
