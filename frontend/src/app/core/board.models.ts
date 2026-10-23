export type PressureMark = 'BUYING' | 'SELLING' | 'RANGE BOUND';

export interface BoardCounts {
  buying: number;
  selling: number;
  rangeBound: number;
}

export interface SymbolSearchHit {
  name: string;
  companyName: string;
  exchange: string;
}

export interface SymbolSnapshot {
  name: string;
  last: number;
  todayOpen: number;
  yesterdayHigh: number;
  yesterdayLow: number;
  highTwoDaysAgo: number;
  lowTwoDaysAgo: number;
  b2: number;
  b1: number;
  s: number;
  s1: number;
  s2: number;
  mark: PressureMark;
  tightRange: boolean;
  previousClose: number;
  /** False when m.Stock is connected but this symbol still has demo seed / unmapped quote. */
  quoteVerified?: boolean;
}

export interface BoardResponse {
  counts: BoardCounts;
  symbols: SymbolSnapshot[];
  updatedAt: string;
  feed: string;
}

export interface ChartBar {
  time: number;
  open: number;
  high: number;
  low: number;
  close: number;
}

export interface ChartLiveUpdate {
  name: string;
  interval: string;
  bar: ChartBar;
  last: number;
  b2: number;
  b1: number;
  s: number;
  s1: number;
  s2: number;
  serverTime: string;
}

export interface ChartResponse {
  name: string;
  tradingSymbol?: string;
  candles: ChartBar[];
  last: number;
  todayOpen?: number;
  previousClose?: number;
  b2: number;
  b1: number;
  s: number;
  s1: number;
  s2: number;
}

export interface OptionMarketQuote {
  strike: number;
  side: 'CE' | 'PE';
  tradingSymbol: string;
  last: number;
  open: number;
  previousClose: number;
  changePercent: number;
}

export interface PaperAgentConfig {
  allocatedCapital: number;
  profitTarget: number;
  maxDailyLoss: number;
  maxOpenPositions: number;
  autoAdmitTrades?: boolean;
}

export interface PaperPosition {
  id: string;
  instrumentType: string;
  symbol: string;
  direction: 'LONG' | 'SHORT';
  triggerReason: string;
  quantity: number;
  entryPrice: number;
  currentPrice: number;
  stopLoss: number;
  target: number;
  pnl: number;
  openedAt: string;
  strategy?: string;
  underlying?: string;
  maxRisk?: number;
  capitalRequired?: number;
  legs?: PaperPositionLeg[];
  exitDeadline?: string | null;
}

export interface PaperAgentMessage {
  role: 'user' | 'assistant';
  text: string;
  at: string;
}

export interface PaperTradeProposal {
  id: string;
  instrumentType: string;
  symbol: string;
  direction: 'LONG' | 'SHORT';
  quantity: number;
  indicativePrice: number;
  stopLoss: number;
  target: number;
  reason: string;
  expiresAt: string;
  strategy?: string;
  underlying?: string;
  evidenceScore?: number;
  maxRisk?: number;
  maxReward?: number;
  capitalRequired?: number;
  rewardRisk?: number;
  evidence?: string[];
  legs?: PaperTradeLeg[];
  dataTimestamp?: string | null;
}

export interface PaperAgentSnapshot {
  paperMode: true;
  running: boolean;
  state: string;
  brokerAvailableFunds: number | null;
  brokerUtilizedFunds: number | null;
  config: PaperAgentConfig | null;
  realizedPnl: number;
  unrealizedPnl: number;
  totalPnl: number;
  pendingTrade: PaperTradeProposal | null;
  positions: PaperPosition[];
  messages: PaperAgentMessage[];
  updatedAt: string;
  marketRegime?: MarketRegime | null;
  recentDecisions?: RankedDecision[];
  recentEvents?: PaperTradeEvent[];
  performance?: PaperPerformance | null;
  marketContext?: MarketContext[];
}

export interface QuoteMetadata {
  source: string;
  exchange: string;
  instrumentToken: number | null;
  dataTimestamp: string;
  ageSeconds: number;
  isFresh: boolean;
  bid: number | null;
  ask: number | null;
  volume: number | null;
  openInterest: number | null;
  missingFields: string[];
}

export interface MarketEvidence {
  emaFast: number | null;
  emaSlow: number | null;
  momentumPercent: number | null;
  realizedVolatilityPercent: number | null;
  trend: string;
  zone: string;
  regime: string;
  facts: string[];
}

export interface MarketContext {
  symbol: string;
  instrumentType: string;
  last: number;
  quote: QuoteMetadata;
  evidence: MarketEvidence;
}

export interface PaperTradeLeg {
  id: string;
  symbol: string;
  underlying: string;
  instrumentType: string;
  side: 'BUY' | 'SELL';
  ratio: number;
  quantity: number;
  indicativePrice: number;
  strike: number | null;
  optionRight: 'CE' | 'PE' | null;
  expiry: string | null;
  quote: QuoteMetadata | null;
}

export interface PaperPositionLeg {
  id: string;
  symbol: string;
  side: 'BUY' | 'SELL';
  quantity: number;
  entryPrice: number;
  currentPrice: number;
  pnl: number;
  strike: number | null;
  optionRight: 'CE' | 'PE' | null;
  expiry: string | null;
}

export interface RankedDecision {
  id: string;
  strategy: string;
  underlying: string;
  direction: string;
  evidenceScore: number;
  accepted: boolean;
  evidence: string[];
  rejections: string[];
  maxRisk: number;
  maxReward: number;
  rewardRisk: number;
  evaluatedAt: string;
  scoreMeaning: string;
}

export interface PaperTradeEvent {
  id: string;
  tradeId: string;
  type: string;
  reason: string;
  pnl: number | null;
  at: string;
}

export interface PaperPerformance {
  closedTrades: number;
  winningTrades: number;
  losingTrades: number;
  winRatePercent: number;
  realizedPnl: number;
  unrealizedPnl: number;
  grossRiskOpen: number;
  targetHits: number;
  stopLossHits: number;
  trailingStopHits: number;
  breakevenTrades: number;
}

export interface MarketRegime {
  regime: string;
  dataTimestamp: string;
  isFresh: boolean;
  evidence: string[];
}

export type TradeHistoryRange = 'today' | '1w' | '1m';

export interface TradeHistoryItem {
  id: string;
  userId: string;
  status: 'PROPOSED' | 'OPEN' | 'CLOSED' | 'REJECTED' | 'CANCELLED' | 'EXPIRED' | 'INTERRUPTED';
  instrumentType: string;
  symbol: string;
  direction: 'LONG' | 'SHORT';
  quantity: number;
  triggerReason: string;
  proposedPrice: number;
  entryPrice: number | null;
  exitPrice: number | null;
  stopLoss: number;
  target: number;
  pnl: number | null;
  exitReason: string | null;
  createdAt: string;
  openedAt: string | null;
  closedAt: string | null;
}

export interface TradeHistoryResponse {
  userId: string;
  range: TradeHistoryRange;
  from: string;
  to: string;
  summary: {
    closedTrades: number;
    winningTrades: number;
    losingTrades: number;
    openTrades: number;
    realizedPnl: number;
    targetHits: number;
    stopLossHits: number;
    trailingStopHits: number;
    breakevenTrades: number;
  };
  trades: TradeHistoryItem[];
}

export interface PaperTradeHistoryLeg {
  id: string;
  symbol: string;
  instrumentType: string;
  side: 'BUY' | 'SELL';
  quantity: number;
  proposedPrice: number;
  entryPrice: number | null;
  currentPrice: number | null;
  exitPrice: number | null;
  pnl: number | null;
  strike: number | null;
  optionRight: 'CE' | 'PE' | null;
  expiry: string | null;
}

export interface PaperTradeHistoryItem {
  id: string;
  status: TradeHistoryItem['status'];
  strategy: string;
  underlying: string;
  direction: string;
  stopLoss?: number;
  target?: number;
  maxRisk: number;
  maxReward: number;
  capitalRequired?: number;
  pnl: number | null;
  exitReason: string | null;
  entryReason?: string | null;
  createdAt: string;
  openedAt: string | null;
  closedAt: string | null;
  legs: PaperTradeHistoryLeg[];
}

export interface PaperTradeHistoryResponse {
  userId: string;
  range: TradeHistoryRange;
  summary: TradeHistoryResponse['summary'];
  trades: PaperTradeHistoryItem[];
}

export interface LiveOrder {
  id: string;
  tradeId: string;
  legId: string;
  brokerOrderId: string;
  exchange: string;
  symbol: string;
  transactionType: 'BUY' | 'SELL';
  product: 'MIS' | 'NRML';
  purpose: 'ENTRY' | 'EXIT' | 'COMPENSATION';
  status: string;
  quantity: number;
  filledQuantity: number;
  averagePrice: number;
  brokerMessage: string;
  tag: string;
  updatedAt: string;
}

export interface LiveTrade {
  id: string;
  strategy: string;
  underlying: string;
  product: 'MIS' | 'NRML';
  status: string;
  failureReason: string | null;
  createdAt: string;
  orders: LiveOrder[];
}

export interface BrokerPosition {
  exchange: string;
  tradingSymbol: string;
  product: string;
  quantity: number;
  overnightQuantity: number;
  averagePrice: number;
  lastPrice: number;
  pnl: number;
}

export interface BrokerOrder {
  orderId: string;
  exchangeOrderId: string;
  exchange: string;
  tradingSymbol: string;
  transactionType: string;
  product: string;
  orderType: string;
  status: string;
  statusMessage: string;
  quantity: number;
  filledQuantity: number;
  pendingQuantity: number;
  price: number;
  triggerPrice: number;
  averagePrice: number;
  tag: string;
  updatedAt: string | null;
}

export interface LiveBrokerPnl {
  totalPnl: number;
  realisedPnl: number;
  unrealisedPnl: number;
  unrealisedPercent: number | null;
}

export interface LiveEodPrompt {
  tradeId: string;
  underlying: string;
  product: string;
  expiresAt: string;
  message: string;
}

export interface LiveExecutionEvent {
  id: string;
  tradeId: string;
  type: string;
  message: string;
  at: string;
}

export interface MarketSession {
  phase: string;
  isOpen: boolean;
  message: string;
  nextEventAt: string | null;
}

export interface LiveExecutionSnapshot {
  featureEnabled: boolean;
  brokerConnected: boolean;
  marketSession: MarketSession;
  armed: boolean;
  armedUntil: string | null;
  zoneAutoLiveArmed: boolean;
  zoneAutoLiveProduct: string;
  state: string;
  pendingProposal: PaperTradeProposal | null;
  trades: LiveTrade[];
  brokerPositions: BrokerPosition[];
  brokerOrders: BrokerOrder[];
  brokerPnl: LiveBrokerPnl | null;
  events: LiveExecutionEvent[];
  eodPrompts: LiveEodPrompt[];
  reconciliationStatus: string;
  updatedAt: string;
}

export const STOCK_B1_REJECTION_STRATEGY = 'STOCK_B1_REJECTION_TO_S2';

export interface ZoneRejectionBacktestResult {
  symbol: string;
  pattern: string;
  samples: number;
  wins: number;
  losses: number;
  winRatePercent: number;
  avgRewardPoints: number;
  avgRiskPoints: number;
}

export interface ZoneRejectionBacktestResponse {
  result: ZoneRejectionBacktestResult;
  qualifies: boolean;
  minWinRate: number;
  minSamples: number;
}
