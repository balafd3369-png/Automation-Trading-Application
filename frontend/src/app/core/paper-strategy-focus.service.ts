import { Injectable } from '@angular/core';
import { BehaviorSubject } from 'rxjs';
import { StrategyPnlQuery } from '../strategies/strategies-page.store';
import { StrategyTradesSource, StrategyTradesTab } from './paper-strategy-navigation.util';

export interface PaperStrategyFocus {
  strategyId: string;
  query: StrategyPnlQuery;
  targetTab?: StrategyTradesTab;
  source?: StrategyTradesSource;
}

@Injectable({ providedIn: 'root' })
export class PaperStrategyFocusService {
  private readonly state$ = new BehaviorSubject<PaperStrategyFocus | null>(null);

  readonly focus$ = this.state$.asObservable();

  snapshot(): PaperStrategyFocus | null {
    return this.state$.value;
  }

  open(strategyId: string, query: StrategyPnlQuery, extras?: Pick<PaperStrategyFocus, 'targetTab' | 'source'>): void {
    this.state$.next({ strategyId, query, ...extras });
  }

  clear(): void {
    this.state$.next(null);
  }
}
