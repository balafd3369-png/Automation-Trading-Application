import { resolveMarketSentimentMode } from './market-sentiment.util';

describe('market-sentiment.util', () => {
  it('detects selling pressure', () => {
    expect(resolveMarketSentimentMode({ buying: 2, selling: 8, rangeBound: 1 })).toBe('sell');
  });

  it('detects buying pressure', () => {
    expect(resolveMarketSentimentMode({ buying: 9, selling: 2, rangeBound: 0 })).toBe('buy');
  });

  it('detects range when pressure is balanced', () => {
    expect(resolveMarketSentimentMode({ buying: 5, selling: 5, rangeBound: 2 })).toBe('range');
  });
});
