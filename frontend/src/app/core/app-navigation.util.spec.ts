import {
  buildAppNavigationHash,
  parseAppNavigationHash,
  parseRouterUrl,
  persistAppNavigationState,
  restoreAppNavigationState,
  routerPathForState,
  APP_NAV_STORAGE_KEY,
} from './app-navigation.util';

describe('app-navigation.util', () => {
  afterEach(() => {
    localStorage.removeItem(APP_NAV_STORAGE_KEY);
    window.history.replaceState(null, '', window.location.pathname);
  });

  it('round-trips trade details tabs', () => {
    const state = {
      centerView: 'trades' as const,
      tradePanelTab: 'history' as const,
      marketKind: 'stocks' as const,
      marketNavTab: 'dashboard' as const,
      strategyDetailId: null,
      operationsTab: 'risk' as const,
      selectedSymbol: 'NIFTY',
      optionUnderlying: null,
      optionExpiry: null,
      optionStrike: null,
      optionSide: null,
      optionsChartMode: null,
    };
    expect(buildAppNavigationHash(state)).toBe('#/trades/history');
    expect(parseAppNavigationHash('#/trades/history').tradePanelTab).toBe('history');
  });

  it('maps index dashboard to /market/index/:symbol', () => {
    const parsed = parseAppNavigationHash('#/market/index/SENSEX');
    expect(parsed.marketKind).toBe('stocks');
    expect(parsed.selectedSymbol).toBe('SENSEX');
    expect(
      routerPathForState({
        centerView: 'market',
        tradePanelTab: 'paper',
        marketKind: 'stocks',
        marketNavTab: 'dashboard',
        strategyDetailId: null,
        operationsTab: 'risk',
        selectedSymbol: 'SENSEX',
        optionUnderlying: null,
        optionExpiry: null,
        optionStrike: null,
        optionSide: null,
        optionsChartMode: null,
      }),
    ).toBe('/market/index/SENSEX');
  });

  it('maps options contract routes with query', () => {
    const url =
      'market/options/NIFTY?expiry=2026-11-17&strike=250&side=CE';
    const parsed = parseRouterUrl(url);
    expect(parsed.marketKind).toBe('options');
    expect(parsed.optionUnderlying).toBe('NIFTY');
    expect(parsed.optionExpiry).toBe('2026-11-17');
    expect(parsed.optionStrike).toBe(250);
    expect(parsed.optionSide).toBe('CE');
    expect(
      buildAppNavigationHash({
        centerView: 'market',
        tradePanelTab: 'paper',
        marketKind: 'options',
        marketNavTab: 'options',
        strategyDetailId: null,
        operationsTab: 'risk',
        selectedSymbol: 'NIFTY',
        optionUnderlying: 'NIFTY',
        optionExpiry: '2026-11-17',
        optionStrike: 250,
        optionSide: 'CE',
        optionsChartMode: 'contract',
      }),
    ).toBe('#/market/options/NIFTY?expiry=2026-11-17&strike=250&side=CE');
  });

  it('restores from localStorage when hash is empty', () => {
    persistAppNavigationState({
      centerView: 'trades',
      tradePanelTab: 'history',
      marketKind: 'stocks',
      marketNavTab: 'dashboard',
      strategyDetailId: null,
      operationsTab: 'risk',
      selectedSymbol: 'NIFTY',
      optionUnderlying: null,
      optionExpiry: null,
      optionStrike: null,
      optionSide: null,
      optionsChartMode: null,
    });
    window.history.replaceState(null, '', window.location.pathname);
    expect(restoreAppNavigationState().tradePanelTab).toBe('history');
  });

  it('parses strategy command center route', () => {
    const state = parseRouterUrl('strategies/bala-10am-high-fade?range=1m');
    expect(state.centerView).toBe('strategies');
    expect(state.strategyDetailId).toBe('bala-10am-high-fade');
    expect(routerPathForState(state)).toBe('/strategies/bala-10am-high-fade');
  });
});
