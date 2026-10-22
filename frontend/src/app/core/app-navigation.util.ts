export type AppCenterView = 'market' | 'trades' | 'strategies' | 'intelligence';
export type AppTradePanelTab = 'paper' | 'live' | 'history' | 'agent';
export type AppMarketKind = 'stocks' | 'options';
export type AppMarketNavTab = 'dashboard' | 'options';

export interface AppNavigationState {
  centerView: AppCenterView;
  tradePanelTab: AppTradePanelTab;
  marketKind: AppMarketKind;
  marketNavTab: AppMarketNavTab;
}

const DEFAULT_NAV: AppNavigationState = {
  centerView: 'market',
  tradePanelTab: 'paper',
  marketKind: 'stocks',
  marketNavTab: 'dashboard',
};

const TRADE_TABS = new Set<AppTradePanelTab>(['paper', 'live', 'history', 'agent']);
export const APP_NAV_STORAGE_KEY = 'twb-app-navigation';

function normalizeAppNavigationState(value: Partial<AppNavigationState> | null | undefined): AppNavigationState {
  const centerView =
    value?.centerView === 'trades' ||
    value?.centerView === 'strategies' ||
    value?.centerView === 'intelligence' ||
    value?.centerView === 'market'
      ? value.centerView
      : DEFAULT_NAV.centerView;
  const tradePanelTab = TRADE_TABS.has(value?.tradePanelTab as AppTradePanelTab)
    ? (value!.tradePanelTab as AppTradePanelTab)
    : DEFAULT_NAV.tradePanelTab;
  const marketKind = value?.marketKind === 'options' ? 'options' : 'stocks';
  const savedNavTab = value?.marketNavTab as string | undefined;
  const marketNavTab =
    savedNavTab === 'options'
      ? 'options'
      : savedNavTab === 'dashboard' || savedNavTab === 'stocks'
        ? 'dashboard'
        : marketKind === 'options'
          ? 'options'
          : DEFAULT_NAV.marketNavTab;
  return { centerView, tradePanelTab, marketKind, marketNavTab };
}

function readStoredNavigationState(): AppNavigationState | null {
  if (typeof window === 'undefined') {
    return null;
  }
  try {
    const raw = localStorage.getItem(APP_NAV_STORAGE_KEY);
    if (!raw) {
      return null;
    }
    return normalizeAppNavigationState(JSON.parse(raw) as Partial<AppNavigationState>);
  } catch {
    return null;
  }
}

export function buildAppNavigationHash(state: AppNavigationState): string {
  if (state.centerView === 'strategies') {
    return '#/strategies';
  }
  if (state.centerView === 'intelligence') {
    return '#/intelligence';
  }
  if (state.centerView === 'trades') {
    return `#/trades/${state.tradePanelTab}`;
  }
  if (state.marketKind === 'options') {
    return '#/market/options';
  }
  return '#/market';
}

export function parseRouterUrl(url: string): AppNavigationState {
  const path = url.split('?')[0].replace(/^\//, '');
  const segments = path.split('/').filter(Boolean);

  if (segments[0] === 'strategies') {
    return { ...DEFAULT_NAV, centerView: 'strategies' };
  }

  if (segments[0] === 'intelligence') {
    return { ...DEFAULT_NAV, centerView: 'intelligence' };
  }

  if (segments[0] === 'trades') {
    const tab = segments[1] as AppTradePanelTab;
    return {
      ...DEFAULT_NAV,
      centerView: 'trades',
      tradePanelTab: TRADE_TABS.has(tab) ? tab : 'paper',
    };
  }

  if (segments[0] === 'market') {
    const kind = segments[1] === 'options' ? 'options' : 'stocks';
    if (kind === 'options') {
      return { ...DEFAULT_NAV, centerView: 'market', marketKind: 'options', marketNavTab: 'options' };
    }
    const stored = readStoredNavigationState();
    const marketNavTab = 'dashboard';
    return { ...DEFAULT_NAV, centerView: 'market', marketKind: 'stocks', marketNavTab };
  }

  return { ...DEFAULT_NAV };
}

declare global {
  interface Window {
    __TWB_BOOT_NAV__?: Partial<AppNavigationState>;
  }
}

export function readBootNavigationState(): AppNavigationState {
  if (typeof window !== 'undefined' && window.__TWB_BOOT_NAV__) {
    return normalizeAppNavigationState(window.__TWB_BOOT_NAV__);
  }
  if (typeof window !== 'undefined' && window.location.hash.length > 1) {
    return readAppNavigationFromLocation();
  }
  return restoreAppNavigationState();
}

export function routerPathForState(state: AppNavigationState): string {
  if (state.centerView === 'strategies') {
    return '/strategies';
  }
  if (state.centerView === 'intelligence') {
    return '/intelligence';
  }
  if (state.centerView === 'trades') {
    return `/trades/${state.tradePanelTab}`;
  }
  if (state.marketKind === 'options') {
    return '/market/options';
  }
  return '/market';
}

export function parseAppNavigationHash(rawHash: string): AppNavigationState {
  const hash = rawHash.trim().replace(/^#/, '');
  const path = hash.startsWith('/') ? hash.slice(1) : hash;
  return parseRouterUrl(path);
}

export function readAppNavigationFromLocation(): AppNavigationState {
  if (typeof window === 'undefined') {
    return { ...DEFAULT_NAV };
  }
  return normalizeAppNavigationState(parseAppNavigationHash(window.location.hash || ''));
}

/** Prefer URL hash when present; otherwise last saved tab from localStorage. */
export function restoreAppNavigationState(): AppNavigationState {
  if (typeof window === 'undefined') {
    return { ...DEFAULT_NAV };
  }
  const hash = window.location.hash || '';
  if (hash.length > 1) {
    return readAppNavigationFromLocation();
  }
  return readStoredNavigationState() ?? { ...DEFAULT_NAV };
}

export function persistAppNavigationState(state: AppNavigationState): void {
  const normalized = normalizeAppNavigationState(state);
  writeAppNavigationHash(normalized);
  if (typeof window === 'undefined') {
    return;
  }
  localStorage.setItem(APP_NAV_STORAGE_KEY, JSON.stringify(normalized));
}

export function writeAppNavigationHash(state: AppNavigationState): void {
  if (typeof window === 'undefined') {
    return;
  }
  const hash = buildAppNavigationHash(state);
  const url = `${window.location.pathname}${window.location.search}${hash}`;
  if (`${window.location.pathname}${window.location.search}${window.location.hash}` !== url) {
    window.history.replaceState(null, '', url);
  }
}
