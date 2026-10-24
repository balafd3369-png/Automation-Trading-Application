import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable, interval, of, startWith, exhaustMap, timeout } from 'rxjs';
import { environment } from '../../environments/environment';
import {
  BoardResponse,
  ChartResponse,
  SymbolSearchHit,
  SymbolSnapshot,
  LiveExecutionSnapshot,
  OptionMarketQuote,
  PaperAgentSnapshot,
  PaperTradeHistoryResponse,
  TradeHistoryRange,
  TradeHistoryResponse,
  ZoneRejectionBacktestResponse,
} from './board.models';
import {
  StrategyCatalogEntry,
  StrategyFeedResponse,
  StrategyTrackRecordResponse,
  ZoneStrategyRolloutResponse,
} from './strategies.models';
import {
  ManualExecutionMode,
  ManualPlaceOrderRequest,
  ManualTradingSnapshot,
} from './manual-trading.models';
import {
  OperationsBrokerHealth,
  OperationsBrokerPortfolio,
  OperationsEmergencyResult,
  OperationsJournalEntry,
  OperationsNotification,
  OperationsOrdersBook,
  OperationsPositionsBook,
  OperationsHoldingRow,
  OperationsRiskDashboard,
  OperationsTimelineEntry,
  OperationsAuditEntry,
  BrokerReconciliationDashboard,
  BrokerReconciliationHistoryEntry,
} from './operations.models';
import { AdminPlatformDashboard, UserAnalyticsSummary, UserDashboardSummary } from './user-dashboard.models';
import { TradingQuantityRules, TradingQuantityRulesQuery } from './trading-quantity.models';

@Injectable({ providedIn: 'root' })
export class BoardService {
  constructor(private readonly http: HttpClient) {}

  /** Single board fetch (bounded wait so a stuck backend does not pile up connections). */
  fetchBoard(): Observable<BoardResponse> {
    return this.http.get<BoardResponse>('/api/board').pipe(timeout(10_000));
  }

  searchSymbols(query: string, limit = 24): Observable<SymbolSearchHit[]> {
    const q = query.trim();
    if (!q) {
      return of([]);
    }
    return this.http
      .get<SymbolSearchHit[]>('/api/board/symbols/search', { params: { q, limit } })
      .pipe(timeout(8_000));
  }

  ensureSymbol(name: string): Observable<SymbolSnapshot> {
    return this.http
      .post<SymbolSnapshot>(`/api/board/symbols/${encodeURIComponent(name)}/ensure`, {})
      .pipe(timeout(12_000));
  }

  watch(pollMs = environment.boardPollMs): Observable<BoardResponse> {
    return interval(pollMs).pipe(
      startWith(0),
      exhaustMap(() => this.fetchBoard()),
    );
  }

  chart(
    name: string,
    interval = '5minute',
    depth: 'fast' | 'full' = 'full',
  ): Observable<ChartResponse> {
    const waitMs = depth === 'fast' ? 12_000 : 25_000;
    return this.http
      .get<ChartResponse>(`/api/board/chart/${encodeURIComponent(name)}`, {
        params: { interval, depth },
      })
      .pipe(timeout(waitMs));
  }

  optionChart(
    contract: { underlying: string; expiry: string; strike: number; side: string },
    interval = '5minute',
  ): Observable<ChartResponse> {
    return this.http.get<ChartResponse>('/api/board/option-chart', {
      params: {
        underlying: contract.underlying,
        expiry: contract.expiry,
        strike: contract.strike,
        side: contract.side,
        interval,
      },
    });
  }

  optionChain(underlying: string, expiry: string): Observable<OptionMarketQuote[]> {
    return this.http.get<OptionMarketQuote[]>('/api/board/option-chain', {
      params: { underlying, expiry },
    });
  }

  paperAgent(): Observable<PaperAgentSnapshot> {
    return this.http.get<PaperAgentSnapshot>('/api/paper-agent').pipe(timeout(10_000));
  }

  sendPaperAgentCommand(message: string): Observable<PaperAgentSnapshot> {
    return this.http.post<PaperAgentSnapshot>('/api/paper-agent/command', { message });
  }

  tradeHistory(range: TradeHistoryRange): Observable<TradeHistoryResponse> {
    return this.http.get<TradeHistoryResponse>('/api/paper-agent/history', {
      params: { range },
    });
  }

  paperTrades(
    range: TradeHistoryRange,
    istFrom?: string | null,
    istTo?: string | null,
  ): Observable<PaperTradeHistoryResponse> {
    const params: Record<string, string> = { range };
    if (istFrom) {
      params['from'] = istFrom;
      if (istTo) {
        params['to'] = istTo;
      }
    }
    return this.http.get<PaperTradeHistoryResponse>('/api/paper-agent/trades', { params });
  }

  zoneRejectionBacktest(name: string): Observable<ZoneRejectionBacktestResponse> {
    return this.http.get<ZoneRejectionBacktestResponse>(
      `/api/paper-agent/backtest/zone-rejection/${encodeURIComponent(name)}`,
    );
  }

  strategies(): Observable<StrategyCatalogEntry[]> {
    return this.http.get<StrategyCatalogEntry[]>('/api/strategies');
  }

  strategyFeed(): Observable<StrategyFeedResponse> {
    return this.http.get<StrategyFeedResponse>('/api/strategies/feed');
  }

  strategyTrackRecord(): Observable<StrategyTrackRecordResponse> {
    return this.http.get<StrategyTrackRecordResponse>('/api/paper-agent/strategy-track-record');
  }

  strategyRollout(): Observable<ZoneStrategyRolloutResponse> {
    return this.http.get<ZoneStrategyRolloutResponse>('/api/strategies/rollout');
  }

  takeStrategyPaperTrade(strategyId: string, underlying: string): Observable<PaperAgentSnapshot> {
    return this.http.post<PaperAgentSnapshot>('/api/paper-agent/take-strategy', {
      strategy: strategyId,
      underlying,
    });
  }

  strategy(id: string): Observable<StrategyCatalogEntry> {
    return this.http.get<StrategyCatalogEntry>(`/api/strategies/${encodeURIComponent(id)}`);
  }

  liveAgent(): Observable<LiveExecutionSnapshot> {
    return this.http.get<LiveExecutionSnapshot>('/api/live-agent').pipe(timeout(10_000));
  }

  armLive(acknowledgement: string): Observable<LiveExecutionSnapshot> {
    return this.http.post<LiveExecutionSnapshot>('/api/live-agent/arm', { acknowledgement });
  }

  disarmLive(): Observable<LiveExecutionSnapshot> {
    return this.http.post<LiveExecutionSnapshot>('/api/live-agent/disarm', {});
  }

  armZoneAutoLive(acknowledgement: string, product: 'MIS' | 'NRML'): Observable<LiveExecutionSnapshot> {
    return this.http.post<LiveExecutionSnapshot>('/api/live-agent/arm-zone-auto', { acknowledgement, product });
  }

  confirmLive(proposalId: string, product: 'MIS' | 'NRML'): Observable<LiveExecutionSnapshot> {
    return this.http.post<LiveExecutionSnapshot>('/api/live-agent/confirm', { proposalId, product });
  }

  exitLive(tradeId: string): Observable<LiveExecutionSnapshot> {
    return this.http.post<LiveExecutionSnapshot>('/api/live-agent/exit', { tradeId });
  }

  liveEod(tradeId: string, action: 'HOLD' | 'EXIT'): Observable<LiveExecutionSnapshot> {
    return this.http.post<LiveExecutionSnapshot>('/api/live-agent/eod', { tradeId, action });
  }

  manualTrading(mode: ManualExecutionMode = 'PAPER'): Observable<ManualTradingSnapshot> {
    return this.http
      .get<ManualTradingSnapshot>('/api/manual-trading', { params: { mode } })
      .pipe(timeout(12_000));
  }

  placeManualOrder(body: ManualPlaceOrderRequest): Observable<{ message: string; order: unknown }> {
    return this.http.post<{ message: string; order: unknown }>('/api/manual-trading/orders', body);
  }

  cancelManualOrder(orderId: string): Observable<{ message: string }> {
    return this.http.post<{ message: string }>('/api/manual-trading/orders/cancel', { orderId });
  }

  closeManualPosition(executionMode: ManualExecutionMode, tradeId: string): Observable<{ message: string }> {
    return this.http.post<{ message: string }>('/api/manual-trading/positions/close', {
      executionMode,
      tradeId,
    });
  }

  operationsRisk(): Observable<OperationsRiskDashboard> {
    return this.http.get<OperationsRiskDashboard>('/api/operations/risk').pipe(timeout(12_000));
  }

  operationsOrders(): Observable<OperationsOrdersBook> {
    return this.http.get<OperationsOrdersBook>('/api/operations/orders').pipe(timeout(12_000));
  }

  operationsPositions(): Observable<OperationsPositionsBook> {
    return this.http.get<OperationsPositionsBook>('/api/operations/positions').pipe(timeout(12_000));
  }

  operationsHoldings(): Observable<OperationsHoldingRow[]> {
    return this.http.get<OperationsHoldingRow[]>('/api/operations/holdings').pipe(timeout(12_000));
  }

  operationsJournal(params: Record<string, string>): Observable<OperationsJournalEntry[]> {
    return this.http.get<OperationsJournalEntry[]>('/api/operations/journal', { params }).pipe(timeout(15_000));
  }

  operationsTimeline(category?: string): Observable<OperationsTimelineEntry[]> {
    return this.http
      .get<OperationsTimelineEntry[]>('/api/operations/timeline', {
        params: category ? { category } : {},
      })
      .pipe(timeout(12_000));
  }

  operationsAudit(limit = 200): Observable<OperationsAuditEntry[]> {
    return this.http.get<OperationsAuditEntry[]>('/api/operations/audit', { params: { limit } }).pipe(timeout(12_000));
  }

  operationsNotifications(limit = 100, unreadOnly = false): Observable<OperationsNotification[]> {
    return this.http
      .get<OperationsNotification[]>('/api/operations/notifications', { params: { limit, unreadOnly } })
      .pipe(timeout(10_000));
  }

  markOperationsNotificationRead(id: string): Observable<{ status: string }> {
    return this.http.post<{ status: string }>(`/api/operations/notifications/${encodeURIComponent(id)}/read`, {});
  }

  operationsBrokerHealth(): Observable<OperationsBrokerHealth> {
    return this.http.get<OperationsBrokerHealth>('/api/operations/broker/health').pipe(timeout(10_000));
  }

  operationsBrokerPortfolio(): Observable<OperationsBrokerPortfolio> {
    return this.http.get<OperationsBrokerPortfolio>('/api/operations/broker/portfolio').pipe(timeout(12_000));
  }

  operationsEmergency(action: string, confirmation: string): Observable<OperationsEmergencyResult> {
    return this.http.post<OperationsEmergencyResult>('/api/operations/emergency', { action, confirmation });
  }

  operationsReconciliation(): Observable<BrokerReconciliationDashboard> {
    return this.http.get<BrokerReconciliationDashboard>('/api/operations/reconciliation').pipe(timeout(12_000));
  }

  operationsReconciliationHistory(limit = 50): Observable<BrokerReconciliationHistoryEntry[]> {
    return this.http
      .get<BrokerReconciliationHistoryEntry[]>('/api/operations/reconciliation/history', { params: { limit } })
      .pipe(timeout(12_000));
  }

  userDashboard(): Observable<UserDashboardSummary> {
    return this.http.get<UserDashboardSummary>('/api/user/dashboard').pipe(timeout(12_000));
  }

  userAnalytics(): Observable<UserAnalyticsSummary> {
    return this.http.get<UserAnalyticsSummary>('/api/user/analytics').pipe(timeout(12_000));
  }

  adminDashboard(): Observable<AdminPlatformDashboard> {
    return this.http.get<AdminPlatformDashboard>('/api/admin/dashboard').pipe(timeout(12_000));
  }

  quantityRules(query: TradingQuantityRulesQuery): Observable<TradingQuantityRules> {
    return this.http
      .get<TradingQuantityRules>('/api/platform/quantity-rules', {
        params: {
          instrumentType: query.instrumentType,
          underlying: query.underlying,
          symbol: query.symbol,
        },
      })
      .pipe(timeout(8_000));
  }
}
