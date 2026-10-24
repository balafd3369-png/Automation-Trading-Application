/**
 * Additive instrument model for UI. Wire values remain backend strings (OPTION, FUTURE, …).
 * Default is Option for legacy / missing types — does not alter execution.
 */
export enum InstrumentKind {
  Option = 'OPTION',
  Future = 'FUTURE',
  Stock = 'STOCK',
  Index = 'INDEX',
  MultiLeg = 'MULTI_LEG',
}

/** Finer product class for future contract routing (not used in execution yet). */
export enum ProductType {
  Unknown = 'Unknown',
  IndexOption = 'IndexOption',
  StockOption = 'StockOption',
  IndexFuture = 'IndexFuture',
  StockFuture = 'StockFuture',
  IndexCash = 'IndexCash',
  StockCash = 'StockCash',
}

const INDEX_UNDERLYINGS = new Set(['NIFTY', 'BANKNIFTY', 'FINNIFTY', 'SENSEX']);

export function inferProductType(
  instrumentType: string | null | undefined,
  underlying: string | null | undefined,
): ProductType {
  const kind = normalizeInstrumentType(instrumentType);
  const index = underlying != null && INDEX_UNDERLYINGS.has(underlying.trim().toUpperCase());
  switch (kind) {
    case InstrumentKind.Future:
      return index ? ProductType.IndexFuture : ProductType.StockFuture;
    case InstrumentKind.Option:
      return index ? ProductType.IndexOption : ProductType.StockOption;
    case InstrumentKind.Stock:
      return ProductType.StockCash;
    case InstrumentKind.Index:
      return ProductType.IndexCash;
    case InstrumentKind.MultiLeg:
      return index ? ProductType.IndexOption : ProductType.StockOption;
    default:
      return ProductType.Unknown;
  }
}

/** UI/history: never show blank/unknown — catalog trades default to OPTION. */
export function formatInstrumentTypeDisplay(raw: string | null | undefined): string {
  return normalizeInstrumentType(raw);
}

export interface PlatformInstrumentFeatures {
  futuresEnabled: boolean;
  defaultInstrumentType: string;
}

export const DEFAULT_PLATFORM_INSTRUMENT_FEATURES: PlatformInstrumentFeatures = {
  futuresEnabled: false,
  defaultInstrumentType: InstrumentKind.Option,
};

/** Parse API instrument type; default Option unless explicitly FUTURE / STOCK / INDEX / MULTI_LEG. */
export function normalizeInstrumentType(raw: string | null | undefined): InstrumentKind {
  const key = (raw ?? '').trim().toUpperCase();
  switch (key) {
    case InstrumentKind.Future:
      return InstrumentKind.Future;
    case InstrumentKind.Stock:
      return InstrumentKind.Stock;
    case InstrumentKind.Index:
      return InstrumentKind.Index;
    case InstrumentKind.MultiLeg:
      return InstrumentKind.MultiLeg;
    case InstrumentKind.Option:
    case 'CE':
    case 'PE':
      return InstrumentKind.Option;
    default:
      return InstrumentKind.Option;
  }
}

export function instrumentKindLabel(kind: InstrumentKind): string {
  switch (kind) {
    case InstrumentKind.Future:
      return 'Future';
    case InstrumentKind.Stock:
      return 'Stock';
    case InstrumentKind.Index:
      return 'Index';
    case InstrumentKind.MultiLeg:
      return 'Multi-leg';
    default:
      return 'Option';
  }
}

export function futuresLiveRoutingAllowed(
  legs: { instrumentType?: string }[],
  features: PlatformInstrumentFeatures = DEFAULT_PLATFORM_INSTRUMENT_FEATURES,
): boolean {
  if (features.futuresEnabled) {
    return true;
  }
  return !legs.some((leg) => normalizeInstrumentType(leg.instrumentType) === InstrumentKind.Future);
}
