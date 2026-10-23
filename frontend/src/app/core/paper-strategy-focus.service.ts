import { Injectable } from '@angular/core';
import { BehaviorSubject } from 'rxjs';
import { StrategyPnlQuery } from '../strategies/strategies-page.store';

export interface PaperStrategyFocus {
  strategyId: string;
  query: StrategyPnlQuery;
}

@Injectable({ providedIn: 'root' })
export class PaperStrategyFocusService {
  private readonly state$ = new BehaviorSubject<PaperStrategyFocus | null>(null);

  readonly focus$ = this.state$.asObservable();

  snapshot(): PaperStrategyFocus | null {
    return this.state$.value;
  }

  open(strategyId: string, query: StrategyPnlQuery): void {
    this.state$.next({ strategyId, query });
  }

  clear(): void {
    this.state$.next(null);
  }
}
