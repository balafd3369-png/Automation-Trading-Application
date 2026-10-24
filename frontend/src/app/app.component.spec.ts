import { HttpClient } from '@angular/common/http';
import { fakeAsync, TestBed, tick } from '@angular/core/testing';
import { provideRouter, Router, withHashLocation } from '@angular/router';
import { EMPTY, of } from 'rxjs';
import { AppComponent } from './app.component';
import { routes } from './app.routes';
import { LiveExecutionSnapshot, PaperAgentSnapshot, PaperTradeHistoryResponse } from './core/board.models';
import { BoardService } from './core/board.service';

describe('AppComponent trade details', () => {
  beforeEach(() => {
    localStorage.clear();
  });
  const snapshot: PaperAgentSnapshot = {
    paperMode: true,
    running: true,
    state: 'AWAITING CONFIRMATION',
    brokerAvailableFunds: 100000,
    brokerUtilizedFunds: 0,
    config: { allocatedCapital: 100000, profitTarget: 1000, maxDailyLoss: 500, maxOpenPositions: 3, autoAdmitTrades: false },
    realizedPnl: 0,
    unrealizedPnl: 0,
    totalPnl: 0,
    pendingTrade: {
      id: 'trade-1',
      instrumentType: 'MULTI_LEG',
      symbol: 'NIFTY',
      direction: 'LONG',
      quantity: 65,
      indicativePrice: 40,
      stopLoss: 20,
      target: 80,
      reason: 'trend and defined risk',
      expiresAt: '2026-09-15T10:01:00Z',
      strategy: 'VERTICAL_DEBIT_SPREAD',
      underlying: 'NIFTY',
      evidenceScore: 80,
      maxRisk: 2600,
      maxReward: 3900,
      rewardRisk: 1.5,
      evidence: ['trend=UP'],
      dataTimestamp: '2026-09-15T10:00:00Z',
      legs: [
        {
          id: 'leg-1',
          symbol: 'NIFTY 25000 CE',
          underlying: 'NIFTY',
          instrumentType: 'OPTION',
          side: 'BUY',
          ratio: 1,
          quantity: 65,
          indicativePrice: 100,
          strike: 25000,
          optionRight: 'CE',
          expiry: '2026-09-22T10:00:00Z',
          quote: null,
        },
        {
          id: 'leg-2',
          symbol: 'NIFTY 25050 CE',
          underlying: 'NIFTY',
          instrumentType: 'OPTION',
          side: 'SELL',
          ratio: 1,
          quantity: 65,
          indicativePrice: 60,
          strike: 25050,
          optionRight: 'CE',
          expiry: '2026-09-22T10:00:00Z',
          quote: null,
        },
      ],
    },
    positions: [],
    messages: [],
    updatedAt: '2026-09-15T10:00:00Z',
    marketRegime: { regime: 'TRENDING', dataTimestamp: '2026-09-15T10:00:00Z', isFresh: true, evidence: ['NIFTY:UP/TRENDING'] },
    recentDecisions: [
      {
        id: 'decision-1',
        strategy: 'VERTICAL_DEBIT_SPREAD',
        underlying: 'NIFTY',
        direction: 'BULLISH',
        evidenceScore: 80,
        accepted: true,
        evidence: ['trend=UP'],
        rejections: [],
        maxRisk: 2600,
        maxReward: 3900,
        rewardRisk: 1.5,
        evaluatedAt: '2026-09-15T10:00:00Z',
        scoreMeaning: 'deterministic rule evidence, not probability',
      },
    ],
    recentEvents: [],
    performance: { closedTrades: 0, winningTrades: 0, losingTrades: 0, winRatePercent: 0, realizedPnl: 0, unrealizedPnl: 0, grossRiskOpen: 0, targetHits: 0, stopLossHits: 0, trailingStopHits: 0, breakevenTrades: 0 },
    marketContext: [],
  };

  const history: PaperTradeHistoryResponse = {
    userId: 'local-user',
    range: '1m',
    summary: { closedTrades: 0, winningTrades: 0, losingTrades: 0, openTrades: 0, realizedPnl: 0, targetHits: 0, stopLossHits: 0, trailingStopHits: 0, breakevenTrades: 0 },
    trades: [],
  };

  const live: LiveExecutionSnapshot = {
    featureEnabled: true,
    brokerConnected: true,
    marketSession: {
      phase: 'OPEN',
      isOpen: true,
      message: 'Market is open for live entries until 15:10 IST.',
      nextEventAt: null,
    },
    armed: false,
    armedUntil: null,
    zoneAutoLiveArmed: false,
    zoneAutoLiveProduct: 'MIS',
    state: 'DISARMED',
    pendingProposal: snapshot.pendingTrade,
    trades: [],
    brokerPositions: [
      {
        exchange: 'NFO',
        tradingSymbol: 'NIFTY22SEP23400PE',
        product: 'MIS',
        quantity: 65,
        overnightQuantity: 0,
        averagePrice: 137.65,
        lastPrice: 143,
        pnl: 347.75,
      },
      {
        exchange: 'NFO',
        tradingSymbol: 'NIFTY22SEP23300PE',
        product: 'MIS',
        quantity: 0,
        overnightQuantity: 0,
        averagePrice: 0,
        lastPrice: 98.55,
        pnl: -263.25,
      },
    ],
    brokerOrders: [
      {
        orderId: '1',
        exchangeOrderId: '1',
        exchange: 'NFO',
        tradingSymbol: 'NIFTY22SEP23400PE',
        transactionType: 'BUY',
        product: 'MIS',
        orderType: 'LIMIT',
        status: 'COMPLETE',
        statusMessage: 'done',
        quantity: 65,
        filledQuantity: 65,
        pendingQuantity: 0,
        price: 137.65,
        triggerPrice: 0,
        averagePrice: 137.65,
        tag: '',
        updatedAt: '2026-09-15T10:00:00Z',
      },
    ],
    brokerPnl: {
      totalPnl: 84.5,
      realisedPnl: -263.25,
      unrealisedPnl: 347.75,
      unrealisedPercent: 3.89,
    },
    events: [],
    eodPrompts: [],
    reconciliationStatus: 'HEALTHY',
    updatedAt: '2026-09-15T10:00:00Z',
  };

  it('renders regime, evidence score, bounded risk and every proposal leg', fakeAsync(() => {
    const boardSymbol = {
      name: 'NIFTY',
      last: 24000,
      todayOpen: 23900,
      yesterdayHigh: 24100,
      yesterdayLow: 23800,
      highTwoDaysAgo: 24050,
      lowTwoDaysAgo: 23750,
      b2: 23900,
      b1: 23950,
      s: 24050,
      s1: 24100,
      s2: 24150,
      mark: 'RANGE' as const,
      tightRange: false,
      previousClose: 23980,
    };
    const boardPayload = {
      counts: { buying: 0, selling: 0, rangeBound: 0 },
      symbols: [boardSymbol],
      updatedAt: '',
      feed: 'test',
    };
    const board = {
      watch: () => of(boardPayload),
      fetchBoard: () => of(boardPayload),
      chart: () =>
        of({
          name: 'NIFTY',
          candles: [],
          last: 24000,
          b2: 23900,
          b1: 23950,
          s: 24050,
          s1: 24100,
          s2: 24150,
        }),
      paperAgent: () => of(snapshot),
      liveAgent: () => of(live),
      paperTrades: () => of(history),
      zoneRejectionBacktest: () => of({ result: { symbol: 'X', pattern: 'STOCK_B1_REJECTION_TO_S2', samples: 0, wins: 0, losses: 0, winRatePercent: 0, avgRewardPoints: 0, avgRiskPoints: 0 }, qualifies: false, minWinRate: 90, minSamples: 8 }),
      optionChain: () => EMPTY,
      sendPaperAgentCommand: () => of(snapshot),
      strategyFeed: () => of({ updatedAt: '', liveNow: [], strategies: [], monthlyStats: [] }),
      strategyTrackRecord: () =>
        of({
          monthLabel: 'Test',
          from: '',
          to: '',
          overall: {
            strategyId: '',
            strategyName: '',
            trades: 0,
            closedTrades: 0,
            openTrades: 0,
            winningTrades: 0,
            losingTrades: 0,
            winRatePercent: 0,
            realizedPnl: 0,
            totalMaxRisk: 0,
            totalMaxReward: 0,
            peakLiveCapital: 0,
          },
          byStrategy: [],
          trades: [],
        }),
      strategyRollout: () => of({ rows: [] }),
    };
    TestBed.configureTestingModule({
      imports: [AppComponent],
      providers: [
        provideRouter(routes, withHashLocation()),
        { provide: BoardService, useValue: board },
        {
          provide: HttpClient,
          useValue: {
            get: () => of({ connected: true, availableFunds: 100000, utilizedFunds: 0 }),
            post: () => of({}),
          },
        },
      ],
    });
    const fixture = TestBed.createComponent(AppComponent);
    const router = TestBed.inject(Router);
    fixture.detectChanges();
    tick();

    void router.navigateByUrl('/trades/paper');
    tick();
    fixture.detectChanges();

    const comp = fixture.componentInstance;
    comp.brokerConnected = true;
    tick();
    comp.paperAgent = snapshot;
    comp.liveAgent = live;
    fixture.detectChanges();
    tick();
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';

    expect(text).toContain('Paper Trading');
    expect(text).toContain('LIVE TRADING');
    expect(text).toContain('TRADE HISTORY');
    expect(text).toContain('AGENT');
    expect(text).toContain('TRENDING');
    expect(text).toContain('VERTICAL_DEBIT_SPREAD');
    expect(text).toContain('80/100');
    expect(text).toContain('Max risk');
    expect(text).toContain('NIFTY 25000 CE');
    expect(text).toContain('NIFTY 25050 CE');

    void router.navigateByUrl('/trades/live');
    tick();
    fixture.detectChanges();
    const liveText = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(liveText).toContain('Live Trading');
    expect(liveText).toContain('Live Positions (P&L)');
    expect(liveText).not.toContain('Recent decisions');
    expect(liveText).not.toContain('Realized P&L');
    expect(liveText).not.toContain('Arm real-money execution');
    comp.setLiveBookView('orders');
    comp.setLiveOrderFilter('executed');
    fixture.detectChanges();
    tick();
    const ordersText = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(ordersText).toContain('Executed (1)');
  }));
});
