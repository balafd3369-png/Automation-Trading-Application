export type TradeSource = 'STRATEGY' | 'MANUAL';
export type ManualExecutionMode = 'PAPER' | 'LIVE';
export type ManualOrderType = 'MARKET' | 'LIMIT';
export type ManualTransactionType = 'BUY' | 'SELL';
export type ManualInstrumentType = 'STOCK' | 'OPTION' | 'FUTURE';

export interface ManualCapitalSummary {
  allocatedCapital: number | null;
  deployedCapital: number;
  availableCapital: number;
  brokerAvailableFunds: number | null;
}

export interface ManualOrder {
  id: string;
  executionMode: ManualExecutionMode;
  tradeSource: TradeSource;
  status: string;
  symbol: string;
  underlying: string;
  instrumentType: string;
  transactionType: ManualTransactionType;
  orderType: ManualOrderType;
  product: string;
  exchange: string;
  quantity: number;
  limitPrice: number | null;
  filledQuantity: number;
  averagePrice: number;
  brokerOrderId: string | null;
  tradeId: string | null;
  message: string;
  createdAt: string;
  updatedAt: string;
}

export interface ManualPosition {
  tradeId: string;
  tradeSource: TradeSource;
  symbol: string;
  underlying: string;
  instrumentType: string;
  direction: string;
  quantity: number;
  entryPrice: number;
  currentPrice: number;
  pnl: number;
  capitalRequired: number;
  product: string;
  openedAt: string;
}

export interface ManualHolding {
  symbol: string;
  exchange: string;
  quantity: number;
  averagePrice: number;
  lastPrice: number;
  marketValue: number;
  pnl: number;
}

export interface ManualTradeHistoryItem {
  id: string;
  status: string;
  strategy: string;
  underlying: string;
  direction: string;
  pnl: number | null;
  exitReason: string | null;
  createdAt: string;
  openedAt: string | null;
  closedAt: string | null;
  quantity: number;
  entryPrice: number;
  exitPrice: number | null;
  tradeSource: TradeSource;
}

export interface ManualTradingSnapshot {
  executionMode: ManualExecutionMode;
  capital: ManualCapitalSummary;
  orders: ManualOrder[];
  positions: ManualPosition[];
  holdings: ManualHolding[];
  tradeHistory: ManualTradeHistoryItem[];
  updatedAt: string;
}

export interface ManualPlaceOrderRequest {
  executionMode: ManualExecutionMode;
  instrumentType: ManualInstrumentType;
  symbol: string;
  underlying: string;
  transactionType: ManualTransactionType;
  orderType: ManualOrderType;
  quantity: number;
  product: string;
  limitPrice?: number | null;
  strike?: number | null;
  optionRight?: string | null;
  expiry?: string | null;
}
