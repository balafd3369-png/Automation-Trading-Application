import { Injectable } from '@angular/core';
import { BehaviorSubject } from 'rxjs';
import { BoardCounts } from './board.models';

@Injectable({ providedIn: 'root' })
export class MarketBreadthStore {
  private readonly counts$ = new BehaviorSubject<BoardCounts | null>(null);

  readonly breadth$ = this.counts$.asObservable();

  snapshot(): BoardCounts | null {
    return this.counts$.value;
  }

  setCounts(counts: BoardCounts | null): void {
    this.counts$.next(counts);
  }
}
