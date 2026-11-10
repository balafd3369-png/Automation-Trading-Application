export interface UserAnalyticsSummary {
  totalTrades: number;
  winRate: number;
  profitFactor: number | null;
  netPnL: number;
  bestTradePnL: number | null;
  worstTradePnL: number | null;
  bestDayPnL: number | null;
  worstDayPnL: number | null;
}

export interface UserDashboardSummary {
  paperCapital: number;
  availableCapital: number;
  openPositions: number;
  closedPositions: number;
  tradeCount: number;
  winRate: number;
  realizedPnL: number;
  unrealizedPnL: number;
  bestTradePnL: number | null;
  worstTradePnL: number | null;
  updatedAt: string;
}

export interface AdminPlatformDashboard {
  totalUsers: number;
  activeUsers: number;
  onlineUsers: number;
  todaysLogins: number;
  paperTradesToday: number;
  openPaperPositions: number;
  brokerConnected: boolean;
  brokerStatus: string;
  topUsers: AdminTopUserRow[];
  updatedAt: string;
}

export interface AdminTopUserRow {
  userId: string;
  displayName: string;
  email: string;
  netPnL: number;
  tradeCount: number;
}

export interface OpenVsZonesRow {
  name: string;
  isIndex: boolean;
  todayOpen: number;
  last: number;
  b2: number;
  b1: number;
  s: number;
  s1: number;
  s2: number;
  mark: string;
  tightRange: boolean;
  openAboveB2: boolean;
  openAboveB1: boolean;
  openBelowS1: boolean;
  openBelowS2: boolean;
  openBand: string;
  openBandKey: OpenVsZoneBandKey;
  ltpBand: string;
  ltpBandKey: OpenVsZoneBandKey;
  sessionMove: string;
}

export type OpenVsZoneBandKey =
  | 'aboveB2'
  | 'aboveB1'
  | 'betweenS1B1'
  | 'betweenS1S2'
  | 'belowS1'
  | 'belowS2'
  | 'unknown';

export interface OpenVsZonesBoardResponse {
  updatedAt: string;
  feed: string;
  indices: OpenVsZonesRow[];
  stocks: OpenVsZonesRow[];
}

export interface ZoneStrategyPlaybookCard {
  patternId: string;
  title: string;
  direction: string;
  when: string;
  target: string;
  stop: string;
}

export interface ZoneStockAnalyzerRow {
  name: string;
  isIndex: boolean;
  action: string;
  patternId: string;
  headline: string;
  detail: string;
  confidence: string;
  qualityScore: number;
  grade: string;
  riskClass: string;
  entryTiming: string;
  triggerDistancePercent: number;
  whyBullets: string[];
  mtfIntraday: string;
  mtfDaily: string;
  mtfWeekly: string;
  mtfAlignmentScore: number;
  todayOpen: number;
  last: number;
  openBand: string;
  ltpBand: string;
  sessionMove: string;
  mark: string;
  tightRange: boolean;
  b1: number;
  b2: number;
  s1: number;
  s2: number;
  s: number;
  targetPrice: number;
  stopPrice: number;
}

export interface ZoneAnalyzerTopOpportunity {
  name: string;
  action: string;
  patternId: string;
  qualityScore: number;
  grade: string;
  riskClass: string;
}

export interface ZoneStockAnalyzerResponse {
  analyzedAt: string;
  feed: string;
  counts: { buying: number; selling: number; rangeBound: number };
  breadthNote: string;
  marketRegime: string;
  topOpportunities: ZoneAnalyzerTopOpportunity[];
  playbook: ZoneStrategyPlaybookCard[];
  buy: ZoneStockAnalyzerRow[];
  sell: ZoneStockAnalyzerRow[];
  watch: ZoneStockAnalyzerRow[];
}

export interface ReversalPlaybook {
  title: string;
  stages: string[];
  intradayPatternId: string;
  continuationPatternId: string;
}

export interface ReversalClusterStats {
  stocksScanned: number;
  failedMorningCount: number;
  activeFadeCount: number;
  gapContinuationCount: number;
  earlyWarningCount: number;
  gapDownOpens: number;
  gapUpOpens: number;
  boardBuying: number;
  boardSelling: number;
  breadthNote: string;
}

export interface ReversalScanRow {
  name: string;
  patternId: string;
  stage: string;
  score: number;
  detail: string;
  gapVsPrevClosePercent: number;
  openBand: string;
  ltpBand: string;
  sessionMove: string;
  mark: string;
  todayOpen: number;
  last: number;
  previousClose: number;
  b1: number;
  s1: number;
  s2: number;
}

export interface GapAnalyzerFeaturedClue {
  clueId: string;
  title: string;
  priorRule: string;
  tradeRule: string;
  backtestWinRatePercent: number;
  backtestWins: number;
  backtestLosses: number;
  totalGapEvents: number;
  symbolsWithEvents: number;
  qualifies: boolean;
  notes: string[];
}

export interface GapAnalyzerPlaybookRow {
  clueId: string;
  catalogId: string;
  title: string;
  priorRule: string;
  gapRule: string;
  tradeRule: string;
  action: string;
  paperAuto: boolean;
  backtestWinRatePercent: number;
  backtestWins: number;
  backtestLosses: number;
  totalGapEvents: number;
  avgContinuationRatePercent: number;
  symbolsWithEvents: number;
  qualifies: boolean;
}

export interface GapAnalyzerTradeRow {
  name: string;
  action: string;
  clueId: string;
  headline: string;
  gapVsPrevClosePercent: number;
  backtestWinRatePercent: number;
  backtestWins: number;
  backtestLosses: number;
  backtestEvents: number;
  qualityScore: number;
  grade: string;
  liveActionable: boolean;
  todayOpen: number;
  last: number;
  previousClose: number;
  b1: number;
  s1: number;
  s2: number;
  targetPrice: number;
  stopPrice: number;
  openBand: string;
  ltpBand: string;
  sessionMove: string;
  mark: string;
  whyBullets: string[];
  entryHint: string;
}

export interface GapAnalyzerResponse {
  analyzedAt: string;
  feed: string;
  featured: GapAnalyzerFeaturedClue;
  playbooks: GapAnalyzerPlaybookRow[];
  lookbackDays: number;
  benchmarkNote: string;
  trades: GapAnalyzerTradeRow[];
  liveActionableCount: number;
}

export interface GapStudyClueSummary {
  clueId: string;
  title: string;
  priorSessionRule: string;
  gapRule: string;
  tradeRule: string;
  totalEvents: number;
  symbolsWithEvents: number;
  avgContinuationRatePercent: number;
  tradeWins: number;
  tradeLosses: number;
  tradeWinRatePercent: number;
  qualifies: boolean;
}

export interface GapStudyUniverseReport {
  universe: string;
  symbolsProcessed: number;
  lookbackSessionDaysHint: number;
  clues: GapStudyClueSummary[];
  verdict: string;
}

export interface GapStudyUniverseResponse {
  lookbackDays: number;
  report: GapStudyUniverseReport;
  symbolsAnalyzed: string[];
}

export interface ReversalScanResponse {
  analyzedAt: string;
  feed: string;
  playbook: ReversalPlaybook;
  stats: ReversalClusterStats;
  activeFades: ReversalScanRow[];
  gapContinuations: ReversalScanRow[];
  earlyWarnings: ReversalScanRow[];
  failedMorningOpens: ReversalScanRow[];
}

export interface ZoneAnalyzerPatternDetailResponse {
  symbol: string;
  patternId: string;
  lookbackDays: number;
  winRatePercent: number;
  samples: number;
  wins: number;
  losses: number;
  avgRewardPoints: number;
  avgRiskPoints: number;
  rewardRiskRatio: number | null;
  last20Wins: number;
  last20Losses: number;
  confidenceLabel: string;
  qualifiesBacktest: boolean;
  adaptiveSignalsLogged: number;
  adaptiveWinRatePercent: number | null;
}

export interface AuthConfigResponse {
  requireAuthentication: boolean;
  allowAnonymousLocalUser: boolean;
  registrationMode?: string;
}
