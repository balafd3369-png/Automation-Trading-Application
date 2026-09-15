import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { BoardResponse, ChartResponse } from './board.models';
import { BoardService } from './board.service';

describe('BoardService', () => {
  let service: BoardService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [BoardService, provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(BoardService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('loads board snapshot from /api/board', async () => {
    const payload: BoardResponse = {
      counts: { buying: 1, selling: 2, rangeBound: 3 },
      symbols: [
        {
          name: 'NIFTY',
          last: 23300,
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
        },
      ],
      updatedAt: '2026-09-15T05:00:00Z',
      feed: 'm.Stock live',
    };

    const boardPromise = firstValueFrom(service.watch());
    const req = httpMock.expectOne('/api/board');
    expect(req.request.method).toBe('GET');
    req.flush(payload);

    const board = await boardPromise;
    expect(board.symbols[0].name).toBe('NIFTY');
    expect(board.feed).toBe('m.Stock live');
  });

  it('loads chart data with interval query param', async () => {
    const payload: ChartResponse = {
      name: 'NIFTY',
      last: 23300,
      b2: 23650,
      b1: 23588.08,
      s: 23458.72,
      s1: 23388.08,
      s2: 23150,
      candles: [{ time: 1726386300, open: 23500, high: 23520, low: 23480, close: 23300 }],
    };

    const chartPromise = firstValueFrom(service.chart('NIFTY', '5minute'));
    const req = httpMock.expectOne((r) => r.url === '/api/board/chart/NIFTY' && r.params.get('interval') === '5minute');
    req.flush(payload);

    const chart = await chartPromise;
    expect(chart.name).toBe('NIFTY');
    expect(chart.candles.length).toBe(1);
    expect(chart.candles[0].close).toBe(23300);
  });

  it('loads three-month trade history', async () => {
    const historyPromise = firstValueFrom(service.tradeHistory('3m'));
    const req = httpMock.expectOne(
      (request) =>
        request.url === '/api/paper-agent/history' &&
        request.params.get('range') === '3m',
    );
    expect(req.request.method).toBe('GET');
    req.flush({
      userId: 'local-user',
      range: '3m',
      from: '2026-06-15T00:00:00Z',
      to: '2026-09-15T00:00:00Z',
      summary: {
        closedTrades: 1,
        winningTrades: 1,
        losingTrades: 0,
        openTrades: 0,
        realizedPnl: 500,
      },
      trades: [],
    });

    const history = await historyPromise;
    expect(history.summary.realizedPnl).toBe(500);
  });

  it('loads paper-agent snapshot with ranked decisions', async () => {
    const snapshotPromise = firstValueFrom(service.paperAgent());
    const req = httpMock.expectOne('/api/paper-agent');
    req.flush({
      paperMode: true,
      running: false,
      state: 'STOPPED',
      brokerAvailableFunds: null,
      brokerUtilizedFunds: null,
      config: null,
      realizedPnl: 0,
      unrealizedPnl: 0,
      totalPnl: 0,
      pendingTrade: null,
      positions: [],
      messages: [],
      updatedAt: '2026-09-15T10:00:00Z',
      marketRegime: { regime: 'TRENDING', dataTimestamp: '2026-09-15T10:00:00Z', isFresh: true, evidence: [] },
      recentDecisions: [{ id: 'd1', strategy: 'STOCK_ZONE_LONG', underlying: 'INFY', direction: 'BULLISH', evidenceScore: 75, accepted: true, evidence: ['trend=UP'], rejections: [], maxRisk: 500, maxReward: 1000, rewardRisk: 2, evaluatedAt: '2026-09-15T10:00:00Z', scoreMeaning: 'rule evidence' }],
      recentEvents: [],
      performance: { closedTrades: 0, winningTrades: 0, losingTrades: 0, winRatePercent: 0, realizedPnl: 0, unrealizedPnl: 0, grossRiskOpen: 0 },
      marketContext: [],
    });

    const snapshot = await snapshotPromise;
    expect(snapshot.recentDecisions?.[0].evidenceScore).toBe(75);
  });

  it('loads multi-leg paper trade history', async () => {
    const historyPromise = firstValueFrom(service.paperTrades('1m'));
    const req = httpMock.expectOne(
      (request) =>
        request.url === '/api/paper-agent/trades' &&
        request.params.get('range') === '1m',
    );
    req.flush({
      userId: 'local-user',
      range: '1m',
      summary: { closedTrades: 0, winningTrades: 0, losingTrades: 0, openTrades: 1, realizedPnl: 0 },
      trades: [{ id: 't1', status: 'OPEN', strategy: 'VERTICAL_DEBIT_SPREAD', underlying: 'NIFTY', direction: 'BULLISH', maxRisk: 2600, maxReward: 3900, pnl: null, exitReason: null, createdAt: '2026-09-15T10:00:00Z', openedAt: '2026-09-15T10:00:01Z', closedAt: null, legs: [] }],
    });

    const history = await historyPromise;
    expect(history.trades[0].strategy).toBe('VERTICAL_DEBIT_SPREAD');
  });
});
