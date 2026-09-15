import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable, interval, startWith, switchMap } from 'rxjs';
import {
  BoardResponse,
  ChartResponse,
  LiveExecutionSnapshot,
  OptionMarketQuote,
  PaperAgentSnapshot,
  PaperTradeHistoryResponse,
  TradeHistoryRange,
  TradeHistoryResponse,
} from './board.models';

@Injectable({ providedIn: 'root' })
export class BoardService {
  constructor(private readonly http: HttpClient) {}

  watch(): Observable<BoardResponse> {
    return interval(1000).pipe(
      startWith(0),
      switchMap(() => this.http.get<BoardResponse>('/api/board')),
    );
  }

  chart(name: string, interval = '5minute'): Observable<ChartResponse> {
    return this.http.get<ChartResponse>(`/api/board/chart/${encodeURIComponent(name)}`, {
      params: { interval },
    });
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
    return this.http.get<PaperAgentSnapshot>('/api/paper-agent');
  }

  sendPaperAgentCommand(message: string): Observable<PaperAgentSnapshot> {
    return this.http.post<PaperAgentSnapshot>('/api/paper-agent/command', { message });
  }

  tradeHistory(range: TradeHistoryRange): Observable<TradeHistoryResponse> {
    return this.http.get<TradeHistoryResponse>('/api/paper-agent/history', {
      params: { range },
    });
  }

  paperTrades(range: TradeHistoryRange): Observable<PaperTradeHistoryResponse> {
    return this.http.get<PaperTradeHistoryResponse>('/api/paper-agent/trades', {
      params: { range },
    });
  }

  liveAgent(): Observable<LiveExecutionSnapshot> {
    return this.http.get<LiveExecutionSnapshot>('/api/live-agent');
  }

  armLive(acknowledgement: string): Observable<LiveExecutionSnapshot> {
    return this.http.post<LiveExecutionSnapshot>('/api/live-agent/arm', { acknowledgement });
  }

  disarmLive(): Observable<LiveExecutionSnapshot> {
    return this.http.post<LiveExecutionSnapshot>('/api/live-agent/disarm', {});
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
