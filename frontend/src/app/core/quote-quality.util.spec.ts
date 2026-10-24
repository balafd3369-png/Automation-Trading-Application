import { quoteFreshnessLabel, quoteQualityLabel, quoteQualityShowCheck } from './quote-quality.util';

describe('quote quality labels', () => {
  it('maps VerifiedLive to LIVE with check', () => {
    expect(quoteQualityLabel('VerifiedLive')).toBe('LIVE');
    expect(quoteQualityShowCheck('VerifiedLive')).toBeTrue();
  });

  it('maps VerifiedClose to PREV CLOSE', () => {
    expect(quoteQualityLabel('VerifiedClose')).toBe('PREV CLOSE');
    expect(quoteQualityShowCheck('VerifiedClose')).toBeFalse();
  });

  it('formats live freshness as IST time and close as IST date', () => {
    expect(quoteFreshnessLabel('VerifiedLive', '2026-10-24T14:35:22Z')).toMatch(/\d{2}:\d{2}:\d{2}/);
    expect(quoteFreshnessLabel('VerifiedClose', '2026-10-23T10:00:00Z')).toContain('2026');
  });
});
