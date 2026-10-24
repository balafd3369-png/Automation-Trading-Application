import {
  ManualExecutionMode,
  ManualInstrumentType,
  ManualTransactionType,
} from '../core/manual-trading.models';

export interface TradeTicketPositionContext {
  tradeId: string;
  quantity: number;
  entryPrice: number;
  currentPrice: number;
  pnl: number;
  direction: string;
}

export interface TradeTicketContext {
  instrumentType: ManualInstrumentType;
  displayName: string;
  exchange: string;
  symbol: string;
  underlying: string;
  transactionType: ManualTransactionType;
  ltp?: number | null;
  previousClose?: number | null;
  strike?: number | null;
  optionRight?: 'CE' | 'PE' | null;
  expiry?: string | null;
  executionMode?: ManualExecutionMode;
  position?: TradeTicketPositionContext | null;
}

export type TradeTicketStep = 'form' | 'preview';
