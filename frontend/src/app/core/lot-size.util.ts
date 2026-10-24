/** Index underlyings with configured F&amp;O lots (UI hints only — use API quantity-rules for trading). */
const LISTED_INDEX_UNDERLYINGS = new Set(['NIFTY', 'BANKNIFTY', 'FINNIFTY', 'SENSEX']);

export function isListedIndex(underlying: string): boolean {
  return LISTED_INDEX_UNDERLYINGS.has(underlying.trim().toUpperCase());
}
