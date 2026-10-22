import { PaperTradeHistoryItem } from './board.models';

export interface StrategyPhase {
  title: string;
  timeIst: string;
  description: string;
}

export interface StrategyTradePlan {
  name: string;
  direction: string;
  validFromIst: string;
  trigger: string;
  target: string;
  stopLoss: string;
  reentry: string;
}

export interface StrategyRiskRules {
  maxAttemptsPerDay: string;
  notes: string;
}

export interface StrategyPlaybook {
  intent: string;
  phases: StrategyPhase[];
  primarySetup: StrategyTradePlan;
  alternateSetup: StrategyTradePlan | null;
  risk: StrategyRiskRules;
}

export interface StrategyCatalogEntry {
  id: string;
  name: string;
  author: string;
  summary: string;
  timeframe: string;
  session: string;
  status: 'catalog' | 'paper-ready' | 'planned' | string;
  tags: string[];
  playbook: StrategyPlaybook;
}

export const TEN_AM_HIGH_FADE_ID = 'bala-10am-high-fade';
/** @deprecated Use TEN_AM_HIGH_FADE_ID */
export const BALA_10AM_HIGH_FADE_ID = TEN_AM_HIGH_FADE_ID;
export const TEN_AM_LOW_BREAK_TO_DAY_OPEN_ID = 'ten-am-low-break-to-day-open';
export const STRATEGY_SCAN_INTERVAL = '15minute';
export const STRATEGY_STOP_BUFFER_POINTS = 10;

export interface ZoneStrategyRolloutRow {
  strategyCode: string;
  strategyLabel: string;
  index: string;
  phase: 'monitor' | 'paper_auto' | 'live_blocked' | string;
  reason: string;
}

export interface ZoneStrategyRolloutResponse {
  policyVersion: string;
  lookbackDays: number;
  minWinRatePercent: number;
  minSamples: number;
  liveTradingNote: string;
  rows: ZoneStrategyRolloutRow[];
}

export interface StrategyLiveRecommendation {
  id: string;
  strategyId: string;
  strategyName: string;
  symbol: string;
  direction: string;
  headline: string;
  status: string;
  entryPrice: number;
  stopLoss: number;
  target: number;
  riskPoints: number;
  rewardPoints: number;
  maxRiskInr: number;
  maxRewardInr: number;
  rewardRisk: number;
  whyNow: string[];
  ifTargetHit: string[];
  ifStopHit: string[];
  signalBarTime: number;
  detectedAt: string;
  canPaperTrade: boolean;
}

export interface StrategyMonthlyStat {
  strategyId: string;
  strategyName: string;
  tradesThisMonth: number;
  closedTrades: number;
  winningTrades: number;
  realizedPnl: number;
  peakLiveCapital: number;
  winRatePercent: number;
}

export interface StrategyTrackRecordRow {
  strategyId: string;
  strategyName: string;
  trades: number;
  closedTrades: number;
  openTrades: number;
  winningTrades: number;
  losingTrades: number;
  winRatePercent: number;
  realizedPnl: number;
  totalMaxRisk: number;
  totalMaxReward: number;
  peakLiveCapital: number;
  avgLiveCapital: number;
}

export interface StrategyTrackRecordResponse {
  monthLabel: string;
  from: string;
  to: string;
  overall: StrategyTrackRecordRow;
  byStrategy: StrategyTrackRecordRow[];
  trades: PaperTradeHistoryItem[];
}

export interface StrategyFeedResponse {
  liveNow: StrategyLiveRecommendation[];
  strategies: StrategyCatalogEntry[];
  monthlyStats: StrategyMonthlyStat[];
  updatedAt: string;
}
