import { TradeHistoryRange } from './board.models';
import { PaperStrategyFocus } from './paper-strategy-focus.service';
import { StrategyPnlQuery } from '../strategies/strategies-page.store';

const RANGE_VALUES = new Set<TradeHistoryRange>(['today', '1w', '1m']);

export type StrategyTradesTab = 'paper' | 'history' | 'live';
export type StrategyTradesSource = 'paper' | 'live' | 'all';

export function buildPaperStrategyTradesUrl(
  strategyId: string,
  query: StrategyPnlQuery,
  options?: { tab?: StrategyTradesTab; source?: StrategyTradesSource },
): string {
  const tab = options?.tab ?? 'history';
  const params = new URLSearchParams();
  params.set('strategy', strategyId.trim());
  if (query.from) {
    params.set('from', query.from);
    params.set('to', query.to || query.from);
  } else {
    params.set('range', query.range);
  }
  const source = options?.source ?? 'paper';
  if (source !== 'all') {
    params.set('source', source);
  }
  return `/trades/${tab}?${params.toString()}`;
}

export function parsePaperStrategyFocusFromUrl(url: string): PaperStrategyFocus | null {
  const queryIndex = url.indexOf('?');
  if (queryIndex < 0) {
    return null;
  }
  const path = url.slice(0, queryIndex);
  const params = new URLSearchParams(url.slice(queryIndex + 1));
  const strategyId = (params.get('strategy') || params.get('strategyId') || '').trim();
  if (!strategyId) {
    return null;
  }
  const from = params.get('from')?.trim();
  const sourceParam = params.get('source')?.trim().toLowerCase();
  const source: StrategyTradesSource =
    sourceParam === 'live' ? 'live' : sourceParam === 'all' ? 'all' : 'paper';
  const targetTab: StrategyTradesTab = path.includes('/trades/history')
    ? 'history'
    : path.includes('/trades/live')
      ? 'live'
      : 'paper';
  if (from) {
    const to = params.get('to')?.trim() || from;
    return { strategyId, query: { range: '1m', from, to }, source, targetTab };
  }
  const rangeParam = params.get('range') as TradeHistoryRange | null;
  const range = rangeParam && RANGE_VALUES.has(rangeParam) ? rangeParam : '1m';
  return { strategyId, query: { range, from: null, to: null }, source, targetTab };
}

export function urlHasPaperStrategyFocus(url: string): boolean {
  return parsePaperStrategyFocusFromUrl(url) != null;
}
