import { HttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { EMPTY, of } from 'rxjs';
import { AppComponent } from './app.component';
import { LiveExecutionSnapshot, PaperAgentSnapshot, PaperTradeHistoryResponse } from './core/board.models';
import { BoardService } from './core/board.service';

describe('AppComponent trade details', () => {
  const snapshot: PaperAgentSnapshot = {
    paperMode: true,
    running: true,
    state: 'AWAITING CONFIRMATION',
    brokerAvailableFunds: 100000,
    brokerUtilizedFunds: 0,
    config: { allocatedCapital: 100000, profitTarget: 1000, maxDailyLoss: 500, maxOpenPositions: 3 },
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
    performance: { closedTrades: 0, winningTrades: 0, losingTrades: 0, winRatePercent: 0, realizedPnl: 0, unrealizedPnl: 0, grossRiskOpen: 0 },
    marketContext: [],
  };

  const history: PaperTradeHistoryResponse = {
    userId: 'local-user',
    range: '1m',
    summary: { closedTrades: 0, winningTrades: 0, losingTrades: 0, openTrades: 0, realizedPnl: 0 },
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
    state: 'DISARMED',
    pendingProposal: snapshot.pendingTrade,
    trades: [],
    brokerPositions: [],
    events: [],
    eodPrompts: [],
    reconciliationStatus: 'HEALTHY',
    updatedAt: '2026-09-15T10:00:00Z',
  };

  it('renders regime, evidence score, bounded risk and every proposal leg', () => {
    const board = {
      watch: () => of({ counts: { buying: 0, selling: 0, rangeBound: 0 }, symbols: [], updatedAt: '', feed: 'test' }),
      paperAgent: () => of(snapshot),
      liveAgent: () => of(live),
      paperTrades: () => of(history),
      optionChain: () => EMPTY,
      sendPaperAgentCommand: () => of(snapshot),
    };
    TestBed.configureTestingModule({
      imports: [AppComponent],
      providers: [
        { provide: BoardService, useValue: board },
        { provide: HttpClient, useValue: { get: () => of({ connected: false }) } },
      ],
    });
    const fixture = TestBed.createComponent(AppComponent);
    fixture.detectChanges();

    fixture.componentInstance.showTradeDetails();
    fixture.detectChanges();
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';

    expect(text).toContain('Paper');
    expect(text).toContain('Live');
    expect(text).toContain('TRENDING');
    expect(text).toContain('VERTICAL_DEBIT_SPREAD');
    expect(text).toContain('80/100');
    expect(text).toContain('Max risk');
    expect(text).toContain('NIFTY 25000 CE');
    expect(text).toContain('NIFTY 25050 CE');

    fixture.componentInstance.setExecutionMode('live');
    fixture.detectChanges();
    const liveText = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(liveText).toContain('Arm live execution');
    expect(liveText).toContain('Confirm live MIS trade');
  });
});
