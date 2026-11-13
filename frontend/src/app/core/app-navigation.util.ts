export type AppCenterView = 'market' | 'trades' | 'strategies' | 'intelligence' | 'manual' | 'operations';
export type AppOperationsTab =
  | 'risk'
  | 'emergency'
  | 'orders'
  | 'positions'
  | 'holdings'
  | 'journal'
  | 'timeline'
  | 'notifications'
  | 'broker'
  | 'reconciliation';
export type AppTradePanelTab = 'paper' | 'live' | 'history' | 'agent';
export type AppMarketKind = 'stocks' | 'options';
export type AppMarketNavTab = 'dashboard' | 'options';
export type AppOptionsChartMode = 'underlying' | 'contract';
export type AppOptionSide = 'CE' | 'PE';

export interface AppNavigationState {
  centerView: AppCenterView;
  tradePanelTab: AppTradePanelTab;
  marketKind: AppMarketKind;
  marketNavTab: AppMarketNavTab;
  strategyDetailId: string | null;
  operationsTab: AppOperationsTab;
  /** Dashboard selected symbol (index or stock). */
  selectedSymbol: string | null;
  optionUnderlying: string | null;
  optionExpiry: string | null;
  optionStrike: number | null;
  optionSide: AppOptionSide | null;
  optionsChartMode: AppOptionsChartMode | null;
}

export const MARKET_INDEX_SYMBOLS = new Set(['NIFTY', 'SENSEX', 'BANKNIFTY', 'FINNIFTY']);

const DEFAULT_NAV: AppNavigationState = {
  centerView: 'market',
  tradePanelTab: 'paper',
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
};

const TRADE_TABS = new Set<AppTradePanelTab>(['paper', 'live', 'history', 'agent']);
const OPERATIONS_TABS = new Set<AppOperationsTab>([
  'risk',
  'emergency',
  'orders',
  'positions',
  'holdings',
  'journal',
  'timeline',
  'notifications',
  'broker',
  'reconciliation',
]);

export const APP_NAV_STORAGE_KEY = 'twb-app-navigation';
export const MARKET_SYMBOL_PICK_KEY = 'twb-market-symbol-pick';

export const CHART_INTERVAL_STORAGE_KEY = 'twb-chart-interval';

export function stashMarketSymbolPick(symbolName: string): void {
  if (typeof window === 'undefined' || !symbolName.trim()) {
    return;
  }
  sessionStorage.setItem(MARKET_SYMBOL_PICK_KEY, symbolName.trim().toUpperCase());
}

export function consumeMarketSymbolPick(): string | null {
  if (typeof window === 'undefined') {
    return null;
  }
  const raw = sessionStorage.getItem(MARKET_SYMBOL_PICK_KEY);
  if (!raw) {
    return null;
  }
  sessionStorage.removeItem(MARKET_SYMBOL_PICK_KEY);
  return raw;
}

function parseSide(raw: string | null): AppOptionSide | null {
  const v = raw?.trim().toUpperCase();
  return v === 'CE' || v === 'PE' ? v : null;
}

function parseChartMode(raw: string | null): AppOptionsChartMode | null {
  const v = raw?.trim().toLowerCase();
  if (v === 'underlying' || v === 'spot') {
    return 'underlying';
  }
  if (v === 'contract' || v === 'premium') {
    return 'contract';
  }
  return null;
}

function parseStrike(raw: string | null): number | null {
  if (!raw?.trim()) {
    return null;
  }
  const n = Number(raw);
  return Number.isFinite(n) && n > 0 ? n : null;
}

function splitRouterPathAndQuery(url: string): { path: string; query: URLSearchParams } {
  const normalized = url.replace(/^\//, '');
  const qIndex = normalized.indexOf('?');
  const path = (qIndex >= 0 ? normalized.slice(0, qIndex) : normalized).replace(/^\//, '');
  const query =
    qIndex >= 0 ? new URLSearchParams(normalized.slice(qIndex + 1)) : new URLSearchParams();
  return { path, query };
}

function normalizeSymbol(symbol: string | null | undefined): string | null {
  const s = symbol?.trim().toUpperCase();
  return s ? s : null;
}

function normalizeAppNavigationState(value: Partial<AppNavigationState> | null | undefined): AppNavigationState {
  const centerView =
    value?.centerView === 'trades' ||
    value?.centerView === 'strategies' ||
    value?.centerView === 'intelligence' ||
    value?.centerView === 'manual' ||
    value?.centerView === 'operations' ||
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
  const strategyDetailId =
    typeof value?.strategyDetailId === 'string' && value.strategyDetailId.trim()
      ? value.strategyDetailId.trim()
      : null;
  const operationsTab = OPERATIONS_TABS.has(value?.operationsTab as AppOperationsTab)
    ? (value!.operationsTab as AppOperationsTab)
    : DEFAULT_NAV.operationsTab;
  const selectedSymbol = normalizeSymbol(value?.selectedSymbol ?? null);
  const optionUnderlying = normalizeSymbol(value?.optionUnderlying ?? null);
  const optionExpiry = value?.optionExpiry?.trim() || null;
  const optionStrike =
    typeof value?.optionStrike === 'number' && value.optionStrike > 0 ? value.optionStrike : null;
  const optionSide = parseSide(value?.optionSide ?? null);
  const optionsChartMode =
    value?.optionsChartMode === 'underlying' || value?.optionsChartMode === 'contract'
      ? value.optionsChartMode
      : null;
  return {
    centerView,
    tradePanelTab,
    marketKind,
    marketNavTab,
    strategyDetailId,
    operationsTab,
    selectedSymbol,
    optionUnderlying,
    optionExpiry,
    optionStrike,
    optionSide,
    optionsChartMode,
  };
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

function marketOptionsQuery(state: AppNavigationState): URLSearchParams {
  const params = new URLSearchParams();
  if (state.optionExpiry) {
    params.set('expiry', state.optionExpiry);
  }
  if (state.optionStrike != null && state.optionStrike > 0) {
    params.set('strike', String(state.optionStrike));
  }
  if (state.optionSide) {
    params.set('side', state.optionSide);
  }
  if (state.optionsChartMode === 'underlying') {
    params.set('mode', 'underlying');
  }
  return params;
}

export function routerPathForState(state: Partial<AppNavigationState> & Pick<AppNavigationState, 'centerView'>): string {
  const nav = normalizeAppNavigationState(state);
  if (nav.centerView === 'strategies') {
    if (nav.strategyDetailId) {
      return `/strategies/${encodeURIComponent(nav.strategyDetailId)}`;
    }
    return '/strategies';
  }
  if (nav.centerView === 'intelligence') {
    return '/intelligence';
  }
  if (nav.centerView === 'manual') {
    return '/manual';
  }
  if (nav.centerView === 'operations') {
    return `/operations/${nav.operationsTab}`;
  }
  if (nav.centerView === 'trades') {
    return `/trades/${nav.tradePanelTab}`;
  }
  if (nav.marketKind === 'options') {
    const underlying = nav.optionUnderlying ?? nav.selectedSymbol ?? 'NIFTY';
    const base = `/market/options/${encodeURIComponent(underlying)}`;
    const params = marketOptionsQuery({
      ...nav,
      optionUnderlying: underlying,
    });
    const q = params.toString();
    return q ? `${base}?${q}` : base;
  }
  const symbol = nav.selectedSymbol;
  if (symbol && MARKET_INDEX_SYMBOLS.has(symbol)) {
    return `/market/index/${encodeURIComponent(symbol)}`;
  }
  if (symbol) {
    return `/market?symbol=${encodeURIComponent(symbol)}`;
  }
  return '/market';
}

export function buildAppNavigationHash(state: Partial<AppNavigationState> & Pick<AppNavigationState, 'centerView'>): string {
  return `#${routerPathForState(state)}`;
}

export function parseRouterUrl(url: string): AppNavigationState {
  const { path, query } = splitRouterPathAndQuery(url);
  const segments = path.split('/').filter(Boolean);

  if (segments[0] === 'strategies') {
    if (segments[1]) {
      return {
        ...DEFAULT_NAV,
        centerView: 'strategies',
        strategyDetailId: decodeURIComponent(segments[1]),
        selectedSymbol: null,
      };
    }
    return { ...DEFAULT_NAV, centerView: 'strategies', strategyDetailId: null, selectedSymbol: null };
  }

  if (segments[0] === 'intelligence') {
    return { ...DEFAULT_NAV, centerView: 'intelligence', selectedSymbol: null };
  }

  if (segments[0] === 'manual') {
    return { ...DEFAULT_NAV, centerView: 'manual', selectedSymbol: null };
  }

  if (segments[0] === 'operations') {
    const tab = segments[1] as AppOperationsTab;
    return {
      ...DEFAULT_NAV,
      centerView: 'operations',
      operationsTab: OPERATIONS_TABS.has(tab) ? tab : 'risk',
      selectedSymbol: null,
    };
  }

  if (segments[0] === 'trades') {
    const tab = segments[1] as AppTradePanelTab;
    return {
      ...DEFAULT_NAV,
      centerView: 'trades',
      tradePanelTab: TRADE_TABS.has(tab) ? tab : 'paper',
      selectedSymbol: null,
    };
  }

  if (segments[0] === 'market') {
    if (segments[1] === 'index' && segments[2]) {
      const symbol = normalizeSymbol(decodeURIComponent(segments[2]));
      return {
        ...DEFAULT_NAV,
        centerView: 'market',
        marketKind: 'stocks',
        marketNavTab: 'dashboard',
        selectedSymbol: symbol ?? DEFAULT_NAV.selectedSymbol,
      };
    }

    if (segments[1] === 'options') {
      const underlying = normalizeSymbol(
        segments[2] ? decodeURIComponent(segments[2]) : query.get('underlying'),
      );
      return {
        ...DEFAULT_NAV,
        centerView: 'market',
        marketKind: 'options',
        marketNavTab: 'options',
        selectedSymbol: underlying ?? DEFAULT_NAV.selectedSymbol,
        optionUnderlying: underlying ?? 'NIFTY',
        optionExpiry: query.get('expiry')?.trim() || null,
        optionStrike: parseStrike(query.get('strike')),
        optionSide: parseSide(query.get('side')) ?? 'CE',
        optionsChartMode: parseChartMode(query.get('mode')) ?? 'contract',
      };
    }

    const symbol = normalizeSymbol(query.get('symbol'));
    return {
      ...DEFAULT_NAV,
      centerView: 'market',
      marketKind: 'stocks',
      marketNavTab: 'dashboard',
      selectedSymbol: symbol ?? DEFAULT_NAV.selectedSymbol,
    };
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
