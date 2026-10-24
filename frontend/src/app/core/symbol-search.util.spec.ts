import {
  parseSymbolSearchQuery,
  rankIndicesForStrike,
  resolveOptionSearchUnderlyings,
  resolveSearchIndices,
  strikesMatchingDigitPrefix,
  symbolMatchesStockQuery,
  symbolSearchCatalogTerm,
} from './symbol-search.util';

describe('symbol-search.util', () => {
  it('parses nifty strike and side', () => {
    const p = parseSymbolSearchQuery('nifty 22500 pe');
    expect(p.strike).toBe(22500);
    expect(p.indexNames).toContain('NIFTY');
    expect(p.optionSide).toBe('PE');
    expect(p.wantsOptions).toBe(true);
  });

  it('uses catalog term from index name in compound query', () => {
    expect(symbolSearchCatalogTerm('nifty 22500')).toBe('NIFTY');
    expect(symbolSearchCatalogTerm('reli')).toBe('reli');
  });

  it('matches stock tokens independently', () => {
    const p = parseSymbolSearchQuery('nifty 22500');
    expect(symbolMatchesStockQuery('NIFTY', [], p)).toBe(true);
    expect(symbolMatchesStockQuery('RELIANCE', [], p)).toBe(false);
  });

  it('ranks index by strike proximity', () => {
    const last = (name: string) => (name === 'NIFTY' ? 22600 : name === 'SENSEX' ? 74000 : 50000);
    expect(rankIndicesForStrike(22500, ['SENSEX', 'NIFTY', 'BANKNIFTY'], last)[0]).toBe('NIFTY');
  });

  it('resolves indices for strike-only query', () => {
    const p = parseSymbolSearchQuery('22500');
    const indices = resolveSearchIndices(p, ['NIFTY', 'SENSEX'], (n) => (n === 'NIFTY' ? 22600 : 74000));
    expect(indices[0]).toBe('NIFTY');
  });

  it('resolves indices for partial strike prefix 22', () => {
    const p = parseSymbolSearchQuery('22');
    const indices = resolveSearchIndices(p, ['NIFTY', 'SENSEX'], () => 0);
    expect(indices[0]).toBe('NIFTY');
  });

  it('finds strikes starting with digit prefix', () => {
    const strikes = strikesMatchingDigitPrefix('22', 50, 22600);
    expect(strikes.some((s) => s === 22500)).toBe(true);
    expect(strikes.every((s) => String(s).startsWith('22'))).toBe(true);
  });

  it('prefers F&O stock over index when query names the stock', () => {
    const p = parseSymbolSearchQuery('tcs 2530');
    const underlyings = resolveOptionSearchUnderlyings(p, ['TCS', 'WSTCSTPAPR'], ['NIFTY', 'SENSEX'], () => 0);
    expect(underlyings[0]).toBe('TCS');
  });

  it('does not false-match short token inside unrelated tickers', () => {
    const p = parseSymbolSearchQuery('tcs');
    expect(symbolMatchesStockQuery('WSTCSTPAPR', ['West Coast Paper Mills Limited'], p)).toBe(false);
    expect(symbolMatchesStockQuery('TCS', [], p)).toBe(true);
  });
});
