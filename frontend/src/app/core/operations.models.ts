export type OperationsTab =
  | 'risk'
  | 'emergency'
  | 'orders'
  | 'positions'
  | 'holdings'
  | 'journal'
  | 'timeline'
  | 'notifications'
  | 'broker'
  | 'reconciliation';

export type OperationsRiskHealth = 'Healthy' | 'Watch' | 'RiskLimitApproaching';
export type OperationsTradingMode = 'Disarmed' | 'ManualLiveArmed' | 'ZoneAutoLiveArmed';
export type OperationsBrokerHealthStatus = 'Healthy' | 'Delayed' | 'Disconnected';

export interface OperationsRiskDashboard {
  account: {
    totalCapital: number;
    availableCapital: number;
    capitalUsed: number;
    marginAvailable: number | null;
    marginUsed: number | null;
    dailyPnl: number;
    realizedPnl: number;
    unrealizedPnl: number;
  };
  exposure: {
    totalExposure: number;
    stockExposure: number;
    optionExposure: number;
    futureExposure: number;
  };
  positionRisk: {
    openPositions: number;
    openRisk: number;
    maxRisk: number;
    largestPositionSymbol: string | null;
    largestPositionExposure: number;
    largestLosingPositionSymbol: string | null;
    largestLosingPnl: number;
  };
  tradingMode: OperationsTradingMode;
  tradingModeLabel: string;
  limits: {
    maxDailyLoss: number;
    maxOpenPositions: number;
    capitalAllocation: number;
    remainingCapacity: number;
    openPositions: number;
  };
  health: OperationsRiskHealth;
  healthLabel: string;
  updatedAt: string;
}

export interface OperationsOrderRow {
  orderId: string;
  symbol: string;
  side: string;
  quantity: number;
  price: number;
  orderType: string;
  status: string;
  time: string | null;
  source: string;
  executionMode: string;
}

export interface OperationsOrdersBook {
  open: OperationsOrderRow[];
  completed: OperationsOrderRow[];
  cancelled: OperationsOrderRow[];
  rejected: OperationsOrderRow[];
}

export interface OperationsPositionRow {
  id: string;
  symbol: string;
  instrumentType: string;
  quantity: number;
  entryPrice: number;
  currentPrice: number;
  pnl: number;
  source: string;
  status: string;
  executionMode: string;
}

export interface OperationsPositionsBook {
  open: OperationsPositionRow[];
  closed: OperationsPositionRow[];
}

export interface OperationsHoldingRow {
  symbol: string;
  quantity: number;
  averagePrice: number;
  currentValue: number;
  pnl: number;
  source: string;
}

export interface OperationsJournalEntry {
  id: string;
  tradeSource: string;
  strategyName: string;
  instrumentType: string;
  symbol: string;
  side: string;
  entryPrice: number | null;
  exitPrice: number | null;
  pnl: number | null;
  openTime: string | null;
  closeTime: string | null;
  notes: string | null;
  executionMode: string;
  status: string;
}

export interface OperationsTimelineEntry {
  id: string;
  category: string;
  title: string;
  detail: string;
  at: string;
}

export interface OperationsAuditEntry {
  id: string;
  category: string;
  eventType: string;
  message: string;
  at: string;
}

export interface OperationsNotification {
  id: string;
  severity: string;
  eventType: string;
  message: string;
  read: boolean;
  at: string;
}

export interface OperationsBrokerHealth {
  status: OperationsBrokerHealthStatus;
  statusLabel: string;
  connected: boolean;
  lastHeartbeat: string | null;
  openOrdersCount: number;
  failedOrdersCount: number;
  todaysOrdersCount: number;
  latencyMs: number | null;
  lastSyncAt: string | null;
}

export interface OperationsBrokerPortfolio {
  connected: boolean;
  provider: string;
  syncHealth: string;
  lastSyncAt: string | null;
  availableFunds: number | null;
  usedMargin: number | null;
  availableCash: number | null;
}

export interface OperationsEmergencyResult {
  success: boolean;
  message: string;
  audit: OperationsAuditEntry | null;
}

export const OPERATIONS_EMERGENCY_CONFIRM = 'CONFIRM EMERGENCY ACTION';

export type BrokerReconciliationHealth =
  | 'Skipped'
  | 'Healthy'
  | 'IssuesFound'
  | 'BrokerError';

export interface BrokerReconciliationMismatch {
  kind: string;
  symbol: string;
  applicationState: string;
  brokerState: string;
  reason: string;
  suggestedAction: string | null;
}

export interface BrokerReconciliationDashboard {
  health: BrokerReconciliationHealth;
  healthLabel: string;
  brokerConnected: boolean;
  positionMatches: number;
  positionMismatches: number;
  orderMatches: number;
  orderMismatches: number;
  issueCount: number;
  lastRunAt: string | null;
  brokerFetchMessage: string | null;
  mismatches: BrokerReconciliationMismatch[];
  suggestedActions: string[];
}

export interface BrokerReconciliationHistoryEntry {
  id: string;
  at: string;
  result: BrokerReconciliationHealth;
  resultLabel: string;
  issueCount: number;
  positionMatches: number;
  positionMismatches: number;
  orderMatches: number;
  orderMismatches: number;
  mismatches: BrokerReconciliationMismatch[];
}
