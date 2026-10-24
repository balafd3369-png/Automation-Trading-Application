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

export interface AuthConfigResponse {
  requireAuthentication: boolean;
  allowAnonymousLocalUser: boolean;
  registrationMode?: string;
}
