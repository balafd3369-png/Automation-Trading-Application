import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable, interval, startWith, switchMap } from 'rxjs';
import { BoardResponse, ChartResponse } from './board.models';

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
}
