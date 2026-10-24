import {
  DEFAULT_PLATFORM_INSTRUMENT_FEATURES,
  InstrumentKind,
  ProductType,
  formatInstrumentTypeDisplay,
  futuresLiveRoutingAllowed,
  inferProductType,
  normalizeInstrumentType,
} from './instrument-type.util';

describe('instrument-type.util', () => {
  it('defaults missing type to OPTION', () => {
    expect(normalizeInstrumentType(null)).toBe(InstrumentKind.Option);
    expect(normalizeInstrumentType('')).toBe(InstrumentKind.Option);
    expect(normalizeInstrumentType('unknown')).toBe(InstrumentKind.Option);
  });

  it('detects futures explicitly', () => {
    expect(normalizeInstrumentType('FUTURE')).toBe(InstrumentKind.Future);
  });

  it('formats blank history instrument as OPTION', () => {
    expect(formatInstrumentTypeDisplay(null)).toBe('OPTION');
    expect(formatInstrumentTypeDisplay('')).toBe('OPTION');
  });

  it('infers index option product type', () => {
    expect(inferProductType('OPTION', 'NIFTY')).toBe(ProductType.IndexOption);
    expect(inferProductType('FUTURE', 'NIFTY')).toBe(ProductType.IndexFuture);
  });

  it('blocks live futures routing when feature flag off', () => {
    expect(
      futuresLiveRoutingAllowed([{ instrumentType: 'FUTURE' }], DEFAULT_PLATFORM_INSTRUMENT_FEATURES),
    ).toBe(false);
    expect(
      futuresLiveRoutingAllowed([{ instrumentType: 'OPTION' }], DEFAULT_PLATFORM_INSTRUMENT_FEATURES),
    ).toBe(true);
  });
});
