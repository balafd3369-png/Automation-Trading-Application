import { TradeHistoryRange } from './board.models';
import { StrategyPnlQuery } from '../strategies/strategies-page.store';
import { StrategyTradesSource } from './paper-strategy-navigation.util';
import { buildPaperStrategyTradesUrl } from './paper-strategy-navigation.util';

const RANGE_VALUES = new Set<TradeHistoryRange>(['today', '1w', '1m']);

export interface StrategyCommandCenterQuery {
  strategyId: string;
  query: StrategyPnlQuery;
  source: StrategyTradesSource;
}

export function buildStrategyCommandCenterUrl(
  strategyId: string,
  query: StrategyPnlQuery,
  options?: { source?: StrategyTradesSource },
): string {
  const params = new URLSearchParams();
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
  const qs = params.toString();
  const id = encodeURIComponent(strategyId.trim());
  return `/strategies/${id}${qs ? `?${qs}` : ''}`;
}

export function parseStrategyCommandCenterFromUrl(url: string): StrategyCommandCenterQuery | null {
  const [pathPart, queryPart = ''] = url.split('?');
  const path = pathPart.replace(/^\//, '');
  const segments = path.split('/').filter(Boolean);
  if (segments[0] !== 'strategies' || !segments[1]) {
    return null;
  }
  const strategyId = decodeURIComponent(segments[1]).trim();
  if (!strategyId) {
    return null;
  }
  const params = new URLSearchParams(queryPart);
  const from = params.get('from')?.trim();
  const sourceParam = params.get('source')?.trim().toLowerCase();
  const source: StrategyTradesSource =
    sourceParam === 'live' ? 'live' : sourceParam === 'all' ? 'all' : 'paper';
  if (from) {
    const to = params.get('to')?.trim() || from;
    return { strategyId, query: { range: '1m', from, to }, source };
  }
  const rangeParam = params.get('range') as TradeHistoryRange | null;
  const range = rangeParam && RANGE_VALUES.has(rangeParam) ? rangeParam : '1m';
  return { strategyId, query: { range, from: null, to: null }, source };
}

export function strategyCommandCenterStrategyIdFromUrl(url: string): string | null {
  return parseStrategyCommandCenterFromUrl(url)?.strategyId ?? null;
}

export function buildStrategyPaperTradesUrl(
  strategyId: string,
  query: StrategyPnlQuery,
  source: StrategyTradesSource = 'paper',
): string {
  return buildPaperStrategyTradesUrl(strategyId, query, { tab: 'history', source });
}

export function buildStrategyLiveTradesUrl(strategyId: string, query: StrategyPnlQuery): string {
  return buildPaperStrategyTradesUrl(strategyId, query, { tab: 'live', source: 'live' });
}
