import { StrategyCatalogEntry, StrategyLiveRecommendation } from './strategies.models';

/** Catalog ids and tags for gap-up / gap-down playbook (separate from zone & session strategies). */
export const GAP_CATALOG_STRATEGY_IDS = new Set<string>([
  'gap-failed-rally-gap-down',
  'gap-distribution-gap-down',
  'gap-weak-close-gap-down',
  'gap-strong-close-gap-up',
]);

/** Backend GapDayStudy clue id → catalog id (Strategies gap block). */
export const GAP_CLUE_ID_TO_CATALOG_ID: Record<string, string> = {
  GAP_DOWN_AFTER_FAILED_RALLY: 'gap-failed-rally-gap-down',
  GAP_DOWN_AFTER_DISTRIBUTION: 'gap-distribution-gap-down',
  GAP_DOWN_AFTER_CLOSE_BELOW_S1: 'gap-weak-close-gap-down',
  GAP_UP_AFTER_CLOSE_ABOVE_B1: 'gap-strong-close-gap-up',
};

export interface GapPlaybookRolloutEntry {
  id: string;
  clueId: string;
  label: string;
  universe: string;
  paperAuto: boolean;
}

export const GAP_PLAYBOOK_ROLLOUT: readonly GapPlaybookRolloutEntry[] = [
  {
    id: 'gap-failed-rally-gap-down',
    clueId: 'GAP_DOWN_AFTER_FAILED_RALLY',
    label: 'Failed rally → gap down (short)',
    universe: 'Nifty 50 stocks · 09:20–10:45 IST on gap-down days',
    paperAuto: true,
  },
  {
    id: 'gap-distribution-gap-down',
    clueId: 'GAP_DOWN_AFTER_DISTRIBUTION',
    label: 'Distribution → gap down (short)',
    universe: 'Nifty 50 · prior open ≥ B1, close ≤ S1',
    paperAuto: true,
  },
  {
    id: 'gap-weak-close-gap-down',
    clueId: 'GAP_DOWN_AFTER_CLOSE_BELOW_S1',
    label: 'Weak close → gap down (short)',
    universe: 'Nifty 50 · prior close below S1',
    paperAuto: true,
  },
  {
    id: 'gap-strong-close-gap-up',
    clueId: 'GAP_UP_AFTER_CLOSE_ABOVE_B1',
    label: 'Strong close → gap up (long)',
    universe: 'Nifty 50 · prior close ≥ B1',
    paperAuto: true,
  },
];

export function isGapCatalogStrategyId(strategyId: string | null | undefined): boolean {
  const id = (strategyId ?? '').trim().toLowerCase();
  if (!id) {
    return false;
  }
  if (GAP_CATALOG_STRATEGY_IDS.has(id)) {
    return true;
  }
  return id.startsWith('gap-');
}

export function catalogEntryIsGapStrategy(entry: StrategyCatalogEntry): boolean {
  if (isGapCatalogStrategyId(entry.id)) {
    return true;
  }
  return (entry.tags ?? []).some((t) => t.toLowerCase() === 'gap');
}

export function liveRecommendationIsGap(rec: StrategyLiveRecommendation): boolean {
  return isGapCatalogStrategyId(rec.strategyId);
}
