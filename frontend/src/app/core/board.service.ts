import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable, interval, of, startWith, exhaustMap, timeout } from 'rxjs';
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

  watch(pollMs = 6000): Observable<BoardResponse> {
    return interval(pollMs).pipe(
      startWith(0),
      exhaustMap(() => this.fetchBoard()),
    );
  }

  chart(name: string, interval = '5minute'): Observable<ChartResponse> {
    return this.http
      .get<ChartResponse>(`/api/board/chart/${encodeURIComponent(name)}`, {
        params: { interval },
      })
      .pipe(timeout(18_000));
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
}
