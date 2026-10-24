import { Injectable } from '@angular/core';
import { Subject } from 'rxjs';

/** Cross-route nudges so paper trades refresh without waiting for poll intervals. */
@Injectable({ providedIn: 'root' })
export class TradingRefreshService {
  private readonly paperAgentRefresh = new Subject<void>();
  private readonly paperHistoryRefresh = new Subject<void>();

  readonly paperAgentRefresh$ = this.paperAgentRefresh.asObservable();
  readonly paperHistoryRefresh$ = this.paperHistoryRefresh.asObservable();

  requestPaperRefresh(): void {
    this.paperAgentRefresh.next();
    this.paperHistoryRefresh.next();
  }
}
