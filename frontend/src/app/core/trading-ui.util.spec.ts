import { SymbolSnapshot } from './board.models';
import { istSessionBucketStart } from './chart-ui.util';
import { PaperTradeHistoryItem } from './board.models';
import { LiveExecutionSnapshot } from './board.models';
import {
  brokerConnectedFromFeed,
  brokerLoginRejectedMessage,
  brokerOtpSendSucceeded,
  filterExecutedPaperHistoryTrades,
  paperTradeCapitalUsed,
  preserveBrokerOtpEntry,
  shouldReplaceLiveAgent,
  syncLiveCandle,
  symbolHasVerifiedQuote,
  boardSymbolDisplayLast,
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

  it('treats invalid username or password as login rejection', () => {
    expect(brokerLoginRejectedMessage('Invalid username or password (3)')).toBeTrue();
    expect(brokerLoginRejectedMessage('m.Stock Type A API key is missing')).toBeTrue();
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

  it('does not synthesize a lone intraday bar after the previous session day', () => {
    const lastOct22 = Math.floor(Date.parse('2026-10-22T15:25:00+05:30') / 1000);
    const oct23Morning = Math.floor(Date.parse('2026-10-23T10:05:00+05:30') / 1000);
    const candles = [{ time: lastOct22, open: 22500, high: 22550, low: 22480, close: 22510 }];
    expect(syncLiveCandle(candles, 22492.1, '5minute', oct23Morning)).toBeNull();
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

  it('boardSymbolDisplayLast uses previous close instead of placeholder 100', () => {
    expect(
      boardSymbolDisplayLast({
        ...row,
        name: 'MARUTI',
        last: 12450,
        todayOpen: 100,
        yesterdayHigh: 12500,
        yesterdayLow: 12380,
        previousClose: 12450,
        quoteVerified: true,
        quoteQuality: 'VerifiedClose',
      }),
    ).toBe(12450);
  });

  it('symbolHasVerifiedQuote rejects unverified broker rows', () => {
    expect(
      symbolHasVerifiedQuote({
        ...row,
        last: 100,
        quoteVerified: false,
      }),
    ).toBeFalse();
    expect(symbolHasVerifiedQuote({ ...row, last: 612, quoteVerified: true })).toBeTrue();
  });

  it('filterExecutedPaperHistoryTrades drops non-executed statuses', () => {
    const base: PaperTradeHistoryItem = {
      id: 't1',
      underlying: 'NIFTY',
      strategy: 'zone-b1-break-b2',
      direction: 'BULLISH',
      pnl: null,
      openedAt: null,
      closedAt: null,
      createdAt: '2026-10-23T10:00:00+05:30',
      status: 'OPEN',
      maxRisk: 0,
      maxReward: 0,
      exitReason: null,
      legs: [],
    };
    const trades = [
      { ...base, id: 'open', status: 'OPEN' as const },
      { ...base, id: 'closed', status: 'CLOSED' as const, pnl: 100 },
      { ...base, id: 'exp', status: 'EXPIRED' as const },
      { ...base, id: 'rej', status: 'REJECTED' as const },
    ] as PaperTradeHistoryItem[];
    const kept = filterExecutedPaperHistoryTrades(trades);
    expect(kept.map((t) => t.id)).toEqual(['open', 'closed']);
  });

  it('paperTradeCapitalUsed uses backend capitalRequired and qty×entry', () => {
    const trade: PaperTradeHistoryItem = {
      id: 't1',
      status: 'CLOSED',
      strategy: 'bala-10am-high-fade',
      underlying: 'LT',
      direction: 'BEARISH',
      maxRisk: 500,
      maxReward: 1000,
      quantity: 175,
      entryPrice: 4000,
      capitalRequired: 700_000,
      pnl: 35_000,
      exitReason: 'hard stop',
      createdAt: '2026-10-23T11:46:00+05:30',
      openedAt: '2026-10-23T11:46:00+05:30',
      closedAt: '2026-10-23T12:17:00+05:30',
      legs: [
        {
          id: 'l1',
          symbol: 'LT',
          instrumentType: 'STOCK',
          side: 'SELL',
          quantity: 175,
          proposedPrice: 4000,
          entryPrice: 4000,
          currentPrice: null,
          exitPrice: 3800,
          pnl: null,
          strike: null,
          optionRight: null,
          expiry: null,
        },
      ],
    };
    expect(paperTradeCapitalUsed(trade)).toBe(700_000);
  });

  it('shouldReplaceLiveAgent detects broker PnL changes with same updatedAt', () => {
    const base: LiveExecutionSnapshot = {
      featureEnabled: true,
      brokerConnected: true,
      marketSession: { phase: 'OPEN', isOpen: true, message: '', nextEventAt: null },
      armed: false,
      armedUntil: null,
      zoneAutoLiveArmed: false,
      zoneAutoLiveProduct: 'MIS',
      state: 'IDLE',
      pendingProposal: null,
      trades: [],
      brokerPositions: [],
      brokerOrders: [],
      brokerPnl: { totalPnl: 100, realisedPnl: 0, unrealisedPnl: 100, unrealisedPercent: 1 },
      events: [],
      eodPrompts: [],
      reconciliationStatus: 'OK',
      updatedAt: '2026-10-24T10:00:00+05:30',
    };
    const moved = {
      ...base,
      brokerPnl: { totalPnl: 150, realisedPnl: 0, unrealisedPnl: 150, unrealisedPercent: 1.5 },
    };
    expect(shouldReplaceLiveAgent(moved, base)).toBe(true);
    expect(shouldReplaceLiveAgent(base, base)).toBe(false);
  });
});
