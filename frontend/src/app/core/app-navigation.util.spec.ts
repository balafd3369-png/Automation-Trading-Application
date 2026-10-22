import {
  buildAppNavigationHash,
  parseAppNavigationHash,
  persistAppNavigationState,
  restoreAppNavigationState,
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
    };
    expect(buildAppNavigationHash(state)).toBe('#/trades/history');
    expect(parseAppNavigationHash('#/trades/history').tradePanelTab).toBe('history');
  });

  it('maps market routes to stocks vs options mode', () => {
    expect(parseAppNavigationHash('#/market/options').marketKind).toBe('options');
    expect(parseAppNavigationHash('#/market').marketKind).toBe('stocks');
    expect(buildAppNavigationHash({
      centerView: 'market',
      tradePanelTab: 'paper',
      marketKind: 'stocks',
      marketNavTab: 'dashboard',
    })).toBe('#/market');
  });

  it('restores from localStorage when hash is empty', () => {
    persistAppNavigationState({
      centerView: 'trades',
      tradePanelTab: 'history',
      marketKind: 'stocks',
      marketNavTab: 'dashboard',
    });
    window.history.replaceState(null, '', window.location.pathname);
    expect(restoreAppNavigationState().tradePanelTab).toBe('history');
  });
});
