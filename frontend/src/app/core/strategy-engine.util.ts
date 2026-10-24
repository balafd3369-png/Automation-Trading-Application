import { RankedDecision } from './board.models';
import { normalizeCatalogStrategyId } from './trading-ui.util';

/** Backend decision / rollout codes for each catalog strategy id. */
const CATALOG_TO_ENGINE_CODES: Record<string, readonly string[]> = {
  'zone-b1-break-b2': ['ZONE_B1_BREAK_B2'],
  'zone-s1-break-s2': ['ZONE_S1_BREAK_S2'],
  'zone-s2-bounce-b1': ['ZONE_S2_BOUNCE_B1'],
  'stock-b1-rejection-to-s2': ['STOCK_B1_REJECTION_TO_S2'],
  'bala-10am-high-fade': ['TEN_AM_HIGH_FADE_DAY_LOW'],
  'ten-am-low-break-to-day-open': ['TEN_AM_LOW_BREAK_DAY_OPEN'],
};

const ZONE_CATALOG_IDS = new Set([
  'zone-b1-break-b2',
  'zone-s1-break-s2',
  'zone-s2-bounce-b1',
]);

export function catalogEngineStrategyCodes(canonicalStrategyId: string): readonly string[] {
  const id = normalizeCatalogStrategyId(canonicalStrategyId) ?? canonicalStrategyId.trim();
  return CATALOG_TO_ENGINE_CODES[id] ?? [id.replace(/-/g, '_').toUpperCase()];
}

export function decisionMatchesCatalogStrategy(decision: RankedDecision, canonicalStrategyId: string): boolean {
  const codes = new Set(catalogEngineStrategyCodes(canonicalStrategyId).map((c) => c.toUpperCase()));
  const raw = decision.strategy.trim().toUpperCase();
  if (codes.has(raw)) {
    return true;
  }
  const normalized = normalizeCatalogStrategyId(decision.strategy);
  return normalized === (normalizeCatalogStrategyId(canonicalStrategyId) ?? canonicalStrategyId);
}

export function engineStrategyMatchesCatalog(engineStrategy: string, canonicalStrategyId: string): boolean {
  const codes = new Set(catalogEngineStrategyCodes(canonicalStrategyId).map((c) => c.toUpperCase()));
  const raw = engineStrategy.trim().toUpperCase();
  if (codes.has(raw)) {
    return true;
  }
  return normalizeCatalogStrategyId(engineStrategy) === normalizeCatalogStrategyId(canonicalStrategyId);
}

export function isZoneCatalogStrategy(canonicalStrategyId: string): boolean {
  const id = normalizeCatalogStrategyId(canonicalStrategyId) ?? canonicalStrategyId;
  return ZONE_CATALOG_IDS.has(id);
}

export function rolloutCodeForZoneCatalog(canonicalStrategyId: string): string | null {
  const id = normalizeCatalogStrategyId(canonicalStrategyId) ?? canonicalStrategyId;
  switch (id) {
    case 'zone-b1-break-b2':
      return 'ZONE_B1_BREAK_B2';
    case 'zone-s1-break-s2':
      return 'ZONE_S1_BREAK_S2';
    case 'zone-s2-bounce-b1':
      return 'ZONE_S2_BOUNCE_B1';
    default:
      return null;
  }
}
