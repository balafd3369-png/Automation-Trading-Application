import { DatePipe, DecimalPipe, NgClass } from '@angular/common';
import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  DestroyRef,
  Input,
  OnChanges,
  OnInit,
  SimpleChanges,
  inject,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { catchError, forkJoin, interval, of, startWith, switchMap } from 'rxjs';
import { AppOperationsTab, routerPathForState } from '../core/app-navigation.util';
import { BoardService } from '../core/board.service';
import {
  OPERATIONS_EMERGENCY_CONFIRM,
  OperationsBrokerHealth,
  OperationsBrokerPortfolio,
  OperationsHoldingRow,
  OperationsJournalEntry,
  OperationsNotification,
  OperationsOrdersBook,
  OperationsPositionRow,
  OperationsPositionsBook,
  OperationsBrokerHealthStatus,
  OperationsRiskHealth,
  OperationsRiskDashboard,
  OperationsTimelineEntry,
  OperationsAuditEntry,
  BrokerReconciliationDashboard,
  BrokerReconciliationHistoryEntry,
} from '../core/operations.models';
import { OperationsNotificationService } from '../core/operations-notification.service';
import { TradeTicketService } from '../core/trade-ticket.service';
import { tradeTicketExchange } from '../core/trade-ticket.util';
import { ManualExecutionMode, ManualInstrumentType } from '../core/manual-trading.models';
import { InstrumentKind, normalizeInstrumentType } from '../core/instrument-type.util';
import { StrategiesPageStore } from '../strategies/strategies-page.store';

@Component({
  selector: 'app-operations-suite',
  standalone: true,
  imports: [DecimalPipe, DatePipe, FormsModule, NgClass],
  templateUrl: './operations-suite.component.html',
  styleUrl: './operations-suite.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class OperationsSuiteComponent implements OnInit, OnChanges {
  private readonly board = inject(BoardService);
  private readonly router = inject(Router);
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly destroyRef = inject(DestroyRef);
  private readonly notify = inject(OperationsNotificationService);
  private readonly tradeTickets = inject(TradeTicketService);
  private readonly strategiesStore = inject(StrategiesPageStore);

  @Input() tab: AppOperationsTab = 'risk';
  @Input() active = true;

  readonly tabs: { id: AppOperationsTab; label: string }[] = [
    { id: 'risk', label: 'Risk' },
    { id: 'emergency', label: 'Emergency' },
    { id: 'orders', label: 'Orders' },
    { id: 'positions', label: 'Positions' },
    { id: 'holdings', label: 'Holdings' },
    { id: 'journal', label: 'Journal' },
    { id: 'timeline', label: 'Timeline' },
    { id: 'notifications', label: 'Alerts' },
    { id: 'broker', label: 'Broker' },
    { id: 'reconciliation', label: 'Reconciliation' },
  ];

  risk: OperationsRiskDashboard | null = null;
  orders: OperationsOrdersBook | null = null;
  positions: OperationsPositionsBook | null = null;
  holdings: OperationsHoldingRow[] = [];
  journal: OperationsJournalEntry[] = [];
  timeline: OperationsTimelineEntry[] = [];
  notifications: OperationsNotification[] = [];
  audit: OperationsAuditEntry[] = [];
  brokerHealth: OperationsBrokerHealth | null = null;
  brokerPortfolio: OperationsBrokerPortfolio | null = null;
  reconciliation: BrokerReconciliationDashboard | null = null;
  reconciliationHistory: BrokerReconciliationHistoryEntry[] = [];

  orderTab = 'open' as 'open' | 'completed' | 'cancelled' | 'rejected';
  positionTab = 'open' as 'open' | 'closed';
  timelineFilter = '';
  journalSource = '';
  journalSymbol = '';
  emergencyConfirm = '';
  emergencyMessage = '';
  statusMessage = '';
  recentTimeline: OperationsTimelineEntry[] = [];
  coreLoading = true;

  ngOnInit(): void {
    this.strategiesStore
      .watchZoneRollout()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.cdr.markForCheck());
    this.notify
      .startPolling()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((list) => this.notify.ingest(list));

    interval(10_000)
      .pipe(
        startWith(0),
        switchMap(() => {
          if (!this.active) {
            return of(null);
          }
          return this.refreshCore();
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe(() => this.cdr.markForCheck());

    this.loadTabData();
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['tab'] && this.active) {
      this.loadTabData();
    }
  }

  selectTab(id: AppOperationsTab): void {
    void this.router.navigateByUrl(
      routerPathForState({
        centerView: 'operations',
        operationsTab: id,
        tradePanelTab: 'paper',
        marketKind: 'stocks',
        marketNavTab: 'dashboard',
        strategyDetailId: null,
      }),
    );
  }

  loadTabData(): void {
    switch (this.tab) {
      case 'orders':
        this.board.operationsOrders().subscribe((o) => {
          this.orders = o;
          this.cdr.markForCheck();
        });
        break;
      case 'positions':
        this.board.operationsPositions().subscribe((p) => {
          this.positions = p;
          this.cdr.markForCheck();
        });
        break;
      case 'holdings':
        this.board.operationsHoldings().subscribe((h) => {
          this.holdings = h;
          this.cdr.markForCheck();
        });
        break;
      case 'journal':
        this.loadJournal();
        break;
      case 'timeline':
        this.loadTimeline();
        break;
      case 'notifications':
        this.loadNotifications();
        break;
      case 'broker':
        this.loadBroker();
        break;
      case 'reconciliation':
        this.loadReconciliation();
        break;
      default:
        break;
    }
  }

  private refreshCore() {
    return forkJoin({
      risk: this.board.operationsRisk().pipe(catchError(() => of(null))),
      health: this.board.operationsBrokerHealth().pipe(catchError(() => of(null))),
      reconciliation: this.board.operationsReconciliation().pipe(catchError(() => of(null))),
      orders: this.board.operationsOrders().pipe(catchError(() => of(null))),
      positions: this.board.operationsPositions().pipe(catchError(() => of(null))),
      notes: this.board.operationsNotifications(40).pipe(catchError(() => of([] as OperationsNotification[]))),
      timeline: this.board.operationsTimeline().pipe(catchError(() => of([] as OperationsTimelineEntry[]))),
      reconHistory: this.board.operationsReconciliationHistory(12).pipe(
        catchError(() => of([] as BrokerReconciliationHistoryEntry[])),
      ),
      audit: this.board.operationsAudit(30).pipe(catchError(() => of([] as OperationsAuditEntry[]))),
    }).pipe(
      switchMap((payload) => {
        this.risk = payload.risk;
        this.brokerHealth = payload.health;
        this.reconciliation = payload.reconciliation;
        if (payload.orders) {
          this.orders = payload.orders;
        }
        if (payload.positions) {
          this.positions = payload.positions;
        }
        this.notifications = payload.notes;
        this.recentTimeline = payload.timeline.slice(0, 8);
        this.reconciliationHistory = payload.reconHistory;
        this.audit = payload.audit;
        this.coreLoading = false;
        if (this.tab === 'reconciliation' && !payload.reconciliation) {
          this.loadReconciliation();
        }
        return of(null);
      }),
    );
  }

  loadJournal(): void {
    const params: Record<string, string> = {};
    if (this.journalSource) {
      params['source'] = this.journalSource;
    }
    if (this.journalSymbol) {
      params['symbol'] = this.journalSymbol;
    }
    this.board.operationsJournal(params).subscribe((j) => {
      this.journal = j;
      this.cdr.markForCheck();
    });
  }

  loadTimeline(): void {
    this.board.operationsTimeline(this.timelineFilter || undefined).subscribe((t) => {
      this.timeline = t;
      this.cdr.markForCheck();
    });
  }

  loadNotifications(): void {
    forkJoin({
      notes: this.board.operationsNotifications(100),
      audit: this.board.operationsAudit(100),
    }).subscribe(({ notes, audit }) => {
      this.notifications = notes;
      this.audit = audit;
      this.cdr.markForCheck();
    });
  }

  loadBroker(): void {
    forkJoin({
      health: this.board.operationsBrokerHealth(),
      portfolio: this.board.operationsBrokerPortfolio().pipe(catchError(() => of(null))),
    }).subscribe(({ health, portfolio }) => {
      this.brokerHealth = health;
      this.brokerPortfolio = portfolio;
      this.cdr.markForCheck();
    });
  }

  loadReconciliation(): void {
    forkJoin({
      dashboard: this.board.operationsReconciliation().pipe(catchError(() => of(null))),
      history: this.board.operationsReconciliationHistory(40).pipe(catchError(() => of([]))),
    }).subscribe(({ dashboard, history }) => {
      this.reconciliation = dashboard;
      this.reconciliationHistory = history;
      this.cdr.markForCheck();
    });
  }

  reconciliationHealthClass(): string {
    const h = this.reconciliation?.health;
    if (h === 'Healthy') {
      return 'recon-healthy';
    }
    if (h === 'IssuesFound') {
      return 'recon-issues';
    }
    return 'recon-muted';
  }

  runEmergency(action: string): void {
    this.board.operationsEmergency(action, this.emergencyConfirm).subscribe({
      next: (res) => {
        this.emergencyMessage = res.message;
        this.emergencyConfirm = '';
        this.loadNotifications();
        this.cdr.markForCheck();
      },
      error: (err) => {
        this.emergencyMessage = err?.error?.message ?? 'Emergency action failed';
        this.cdr.markForCheck();
      },
    });
  }

  tabLabel(t: { id: AppOperationsTab; label: string }): string {
    switch (t.id) {
      case 'orders':
        return `Orders (${this.ordersOpenCount()})`;
      case 'positions':
        return `Positions (${this.positionsOpenCount()})`;
      case 'notifications':
        return `Alerts (${this.alertsUnreadCount()})`;
      case 'reconciliation':
        return `Reconciliation (${this.reconciliationTabBadge()})`;
      default:
        return t.label;
    }
  }

  ordersOpenCount(): number {
    return this.orders?.open.length ?? 0;
  }

  positionsOpenCount(): number {
    return this.positions?.open.length ?? this.risk?.positionRisk.openPositions ?? 0;
  }

  alertsUnreadCount(): number {
    return this.notifications.filter((n) => !n.read).length;
  }

  reconciliationTabBadge(): string {
    const h = this.reconciliation?.health;
    if (h === 'Healthy') {
      return 'OK';
    }
    if (h === 'IssuesFound') {
      return 'Issues';
    }
    if (h === 'BrokerError') {
      return 'Error';
    }
    if (h === 'Skipped') {
      return '—';
    }
    return '…';
  }

  capitalUsagePercent(): number {
    const total = this.risk?.account.totalCapital ?? 0;
    const used = this.risk?.account.capitalUsed ?? 0;
    if (total <= 0) {
      return 0;
    }
    return Math.min(100, (used / total) * 100);
  }

  dailyLossUsedPercent(): number {
    const max = this.risk?.limits.maxDailyLoss ?? 0;
    const pnl = this.risk?.account.dailyPnl ?? 0;
    if (max <= 0 || pnl >= 0) {
      return 0;
    }
    return Math.min(100, (Math.abs(pnl) / max) * 100);
  }

  riskHealthTier(): 'healthy' | 'warning' | 'critical' {
    return this.resolveRiskHealthTier();
  }

  riskHealthHeadline(): string {
    const tier = this.resolveRiskHealthTier();
    if (tier === 'healthy') {
      return 'Healthy';
    }
    if (tier === 'warning') {
      return 'Watch';
    }
    return 'Risk Limit Approaching';
  }

  /** API may send OperationsRiskHealth as number (default System.Text.Json); healthLabel is fallback. */
  private resolveRiskHealthTier(): 'healthy' | 'warning' | 'critical' {
    const raw = this.risk?.health as OperationsRiskHealth | number | undefined;
    const label = (this.risk?.healthLabel ?? '').toLowerCase();

    if (raw === 'Healthy' || raw === 0) {
      return 'healthy';
    }
    if (raw === 'Watch' || raw === 1) {
      return 'warning';
    }
    if (raw === 'RiskLimitApproaching' || raw === 2) {
      return 'critical';
    }

    if (label.includes('healthy')) {
      return 'healthy';
    }
    if (label.includes('watch')) {
      return 'warning';
    }
    if (label.includes('risk limit') || label.includes('approaching')) {
      return 'critical';
    }

    return 'warning';
  }

  isLiveTradingOn(): boolean {
    const mode = this.risk?.tradingMode;
    return mode === 'ManualLiveArmed' || mode === 'ZoneAutoLiveArmed';
  }

  isManualTradingOn(): boolean {
    return this.risk?.tradingMode === 'ManualLiveArmed';
  }

  isZoneAutoOn(): boolean {
    return this.risk?.tradingMode === 'ZoneAutoLiveArmed';
  }

  isStrategyAutoOn(): boolean {
    const rows = this.strategiesStore.snapshotZoneRollout()?.rows ?? [];
    return rows.some((r) => r.phase === 'paper_auto');
  }

  recentAlerts(): OperationsNotification[] {
    return this.notifications.slice(0, 5);
  }

  recentReconciliationEvents(): BrokerReconciliationHistoryEntry[] {
    return this.reconciliationHistory.slice(0, 5);
  }

  recentAuditActivity(): OperationsAuditEntry[] {
    return this.audit.slice(0, 5);
  }

  tradingModeIcon(): string {
    const label = this.risk?.tradingModeLabel ?? 'Disarmed';
    if (label.includes('Zone Auto')) {
      return '🟢';
    }
    if (label.includes('Manual Live')) {
      return '🟡';
    }
    return '🔴';
  }

  healthClass(): string {
    const tier = this.resolveRiskHealthTier();
    if (tier === 'healthy') {
      return 'health-green';
    }
    if (tier === 'warning') {
      return 'health-yellow';
    }
    return 'health-red';
  }

  brokerHealthClass(): string {
    const raw = this.brokerHealth?.status;
    const label = (this.brokerHealth?.statusLabel ?? '').toLowerCase();
    const tier = this.resolveBrokerHealthTier(raw, label);
    if (tier === 'healthy') {
      return 'health-green';
    }
    if (tier === 'warning') {
      return 'health-yellow';
    }
    return 'health-red';
  }

  /** API may send enum as number (default System.Text.Json); UI strings use statusLabel as fallback. */
  private resolveBrokerHealthTier(
    status: OperationsBrokerHealthStatus | number | undefined,
    statusLabelLower: string,
  ): 'healthy' | 'warning' | 'error' {
    if (status === 'Healthy' || status === 0) {
      return 'healthy';
    }
    if (status === 'Delayed' || status === 1) {
      return 'warning';
    }
    if (status === 'Disconnected' || status === 2) {
      return 'error';
    }
    if (statusLabelLower.includes('healthy')) {
      return 'healthy';
    }
    if (statusLabelLower.includes('delay')) {
      return 'warning';
    }
    return 'error';
  }

  emergencyConfirmHint(): string {
    return OPERATIONS_EMERGENCY_CONFIRM;
  }

  openStrategies(): void {
    void this.router.navigateByUrl('/strategies');
  }

  openPositionTicket(p: OperationsPositionRow): void {
    if (p.status !== 'OPEN' && this.positionTab === 'closed') {
      return;
    }
    const kind = normalizeInstrumentType(p.instrumentType);
    let instrumentType: ManualInstrumentType = 'STOCK';
    if (kind === InstrumentKind.Future) {
      instrumentType = 'FUTURE';
    } else if (kind === InstrumentKind.Option) {
      instrumentType = 'OPTION';
    }
    const mode: ManualExecutionMode =
      p.executionMode?.toUpperCase() === 'LIVE' ? 'LIVE' : 'PAPER';
    this.tradeTickets.open({
      instrumentType,
      displayName: p.symbol,
      exchange: tradeTicketExchange(instrumentType, p.symbol),
      symbol: p.symbol,
      underlying: p.symbol,
      transactionType: 'SELL',
      ltp: p.currentPrice,
      previousClose: p.entryPrice,
      executionMode: mode,
    });
    this.cdr.markForCheck();
  }
}
