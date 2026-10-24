export type OptionSideHint = 'CE' | 'PE';

export interface ParsedSymbolSearchQuery {
  raw: string;
  tokens: string[];
  alphaTokens: string[];
  strike: number | null;
  optionSide: OptionSideHint | null;
  indexNames: string[];
  wantsOptions: boolean;
}

const INDEX_ALIASES: Array<{ keys: string[]; name: string }> = [
  { keys: ['banknifty', 'bank nifty', 'bank-nifty', 'bnf'], name: 'BANKNIFTY' },
  { keys: ['finnifty', 'fin nifty', 'fin-nifty'], name: 'FINNIFTY' },
  { keys: ['sensex', 'sx'], name: 'SENSEX' },
  { keys: ['nifty', 'nifty50', 'nifty 50'], name: 'NIFTY' },
];

const DEFAULT_INDEX_ORDER = ['NIFTY', 'BANKNIFTY', 'FINNIFTY', 'SENSEX'];

/** NSE F&O stock underlyings (aligned with backend IndexUniverse.Nifty50). */
export const FNO_EQUITY_SYMBOLS: readonly string[] = [
  'ADANIENT', 'ADANIPORTS', 'APOLLOHOSP', 'ASIANPAINT', 'AXISBANK',
  'BAJAJ-AUTO', 'BAJFINANCE', 'BAJAJFINSV', 'BEL', 'BHARTIARTL',
  'BPCL', 'CIPLA', 'COALINDIA', 'DRREDDY', 'EICHERMOT',
  'GRASIM', 'HCLTECH', 'HDFCBANK', 'HDFCLIFE', 'HEROMOTOCO',
  'HINDALCO', 'HINDUNILVR', 'ICICIBANK', 'INDUSINDBK', 'INFY',
  'ITC', 'JIOFIN', 'JSWSTEEL', 'KOTAKBANK', 'LT',
  'M&M', 'MARUTI', 'NESTLEIND', 'NTPC', 'ONGC',
  'POWERGRID', 'RELIANCE', 'SBILIFE', 'SBIN', 'SHRIRAMFIN',
  'SUNPHARMA', 'TATACONSUM', 'TATAMOTORS', 'TATASTEEL', 'TCS',
  'TECHM', 'TITAN', 'TRENT', 'ULTRACEMCO', 'WIPRO',
];

const FNO_EQUITY_SET = new Set(FNO_EQUITY_SYMBOLS.map((s) => s.toUpperCase()));
const INDEX_NAME_SET = new Set(DEFAULT_INDEX_ORDER);

export function isIndexOptionUnderlying(symbol: string): boolean {
  return INDEX_NAME_SET.has(symbol.toUpperCase());
}

export function isFnoEquityUnderlying(symbol: string): boolean {
  return FNO_EQUITY_SET.has(symbol.toUpperCase());
}

export function hasListedOptions(symbol: string): boolean {
  const key = symbol.toUpperCase();
  return INDEX_NAME_SET.has(key) || FNO_EQUITY_SET.has(key);
}

export function parseSymbolSearchQuery(raw: string): ParsedSymbolSearchQuery {
  const trimmed = raw.trim();
  const lower = trimmed.toLowerCase();
  const tokens = lower.split(/\s+/).filter(Boolean);
  const alphaTokens = tokens.filter((t) => /[a-z]/.test(t) && !/^\d+$/.test(t));

  let strike: number | null = null;
  const strikeMatch = lower.replace(/,/g, '').match(/\b(\d{4,6})\b/);
  if (strikeMatch) {
    const n = Number(strikeMatch[1]);
    if (n >= 1000 && n <= 999_999) {
      strike = n;
    }
  }

  let optionSide: OptionSideHint | null = null;
  if (/\bpe\b|put\b/.test(lower)) {
    optionSide = 'PE';
  } else if (/\bce\b|call\b/.test(lower)) {
    optionSide = 'CE';
  }

  const indexNames: string[] = [];
  const compact = lower.replace(/[^a-z0-9]/g, '');
  for (const entry of INDEX_ALIASES) {
    for (const key of entry.keys) {
      const keyCompact = key.replace(/[^a-z0-9]/g, '');
      if (compact.includes(keyCompact) || lower.includes(key)) {
        if (!indexNames.includes(entry.name)) {
          indexNames.push(entry.name);
        }
      }
    }
  }
  for (const token of alphaTokens) {
    if (token.length < 2) {
      continue;
    }
    for (const entry of INDEX_ALIASES) {
      if (entry.keys.some((k) => k.startsWith(token) || token.startsWith(k.replace(/\s/g, '')))) {
        if (!indexNames.includes(entry.name)) {
          indexNames.push(entry.name);
        }
      }
    }
  }

  const wantsOptions =
    strike != null ||
    optionSide != null ||
    /\b(ce|pe|call|put|option)\b/.test(lower) ||
    (indexNames.length > 0 && strike != null);

  return {
    raw: trimmed,
    tokens,
    alphaTokens,
    strike,
    optionSide,
    indexNames,
    wantsOptions,
  };
}

/** Best term for backend symbol catalog search (avoid sending full strike phrases). */
export function symbolSearchCatalogTerm(raw: string): string {
  const parsed = parseSymbolSearchQuery(raw);
  if (parsed.indexNames[0]) {
    return parsed.indexNames[0];
  }
  const candidates = parsed.alphaTokens.filter((t) => t.length >= 2);
  if (candidates.length) {
    return candidates.sort((a, b) => b.length - a.length)[0]!;
  }
  const compact = raw.trim();
  if (compact.length >= 2 && !/^\d+$/.test(compact)) {
    return compact;
  }
  return '';
}

export function symbolMatchesStockQuery(
  symbolName: string,
  extras: string[],
  parsed: ParsedSymbolSearchQuery,
): boolean {
  if (!parsed.raw) {
    return true;
  }
  const name = symbolName.toLowerCase();
  const haystack = [name, ...extras.map((e) => e.toLowerCase())].join(' ');
  const qCompact = parsed.raw.replace(/\s/g, '').toLowerCase();
  if (qCompact.length >= 2 && qCompact.length <= 6 && /^[a-z]+$/.test(qCompact)) {
    return name.startsWith(qCompact);
  }

  const tokens = parsed.alphaTokens.length ? parsed.alphaTokens : parsed.tokens.filter((t) => /[a-z]/.test(t));
  if (!tokens.length) {
    return false;
  }
  return tokens.every((token) => {
    if (token.length <= 3) {
      return name.startsWith(token) || name === token;
    }
    if (token.length < 2) {
      return name.startsWith(token);
    }
    return name.startsWith(token) || haystack.includes(token);
  });
}

/**
 * Prefer matched F&O stocks when user typed a symbol; otherwise index strikes from numbers.
 */
export function resolveOptionSearchUnderlyings(
  parsed: ParsedSymbolSearchQuery,
  matchedStockSymbols: readonly string[],
  indexTabs: readonly string[],
  lastPrice: (name: string) => number,
): string[] {
  const explicitIndexOnly =
    parsed.indexNames.length > 0 &&
    !matchedStockSymbols.some((s) => isFnoEquityUnderlying(s) && parsed.alphaTokens.some((t) => s.toLowerCase().startsWith(t)));

  if (!explicitIndexOnly) {
    const equities = matchedStockSymbols.filter((s) => isFnoEquityUnderlying(s));
    if (equities.length) {
      return [...new Set(equities.map((s) => s.toUpperCase()))].slice(0, 2);
    }
  }

  if (parsed.indexNames.length) {
    return parsed.indexNames;
  }

  return resolveSearchIndices(parsed, indexTabs, lastPrice);
}

/** Strike interval for search suggestions (equities use tighter steps when spot is lower). */
export function equityOptionStrikeStep(spot: number): number {
  if (spot >= 20_000) {
    return 100;
  }
  if (spot >= 5_000) {
    return 50;
  }
  if (spot >= 1_000) {
    return 20;
  }
  return 10;
}

/** Rough index guess when live spot is not loaded yet (IST index strike ranges). */
export function guessIndexForStrike(strike: number): string {
  if (strike >= 65_000) {
    return 'SENSEX';
  }
  if (strike >= 45_000) {
    return 'BANKNIFTY';
  }
  if (strike >= 28_000 && strike < 45_000) {
    return 'BANKNIFTY';
  }
  if (strike >= 18_000 && strike <= 28_000) {
    return 'NIFTY';
  }
  if (strike >= 15_000 && strike < 18_000) {
    return 'FINNIFTY';
  }
  return 'NIFTY';
}

export function rankIndicesForStrike(
  strike: number,
  indices: readonly string[],
  lastPrice: (name: string) => number,
): string[] {
  const ranked = [...indices]
    .map((name) => {
      const last = lastPrice(name);
      return { name, last, dist: last > 0 ? Math.abs(last - strike) : Number.POSITIVE_INFINITY };
    })
    .filter((row) => row.dist < Number.POSITIVE_INFINITY)
    .sort((a, b) => a.dist - b.dist)
    .map((row) => row.name);
  if (ranked.length) {
    return ranked;
  }
  const guess = guessIndexForStrike(strike);
  if (indices.includes(guess)) {
    return [guess];
  }
  return indices.length ? [indices[0]!] : [guess];
}

export function roundStrikeToStep(strike: number, step: number): number {
  if (step <= 0) {
    return strike;
  }
  return Math.round(strike / step) * step;
}

/** Digits-only prefix from query (e.g. "22" while typing a strike). */
export function digitPrefixFromQuery(raw: string): string | null {
  const digits = raw.replace(/[^\d]/g, '');
  if (digits.length >= 2 && digits.length < 6) {
    return digits;
  }
  return null;
}

/** Expand short digit prefix to a rough spot for index guessing (22 → 22000). */
export function pseudoStrikeFromDigitPrefix(prefix: string): number {
  if (!prefix) {
    return 0;
  }
  const n = Number(prefix);
  if (!Number.isFinite(n) || n <= 0) {
    return 0;
  }
  if (prefix.length >= 4) {
    return n;
  }
  return n * 10 ** (5 - prefix.length);
}

export function strikesMatchingDigitPrefix(
  prefix: string,
  step: number,
  spot: number,
  max = 8,
): number[] {
  if (!prefix || step <= 0) {
    return [];
  }
  const anchor = spot > 0 ? spot : pseudoStrikeFromDigitPrefix(prefix);
  const atm = roundStrikeToStep(anchor, step);
  const found: number[] = [];
  for (let i = -80; i <= 80; i++) {
    const strike = atm + i * step;
    if (strike > 0 && String(strike).startsWith(prefix)) {
      found.push(strike);
    }
  }
  found.sort((a, b) => Math.abs(a - anchor) - Math.abs(b - anchor));
  return [...new Set(found)].slice(0, max);
}

export function resolveSearchIndices(
  parsed: ParsedSymbolSearchQuery,
  allIndices: readonly string[],
  lastPrice: (name: string) => number,
): string[] {
  if (parsed.indexNames.length) {
    return parsed.indexNames;
  }
  if (parsed.strike != null) {
    const ranked = rankIndicesForStrike(parsed.strike, allIndices, lastPrice);
    return ranked.slice(0, 2);
  }
  const digitPrefix = digitPrefixFromQuery(parsed.raw);
  if (digitPrefix) {
    const pseudo = pseudoStrikeFromDigitPrefix(digitPrefix);
    return rankIndicesForStrike(pseudo, allIndices, lastPrice).slice(0, 2);
  }
  return [];
}

export function defaultIndexOrder(): readonly string[] {
  return DEFAULT_INDEX_ORDER;
}
