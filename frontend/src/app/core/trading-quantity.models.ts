import { ManualInstrumentType } from './manual-trading.models';

export interface TradingQuantityRules {
  instrumentType: string;
  underlying: string;
  symbol: string;
  lotSize: number;
  stepSize: number;
  defaultQuantity: number;
  defaultLots: number;
  usesLots: boolean;
  quantityUnitLabel: string;
}

export interface TradingQuantityRulesQuery {
  instrumentType: ManualInstrumentType;
  underlying: string;
  symbol: string;
}
