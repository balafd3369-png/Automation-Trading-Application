import { QuoteSourceQuality, SymbolSnapshot } from './board.models';

export function quoteQualityLabel(quality: QuoteSourceQuality | undefined): string {
  switch (quality) {
    case 'VerifiedLive':
      return 'LIVE';
    case 'VerifiedClose':
      return 'PREV CLOSE';
    case 'FallbackClose':
      return 'CLOSE';
    case 'SeedData':
      return 'SEED';
    case 'Placeholder':
      return 'PLACEHOLDER';
    case 'Rejected':
      return 'REJECTED';
    default:
      return '';
  }
}

export function quoteQualityShowCheck(quality: QuoteSourceQuality | undefined): boolean {
  return quality === 'VerifiedLive';
}

export function quoteQualityCssClass(quality: QuoteSourceQuality | undefined): string {
  switch (quality) {
    case 'VerifiedLive':
      return 'quote-quality-live';
    case 'VerifiedClose':
    case 'FallbackClose':
      return 'quote-quality-close';
    case 'Rejected':
      return 'quote-quality-rejected';
    case 'Placeholder':
      return 'quote-quality-placeholder';
    case 'SeedData':
      return 'quote-quality-seed';
    default:
      return 'quote-quality-unknown';
  }
}

export function symbolQuoteQuality(row: SymbolSnapshot | null | undefined): QuoteSourceQuality | undefined {
  return row?.quoteQuality;
}

/** IST freshness line for UI (time for LIVE, date for close-based quotes). */
export function quoteFreshnessLabel(
  quality: QuoteSourceQuality | undefined,
  quoteTimestamp: string | null | undefined,
): string {
  if (!quality || !quoteTimestamp) {
    return '';
  }
  const instant = Date.parse(quoteTimestamp);
  if (!Number.isFinite(instant)) {
    return '';
  }
  if (quality === 'VerifiedLive') {
    return new Intl.DateTimeFormat('en-IN', {
      timeZone: 'Asia/Kolkata',
      hour12: false,
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit',
    }).format(instant);
  }
  return new Intl.DateTimeFormat('en-IN', {
    timeZone: 'Asia/Kolkata',
    day: '2-digit',
    month: 'short',
    year: 'numeric',
  }).format(instant);
}

export function symbolQuoteFreshness(row: SymbolSnapshot | null | undefined): string {
  if (!row) {
    return '';
  }
  return quoteFreshnessLabel(row.quoteQuality, row.quoteTimestamp);
}
