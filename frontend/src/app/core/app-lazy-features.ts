import { Type } from '@angular/core';

export type LazyFeatureKey =
  | 'strategies'
  | 'strategyCenter'
  | 'manual'
  | 'operations'
  | 'intelligence'
  | 'tradeHistory'
  | 'strategyTradesPanel';

const loaders: Record<LazyFeatureKey, () => Promise<Type<unknown>>> = {
  strategies: () =>
    import('../strategies/strategies-page.component').then((m) => m.StrategiesPageComponent),
  strategyCenter: () =>
    import('../strategy-command-center/strategy-command-center.component').then(
      (m) => m.StrategyCommandCenterComponent,
    ),
  manual: () =>
    import('../manual-trading/manual-trading-page.component').then((m) => m.ManualTradingPageComponent),
  operations: () =>
    import('../operations/operations-suite.component').then((m) => m.OperationsSuiteComponent),
  intelligence: () =>
    import('../super-intelligence/super-intelligence-page.component').then(
      (m) => m.SuperIntelligencePageComponent,
    ),
  tradeHistory: () =>
    import('../trade-history-dashboard/trade-history-dashboard.component').then(
      (m) => m.TradeHistoryDashboardComponent,
    ),
  strategyTradesPanel: () =>
    import('../strategy-trades-panel/strategy-trades-panel.component').then(
      (m) => m.StrategyTradesPanelComponent,
    ),
};

export function loadLazyFeature(key: LazyFeatureKey): Promise<Type<unknown>> {
  return loaders[key]();
}

/** Warm common feature chunks after first paint without blocking initial load. */
export function prefetchLazyFeaturesOnIdle(): void {
  const warm = () => {
    for (const key of Object.keys(loaders) as LazyFeatureKey[]) {
      void loaders[key]();
    }
  };
  if (typeof requestIdleCallback !== 'undefined') {
    requestIdleCallback(warm, { timeout: 8000 });
  } else {
    setTimeout(warm, 2500);
  }
}
