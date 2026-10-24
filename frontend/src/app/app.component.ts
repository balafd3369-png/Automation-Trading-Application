import { DatePipe, DecimalPipe, NgClass } from '@angular/common';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import {
  AfterViewInit,
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  DestroyRef,
  ElementRef,
  HostListener,
  NgZone,
  OnInit,
  ViewChild,
  inject,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterOutlet } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { DomSanitizer, SafeResourceUrl } from '@angular/platform-browser';
import {
  EMPTY,
  Observable,
  Subject,
  catchError,
  debounceTime,
  distinctUntilChanged,
  exhaustMap,
  filter,
  finalize,
  interval,
  map,
  merge,
  of,
  startWith,
  switchMap,
  take,
  tap,
  timeout,
} from 'rxjs';
import {
  BoardResponse,
  ChartBar,
  ChartLiveUpdate,
  ChartResponse,
  LiveExecutionEvent,
  LiveExecutionSnapshot,
  LiveOrder,
  LiveTrade,
  OptionMarketQuote,
  PaperAgentSnapshot,
  PaperPosition,
  PaperTradeHistoryItem,
  PaperTradeHistoryResponse,
  PaperTradeProposal,
  PressureMark,
  STOCK_B1_REJECTION_STRATEGY,
  SymbolSearchHit,
  SymbolSnapshot,
  TradeHistoryRange,
  ZoneRejectionBacktestResponse,
} from './core/board.models';
import { AuthService } from './core/auth.service';
import { BoardService } from './core/board.service';
import { environment } from '../environments/environment';
import { MarketBreadthStore } from './core/market-breadth.store';
import {
  MarketSentimentMode,
  marketMomentumBadgeLabel,
  resolveMarketSentimentMode,
} from './core/market-sentiment.util';
import { ChartPersistenceService } from './core/chart-persistence.service';
import { BoardPollStore } from './core/board-poll.store';
import { BrokerSessionStore } from './core/broker-session.store';
import { ChartLiveCoordinator } from './core/chart-live.coordinator';
import { TradingRefreshService } from './core/trading-refresh.service';
import { LiveClockComponent } from './live-clock.component';
import { PaperStrategyFocus, PaperStrategyFocusService } from './core/paper-strategy-focus.service';
import { StrategiesPageComponent } from './strategies/strategies-page.component';
import { StrategyCommandCenterComponent } from './strategy-command-center/strategy-command-center.component';
import { StrategiesPageStore } from './strategies/strategies-page.store';
import { StrategyTradesPanelComponent } from './strategy-trades-panel/strategy-trades-panel.component';
import { TradeHistoryDashboardComponent } from './trade-history-dashboard/trade-history-dashboard.component';
import { ManualTradingPageComponent } from './manual-trading/manual-trading-page.component';
import { ManualTransactionType } from './core/manual-trading.models';
import { TradeTicketService } from './core/trade-ticket.service';
import {
  contextFromOption,
  contextFromStock,
  manualExecutionFromAppMode,
  tradeTicketExchange,
} from './core/trade-ticket.util';
import { WatchlistHoverRowComponent } from './shared/watchlist-hover-row.component';
import { OperationsNotificationService } from './core/operations-notification.service';
import { OperationsNotification } from './core/operations.models';
import { TradeTicketModalComponent } from './trade-ticket/trade-ticket-modal.component';
import { InstrumentKind, normalizeInstrumentType } from './core/instrument-type.util';
import { OperationsSuiteComponent } from './operations/operations-suite.component';
import { AppOperationsTab } from './core/app-navigation.util';
import { MarketSentimentAmbientComponent } from './market-sentiment-ambient/market-sentiment-ambient.component';
import { SuperIntelligencePageComponent } from './super-intelligence/super-intelligence-page.component';
import {
  ChartDrawing,
  IndicatorKind,
  drawingStorageKey,
  chartCandlesTailOnlyChange,
  firstChangedCandle,
  istDate,
  boardDisplayLast,
  mergeChartCandlesStable,
  clampIntradayBarWicks,
  isIstCashSessionOpen,
  normalizeChartCandles,
  sanitizeChartCandles,
  istDateTime,
  bollingerBands,
  extendTrendSegment,
  loadChartIndicatorPrefs,
  movingAverage,
  relativeStrengthIndex,
  saveChartIndicatorPrefs,
  parseDrawings,
  sessionStartBars,
  todayIstDateKey,
} from './core/chart-ui.util';
import {
  syncLiveCandle,
  tone as rowTone,
  isPaperStopLossExit,
  isPaperTargetExit,
  mergePaperPositions,
  paperPositionCapitalUsed,
  paperTradeCapitalUsed,
  paperTradeEntryPrice,
  paperTradeExitPrice,
  paperTradeStopLoss,
  paperTradeTarget,
  filterCatalogPaperHistoryTrades,
  filterExecutedPaperHistoryTrades,
  shouldReplaceLiveAgent,
  formatPaperHistoryStrategyLabel,
  paperTradeHistoryInIstDateRange,
  tradeHistoryPresetIstWindow,
  istTodayCalendarYmd,
  brokerLoginRejectedMessage,
  brokerOtpSendSucceeded,
  preserveBrokerOtpEntry,
  symbolHasVerifiedQuote,
  boardSymbolDisplayLast,
  normalizeCatalogStrategyId,
  tradeMatchesCatalogStrategy,
} from './core/trading-ui.util';
import { formatInstrumentTypeDisplay } from './core/instrument-type.util';
import {
  quoteQualityCssClass,
  quoteQualityLabel,
  quoteQualityShowCheck,
  symbolQuoteFreshness,
} from './core/quote-quality.util';
import {
  buildPaperStrategyTradesUrl,
  parsePaperStrategyFocusFromUrl,
  urlHasPaperStrategyFocus,
} from './core/paper-strategy-navigation.util';
import { mapLiveTradesToHistoryItems } from './core/live-trades-history.util';
import {
  digitPrefixFromQuery,
  equityOptionStrikeStep,
  hasListedOptions,
  isFnoEquityUnderlying,
  parseSymbolSearchQuery,
  resolveOptionSearchUnderlyings,
  roundStrikeToStep,
  strikesMatchingDigitPrefix,
  symbolMatchesStockQuery,
  symbolSearchCatalogTerm,
} from './core/symbol-search.util';
import {
  clearMStockCredentials,
  hasMStockCredentials,
  loadMStockCredentials,
  saveMStockCredentials,
} from './core/broker-credentials.storage';
import {
  APP_THEME_PRESETS,
  AppThemePalette,
  AppThemePresetId,
  AppThemeSelection,
  applyThemePalette,
  CHART_CANDLE_STYLE,
  lightweightChartDisplayOptions,
  loadThemeSelection,
  resolveChartSurfaceFromPalette,
  resolveThemePalette,
  saveThemeSelection,
} from './core/app-theme';
import {
  APP_NAV_STORAGE_KEY,
  parseRouterUrl,
  readBootNavigationState,
  routerPathForState,
} from './core/app-navigation.util';

const BOOT_NAV = readBootNavigationState();

type UiState = 'loading' | 'ready' | 'error' | 'empty';
type DrawTool = 'none' | 'hline' | 'trend';
type MarketKind = 'stocks' | 'options';
type MarketNavTab = 'dashboard' | 'options';
type OptionSide = 'CE' | 'PE';
/** On Options: index tab shows spot chart; CE/PE strike shows option premium chart. */
type OptionsChartMode = 'underlying' | 'contract';
type CenterView = 'market' | 'trades' | 'strategies' | 'intelligence' | 'manual' | 'operations';
type ExecutionMode = 'paper' | 'live';
type TradePanelTab = 'paper' | 'live' | 'history' | 'agent';
type LiveBookView = 'positions' | 'orders';
type LiveOrderFilter = 'pending' | 'executed' | 'trigger';
const EXECUTION_MODE_KEY = 'twb-execution-mode';

export interface OptionContract {
  underlying: string;
  expiry: string;
  expiryLabel: string;
  strike: number;
  side: OptionSide;
  label: string;
  last?: number;
  changePoints?: number;
  changePercent?: number;
  previousClose?: number;
}
type TvChart = {
  addCandlestickSeries(options: Record<string, unknown>): TvCandle;
  addLineSeries(options: Record<string, unknown>): TvLine;
  timeScale(): { fitContent(): void; scrollToRealTime(): void };
  subscribeClick(handler: (param: { point?: { x: number; y: number }; time?: number }) => void): void;
  subscribeCrosshairMove(handler: (param: TvCrosshairParam) => void): void;
  removeSeries(series: TvLine): void;
  resize(width: number, height: number): void;
  remove(): void;
  applyOptions?(options: Record<string, unknown>): void;
  priceScale(id?: string): { applyOptions(options: Record<string, unknown>): void };
};
type TvPriceLine = { applyOptions(options: Record<string, unknown>): void };
type TvLine = {
  setData(data: Array<Record<string, unknown>>): void;
};
type TvCrosshairParam = {
  time?: number;
  point?: { x: number; y: number };
  seriesData?: Map<unknown, { time: number; open?: number; high?: number; low?: number; close?: number; value?: number }>;
};
type TvCandle = {
  setData(data: Array<Record<string, unknown>>): void;
  update(bar: Record<string, unknown>): void;
  setMarkers(markers: Array<Record<string, unknown>>): void;
  createPriceLine(options: Record<string, unknown>): TvPriceLine;
  removePriceLine(line: TvPriceLine): void;
  coordinateToPrice(y: number): number | null;
};

@Component({
  selector: 'app-root',
  imports: [
    DecimalPipe,
    DatePipe,
    NgClass,
    FormsModule,
    LiveClockComponent,
    StrategiesPageComponent,
    StrategyCommandCenterComponent,
    SuperIntelligencePageComponent,
    StrategyTradesPanelComponent,
    TradeHistoryDashboardComponent,
    MarketSentimentAmbientComponent,
    ManualTradingPageComponent,
    OperationsSuiteComponent,
    RouterLink,
    RouterOutlet,
    TradeTicketModalComponent,
    WatchlistHoverRowComponent,
  ],
  templateUrl: './app.component.html',
  styleUrl: './app.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AppComponent implements OnInit, AfterViewInit {
  private readonly board = inject(BoardService);
  readonly auth = inject(AuthService);
  readonly requireAuth = environment.requireAuth;
  private readonly marketBreadth = inject(MarketBreadthStore);
  private readonly chartPersistence = inject(ChartPersistenceService);
  private readonly boardPoll = inject(BoardPollStore);
  private readonly brokerSession = inject(BrokerSessionStore);
  private readonly chartLive = inject(ChartLiveCoordinator);
  private readonly tradingRefresh = inject(TradingRefreshService);
  private readonly strategiesStore = inject(StrategiesPageStore);
  private readonly operationsNotify = inject(OperationsNotificationService);
  private readonly paperStrategyFocus = inject(PaperStrategyFocusService);
  private readonly http = inject(HttpClient);
  private readonly sanitizer = inject(DomSanitizer);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly tradeTickets = inject(TradeTicketService);
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly ngZone = inject(NgZone);
  private readonly optionChainReload = new Subject<void>();
  private readonly historyReload = new Subject<void>();
  private readonly zoneBacktestReload = new Subject<void>();
  @ViewChild('chartHost') chartHost?: ElementRef<HTMLDivElement>;
  @ViewChild('brokerHub') brokerHubRef?: ElementRef<HTMLDetailsElement>;
  @ViewChild('symbolSearchInput') symbolSearchInput?: ElementRef<HTMLInputElement>;
  @ViewChild('symbolSearchRoot') symbolSearchRoot?: ElementRef<HTMLElement>;
  @ViewChild('themePickerRoot') themePickerRoot?: ElementRef<HTMLElement>;
  private loadedChartName = '';
  private pendingChartName = '';
  chartBusy = false;
  private tvChart?: TvChart;
  private candleSeries?: TvCandle;
  private zonePriceLines: TvPriceLine[] = [];
  lastChart?: ChartResponse;
  private readonly chartRefreshSec = 5;
  private readonly chartStreamFullRefreshSec = 90;
  /** True when SignalR is delivering fresh chart ticks (not REST-only). */
  chartStreamReceiving = false;
  private indexChartPrefetchStarted = false;
  private chartPollInFlight = false;
  private chartResize?: ResizeObserver;
  private boardSymbolsRev = '';
  private readonly railLists: {
    selling: SymbolSnapshot[];
    buying: SymbolSnapshot[];
    ranged: SymbolSnapshot[];
  } = { selling: [], buying: [], ranged: [] };
  private lastIndicatorCandleKey = '';
  private lastAppliedBoardKey = '';
  private sessionMarkerKey = '';
  private optionListRev = '';
  private optionCeCache: OptionContract[] = [];
  private optionPeCache: OptionContract[] = [];
  private chartLoadFrame = 0;
  private trendFrom?: { time: number; value: number };
  private readonly indicatorSeries = new Map<IndicatorKind, TvLine>();
  private bollingerSeries?: { upper: TvLine; middle: TvLine; lower: TvLine };
  private rsiSeries?: TvLine;
  private readonly drawingPriceLines = new Map<string, TvPriceLine>();
  private readonly drawingTrendSeries = new Map<string, TvLine>();
  drawTool: DrawTool = 'none';
  drawingStatus = '';
  drawings: ChartDrawing[] = [];
  selectedDrawingId = '';
  activeIndicators = loadChartIndicatorPrefs();
  crosshairCandle?: { time: string; open: number; high: number; low: number; close: number };
  chartInterval = '5minute';
  /** full = standard chart; max = minimal chrome, largest chart */
  chartViewMode: 'full' | 'max' = 'full';
  chartRefreshPending = false;
  /** True while full-history API is in flight but candles are already on screen. */
  chartFullHistoryPending = false;
  /** Chart was painted from browser/memory cache before the latest network refresh finished. */
  chartPaintedFromCache = false;
  private readonly chartDataCache = new Map<string, ChartResponse>();
  private readonly chartCacheMax = 16;
  readonly timeframes = [
    { id: 'minute', label: '1m', hint: 'Intraday · today (and prior session when broker provides it)' },
    { id: '5minute', label: '5m', hint: 'Intraday · up to ~2 weeks of sessions' },
    { id: '15minute', label: '15m', hint: 'Intraday · multiple sessions' },
    { id: '60minute', label: '1h', hint: 'Intraday · multiple sessions' },
    { id: 'day', label: 'Daily', hint: 'One candle per trading day · months of history' },
  ];

  state: UiState = 'loading';
  errorMessage = '';
  boardData: BoardResponse | null = null;
  selectedName = 'NIFTY';
  selected: SymbolSnapshot | null = null;
  otp = '';
  otpRequested = false;
  brokerLoginUsername = '';
  brokerLoginPassword = '';
  brokerLoginApiKey = '';
  brokerServerApiKeyConfigured = false;
  /** False until GET /api/broker/capabilities succeeds (401 must not look like "missing API key"). */
  brokerCapabilitiesLoaded = false;
  brokerEditingCredentials = false;
  brokerBusy = false;
  brokerConnected = false;
  brokerAvailableFunds: number | null = null;
  brokerUtilizedFunds: number | null = null;
  brokerProvider: 'mstock' = 'mstock';
  brokerDisplayName = 'm.Stock';
  brokerMessage = 'Click Request OTP, then enter the code from your phone.';
  readonly brokerChoices: ReadonlyArray<{
    id: 'mstock';
    name: string;
    mark: string;
    authLabel: string;
    description: string;
  }> = [
    {
      id: 'mstock',
      name: 'm.Stock',
      mark: 'MS',
      authLabel: 'OTP',
      description: 'Daily login: user ID + password → Request OTP → Submit. API key is one-time on the server (not each login).',
    },
  ];
  readonly indexTabs = ['NIFTY', 'SENSEX', 'BANKNIFTY', 'FINNIFTY'];
  marketKind: MarketKind = BOOT_NAV.marketKind;
  marketNavTab: MarketNavTab = BOOT_NAV.marketNavTab;
  centerView: CenterView = BOOT_NAV.centerView;
  strategyDetailId: string | null = BOOT_NAV.strategyDetailId ?? null;
  operationsTab: AppOperationsTab = BOOT_NAV.operationsTab ?? 'risk';
  fullPageRoute = false;
  pickerOpen = false;
  symbolSearchPanelTop = 0;
  symbolSearchPanelLeft = 0;
  symbolSearchPanelWidth = 400;
  pickerKind: MarketKind = 'stocks';
  pickerQuery = '';
  readonly symbolSearchShortcutHint =
    typeof navigator !== 'undefined' && /Mac|iPhone|iPad/i.test(navigator.userAgent) ? '⌘K' : 'Ctrl+K';
  pickerCatalogHits: SymbolSearchHit[] = [];
  private readonly pickerSearchQuery$ = new Subject<string>();
  optionUnderlying = 'NIFTY';
  optionExpiry = '';
  optionSide: OptionSide = 'CE';
  openedOption: OptionContract | null = null;
  optionsChartMode: OptionsChartMode = 'contract';
  optionChartError = '';
  optionQuotes = new Map<string, OptionMarketQuote>();
  paperAgent: PaperAgentSnapshot | null = null;
  zoneBacktest: ZoneRejectionBacktestResponse | null = null;
  zoneBacktestLoading = false;
  zoneBacktestError = '';
  readonly b1RejectionStrategy = STOCK_B1_REJECTION_STRATEGY;
  agentCommand = '';
  agentBusy = false;
  agentError = '';
  tradeHistory: PaperTradeHistoryResponse | null = null;
  historyRange: TradeHistoryRange = '1m';
  historyPage = 1;
  readonly historyPageSize = 10;
  historyDetailTradeId: string | null = null;
  historyPnlFilter: 'all' | 'positive' | 'negative' = 'all';
  paperLedgerPage = 1;
  readonly paperLedgerPageSize = 10;
  paperLedgerDetailTradeId: string | null = null;
  paperLedgerPnlFilter: 'all' | 'positive' | 'negative' = 'all';
  /** IST calendar dates `yyyy-MM-dd` — single day when only From is set. */
  historyDateFrom: string | null = null;
  historyDateTo: string | null = null;
  /** Shows calendar pickers when user chose Custom date. */
  historyCustomDateOpen = false;
  historyError = '';
  paperStrategyFilter: string | null = null;
  executionMode: ExecutionMode = BOOT_NAV.tradePanelTab === 'live' ? 'live' : 'paper';
  tradePanelTab: TradePanelTab = BOOT_NAV.tradePanelTab;
  liveAgent: LiveExecutionSnapshot | null = null;
  liveAcknowledgement = '';
  zoneAutoLiveAcknowledgement = '';
  readonly zoneAutoLivePhrase = 'GO LIVE ZONE CSV ON REAL MONEY';
  liveProduct: 'MIS' | 'NRML' = 'MIS';
  liveBusy = false;
  liveError = '';
  liveBookView: LiveBookView = 'positions';
  liveDetailTradeId: string | null = null;
  liveOrderFilter: LiveOrderFilter = 'executed';
  readonly themePresets = APP_THEME_PRESETS;
  themeSelection: AppThemeSelection = { presetId: 'default' };
  /** Label shown in the theme menu while hovering a preset (does not change the app theme). */
  themePreviewLabel = '';
  /** Swatch color for the in-menu preview strip while hovering. */
  themeHoverPreviewSwatch = '';
  themePickerOpen = false;
  navNotifications: OperationsNotification[] = [];
  navUnreadCount = 0;
  navNotificationsLoading = false;
  /** From GET /api/platform/features — false disables live trading UI and polls. */
  platformLiveTradingEnabled = false;
  userMenuPanel: 'main' | 'password' = 'main';
  changePasswordCurrent = '';
  changePasswordNew = '';
  changePasswordBusy = false;
  changePasswordMessage = '';

  ngOnInit(): void {
    this.auth.user$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => this.bumpView());
    this.enforceAuthSession();
    this.http
      .get<{ liveTradingEnabled: boolean }>('/api/platform/features')
      .pipe(
        catchError(() => of({ liveTradingEnabled: false })),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((features) => {
        this.platformLiveTradingEnabled = features.liveTradingEnabled;
        if (!features.liveTradingEnabled && this.tradePanelTab === 'live') {
          void this.router.navigateByUrl('/trades/paper');
        }
        this.bumpView();
      });

    this.themeSelection = loadThemeSelection();
    this.applyAppTheme();
    this.paperStrategyFocus.focus$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((focus) => {
      if (focus) {
        this.applyPaperStrategyFocus(focus);
      } else if (!urlHasPaperStrategyFocus(this.router.url)) {
        this.paperStrategyFilter = null;
      }
      this.bumpView();
    });
    const bootUrl = this.router.url;
    const bootFocusFromUrl = parsePaperStrategyFocusFromUrl(bootUrl);
    if (bootFocusFromUrl) {
      this.paperStrategyFocus.open(bootFocusFromUrl.strategyId, bootFocusFromUrl.query, {
        targetTab: bootFocusFromUrl.targetTab,
        source: bootFocusFromUrl.source,
      });
      this.applyPaperStrategyFocus(bootFocusFromUrl);
    } else {
      const bootFocus = this.paperStrategyFocus.snapshot();
      if (bootFocus) {
        this.applyPaperStrategyFocus(bootFocus);
      }
    }
    this.pendingChartName = this.selectedName;
    this.chartLive.live$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((update) => {
      this.ngZone.runOutsideAngular(() => {
        this.applyChartLiveUpdate(update);
      });
      this.bumpView();
    });
    this.chartLive.status$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((status) => {
      this.chartStreamReceiving = status.streaming;
      this.bumpView();
    });
    this.destroyRef.onDestroy(() => {
      void this.chartLive.disconnect();
    });
    const syncRouteChrome = (url: string) => {
      this.fullPageRoute =
        url.includes('/login') || url.includes('/dashboard') || url.includes('/admin');
      if (!this.fullPageRoute) {
        this.applyNavigationState(parseRouterUrl(url));
        this.enforceAuthSession();
      }
      this.syncPaperStrategyFocusFromUrl(url);
      this.saveNavigationToStorage();
      this.bumpView();
    };
    syncRouteChrome(this.router.url);
    this.router.events
      .pipe(
        filter((event): event is NavigationEnd => event instanceof NavigationEnd),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((event) => syncRouteChrome(event.urlAfterRedirects));

    this.boardPoll.start();
    this.boardPoll.events$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((event) => {
      if (event.kind === 'error') {
        this.state = 'error';
        this.errorMessage = event.message;
        this.bumpView();
        return;
      }
      this.apply(event.data);
    });

    this.operationsNotify
      .startPolling()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((list) => this.operationsNotify.ingest(list));

    interval(20_000)
      .pipe(
        startWith(0),
        switchMap(() =>
          this.board.operationsNotifications(25, false).pipe(catchError(() => of([] as OperationsNotification[]))),
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((list) => {
        this.navNotifications = list;
        this.navUnreadCount = list.filter((n) => !n.read).length;
        this.bumpView();
      });

    merge(
      interval(this.chartRefreshSec * 1000).pipe(
        startWith(0),
        filter(
          () =>
            this.centerView === 'market'
            && this.brokerConnected
            && !!this.pendingChartName
            && !!this.candleSeries
            && !this.chartStreamReceiving,
        ),
      ),
      interval(this.chartStreamFullRefreshSec * 1000).pipe(
        startWith(0),
        filter(
          () =>
            this.centerView === 'market'
            && this.brokerConnected
            && !!this.pendingChartName
            && !!this.candleSeries
            && this.chartStreamReceiving,
        ),
      ),
    )
      .pipe(
        exhaustMap(() => {
          const requestKey = this.chartRequestKey();
          this.chartPollInFlight = true;
          return this.chartRequest(this.pendingChartName, this.chartInterval).pipe(
            map((data) => ({ data, requestKey })),
            catchError(() => EMPTY),
            finalize(() => {
              this.chartPollInFlight = false;
            }),
          );
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe(({ data, requestKey }) => {
        if (requestKey !== this.chartRequestKey()) {
          return;
        }
        const prior = this.chartCandlesPriorForMerge(data.name);
        const mergedPayload = this.mergeChartPayload(prior, data);
        const remount = this.shouldRemountChart(prior, mergedPayload.candles);
        this.ngZone.runOutsideAngular(() => {
          this.applyChartData(mergedPayload, remount);
        });
        this.rememberChartCache(requestKey, mergedPayload);
      });

    merge(interval(3000), this.optionChainReload)
      .pipe(
        startWith(0),
        filter(() => this.marketKind === 'options' && !!this.optionUnderlying && !!this.optionExpiry),
        switchMap(() =>
          this.board.optionChain(this.optionUnderlying, this.optionExpiry).pipe(
            catchError(() => EMPTY),
          ),
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((quotes) => {
        this.optionQuotes = new Map(
          quotes.map((quote) => [this.optionQuoteKey(quote.strike, quote.side), quote]),
        );
        this.refreshOpenedOptionQuote();
        this.refreshOptionContractRails();
        this.bumpView();
      });

    merge(interval(4000), this.tradingRefresh.paperAgentRefresh$)
      .pipe(
        startWith(0),
        filter(() => this.state === 'ready'),
        exhaustMap(() =>
          this.board.paperAgent().pipe(
            catchError(() => {
              this.agentError =
                'Strategy agent API unavailable (check backend on port 45211). Open positions below use trade history.';
              this.bumpView();
              return EMPTY;
            }),
          ),
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((snapshot) => {
        if (!this.shouldReplacePaperAgent(snapshot)) {
          return;
        }
        this.paperAgent = snapshot;
        this.agentError = '';
        this.bumpView();
      });

    interval(4000)
      .pipe(
        startWith(0),
        filter(() => this.state === 'ready' && this.platformLiveTradingEnabled),
        exhaustMap(() => this.board.liveAgent().pipe(catchError(() => EMPTY))),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((snapshot) => {
        if (!shouldReplaceLiveAgent(snapshot, this.liveAgent)) {
          return;
        }
        this.liveAgent = snapshot;
        this.bumpView();
      });

    merge(interval(5000), this.historyReload, this.tradingRefresh.paperHistoryRefresh$)
      .pipe(
        startWith(0),
        filter(() => this.centerView === 'trades' && (this.tradePanelTab === 'paper' || this.tradePanelTab === 'history')),
        switchMap(() => {
          const { range, from, to } = this.historyPaperTradesQuery();
          return this.board.paperTrades(range, from, to).pipe(
            catchError(() => {
              this.historyError = 'Trade history is unavailable.';
              return EMPTY;
            }),
          );
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((history) => {
        this.tradeHistory = history;
        this.historyError = '';
        this.clampHistoryPage();
        this.clampPaperLedgerPage();
        this.bumpView();
      });

    merge(interval(60000), this.zoneBacktestReload)
      .pipe(
        startWith(undefined),
        filter(() => this.tradePanelTab === 'paper' && this.centerView === 'trades'),
        switchMap(() => {
          const name = this.zoneBacktestTargetSymbol();
          if (!name) {
            this.zoneBacktest = null;
            this.zoneBacktestError = '';
            this.zoneBacktestLoading = false;
            return of(null);
          }
          this.zoneBacktestLoading = true;
          this.zoneBacktestError = '';
          return this.board.zoneRejectionBacktest(name).pipe(
            catchError(() => {
              this.zoneBacktestError = `B1→S2 backtest unavailable for ${name}.`;
              return of(null);
            }),
            finalize(() => {
              this.zoneBacktestLoading = false;
            }),
          );
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((data) => {
        this.zoneBacktest = data;
        this.bumpView();
      });

    this.pickerSearchQuery$
      .pipe(
        debounceTime(120),
        distinctUntilChanged(),
        switchMap((q) => {
          const term = symbolSearchCatalogTerm(q);
          if (!term || term.length < 2) {
            return of<SymbolSearchHit[]>([]);
          }
          return this.board.searchSymbols(term).pipe(catchError(() => of<SymbolSearchHit[]>([])));
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((hits) => {
        this.pickerCatalogHits = hits;
        this.bumpView();
      });

    this.loadBrokerCredentialsFromStorage();
    this.refreshBrokerCapabilities();
    this.brokerSession
      .watch()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((res) => this.applyBrokerSession(res));

    const onKey = (event: KeyboardEvent) => {
      if ((event.ctrlKey || event.metaKey) && (event.key.toLowerCase() === 'p' || event.key.toLowerCase() === 'k')) {
        event.preventDefault();
        this.openPicker();
        queueMicrotask(() => this.symbolSearchInput?.nativeElement.focus());
      }
      if (event.key === 'Escape' && this.pickerOpen) {
        this.closePicker(true);
      }
      if (event.key === 'Escape' && this.drawTool !== 'none') {
        this.cancelDrawing();
      }
      const isTyping = event.target instanceof HTMLInputElement || event.target instanceof HTMLTextAreaElement;
      if (!isTyping && (event.key === 'Delete' || event.key === 'Backspace') && this.selectedDrawingId) {
        event.preventDefault();
        this.deleteSelectedDrawing();
      }
    };
    window.addEventListener('keydown', onKey);
    const onResize = () => this.positionSymbolSearchPanel();
    window.addEventListener('resize', onResize);
    this.destroyRef.onDestroy(() => {
      window.removeEventListener('keydown', onKey);
      window.removeEventListener('resize', onResize);
      cancelAnimationFrame(this.chartLoadFrame);
      this.chartResize?.disconnect();
    });
  }

  userRoleLabel(): string {
    const role = this.auth.user?.role;
    if (role === 'Admin') {
      return 'Admin';
    }
    if (role === 'Trader') {
      return 'Trader';
    }
    if (role === 'Viewer') {
      return 'Viewer';
    }
    return role ?? 'User';
  }

  userDisplayLabel(): string {
    return this.auth.user?.displayName?.trim() || this.auth.user?.email || 'Account';
  }

  openUserProfile(): void {
    const route = this.auth.isAdmin ? '/admin' : '/dashboard';
    void this.router.navigateByUrl(route);
  }

  openUserChangePassword(): void {
    this.userMenuPanel = 'password';
    this.changePasswordMessage = '';
    this.bumpView();
  }

  backUserMenu(): void {
    this.userMenuPanel = 'main';
    this.changePasswordMessage = '';
    this.bumpView();
  }

  submitChangePassword(): void {
    if (this.changePasswordBusy) {
      return;
    }
    const current = this.changePasswordCurrent.trim();
    const next = this.changePasswordNew.trim();
    if (!current || !next) {
      this.changePasswordMessage = 'Enter current and new password.';
      return;
    }
    this.changePasswordBusy = true;
    this.changePasswordMessage = '';
    this.auth
      .changePassword(current, next)
      .pipe(
        catchError((err: HttpErrorResponse) => {
          const body = err.error as { message?: string } | null;
          this.changePasswordMessage = body?.message ?? 'Could not update password.';
          return EMPTY;
        }),
        finalize(() => {
          this.changePasswordBusy = false;
          this.bumpView();
        }),
        take(1),
      )
      .subscribe(() => {
        this.changePasswordMessage = 'Password updated.';
        this.changePasswordCurrent = '';
        this.changePasswordNew = '';
      });
  }

  logoutUser(): void {
    this.auth.logout().pipe(take(1)).subscribe(() => void this.router.navigateByUrl('/login'));
  }

  private enforceAuthSession(): void {
    if (!environment.requireAuth) {
      return;
    }
    if (this.isAuthShellRoute(this.router.url)) {
      return;
    }
    this.auth
      .ensureSession()
      .pipe(take(1), takeUntilDestroyed(this.destroyRef))
      .subscribe((ok) => {
        if (!ok) {
          void this.router.navigateByUrl('/login');
        }
      });
  }

  private isAuthShellRoute(url: string): boolean {
    return url.includes('/login');
  }

  sendAgentCommand(messageOverride?: string): void {
    if (!this.auth.isAdmin) {
      this.agentError = 'Paper agent commands are restricted to platform admins.';
      return;
    }
    const message = (messageOverride ?? this.agentCommand).trim();
    if (!message || this.agentBusy) {
      return;
    }
    this.agentBusy = true;
    this.agentError = '';
    this.board
      .sendPaperAgentCommand(message)
      .pipe(
        catchError(() => {
          this.agentError = 'Paper agent API is unavailable.';
          this.bumpView();
          return EMPTY;
        }),
        finalize(() => {
          this.agentBusy = false;
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((snapshot) => {
        this.paperAgent = snapshot;
        this.agentCommand = '';
        this.historyReload.next();
        this.bumpView();
      });
  }

  setExecutionMode(mode: ExecutionMode): void {
    this.executionMode = mode;
    this.tradePanelTab = mode;
    this.liveError = '';
    localStorage.setItem(EXECUTION_MODE_KEY, mode);
  }

  setTradePanelTab(tab: TradePanelTab): void {
    void this.router.navigateByUrl(`/trades/${tab}`);
  }

  private applyTradePanelSideEffects(tab: TradePanelTab): void {
    if (tab === 'history') {
      this.historyReload.next();
    }
    if (tab === 'paper') {
      const preservePeriod =
        !!this.paperStrategyFocus.snapshot()
        || urlHasPaperStrategyFocus(this.router.url);
      if (!preservePeriod) {
        this.historyRange = 'today';
      }
      this.historyReload.next();
      this.zoneBacktestReload.next();
    }
  }

  private navigateApp(state: ReturnType<typeof parseRouterUrl>): void {
    const path = routerPathForState(state);
    if (this.router.url !== path) {
      void this.router.navigateByUrl(path);
    }
  }

  private syncNavigationHash(): void {
    this.navigateApp({
      centerView: this.centerView,
      tradePanelTab: this.tradePanelTab,
      marketKind: this.marketKind,
      marketNavTab: this.marketNavTab,
      strategyDetailId: this.strategyDetailId,
      operationsTab: this.operationsTab,
    });
    this.saveNavigationToStorage();
  }

  private saveNavigationToStorage(): void {
    if (typeof localStorage === 'undefined') {
      return;
    }
    localStorage.setItem(
      APP_NAV_STORAGE_KEY,
      JSON.stringify({
        centerView: this.centerView,
        tradePanelTab: this.tradePanelTab,
        marketKind: this.marketKind,
        marketNavTab: this.marketNavTab,
      }),
    );
  }

  private persistNavigationHash(): void {
    this.syncNavigationHash();
  }

  private leaveMarketWorkbench(): void {
    this.tvChart?.remove();
    this.tvChart = undefined;
    this.candleSeries = undefined;
    this.chartResize?.disconnect();
    this.loadedChartName = '';
    this.lastChart = undefined;
  }

  private applyNavigationState(state: ReturnType<typeof parseRouterUrl>): void {
    if (state.centerView !== 'market' && this.centerView === 'market') {
      this.leaveMarketWorkbench();
    }

    this.centerView = state.centerView;
    this.tradePanelTab = state.tradePanelTab;
    this.marketNavTab = state.marketNavTab;
    this.strategyDetailId = state.strategyDetailId;
    this.operationsTab = state.operationsTab ?? 'risk';

    if (state.centerView === 'market') {
      this.marketKind = state.marketKind;
      if (state.marketKind === 'options') {
        this.marketNavTab = 'options';
        this.optionUnderlying = this.isIndex(this.selectedName) ? this.selectedName : 'NIFTY';
        this.selectIndex(this.optionUnderlying);
        this.ensureOptionExpiry();
        if (!this.openedOption) {
          this.openedOption = this.atmContract(this.optionUnderlying, this.optionExpiry, this.optionSide);
        }
        this.loadedChartName = '';
        this.optionChainReload.next();
      } else {
        this.openedOption = null;
        this.optionChartError = '';
        this.loadedChartName = '';
      }
      if (this.selected) {
        this.pendingChartName = this.selected.name;
      }
      this.scheduleChartLoad();
      requestAnimationFrame(() => this.scheduleChartLoad());
      this.refreshOptionContractRails();
      return;
    }

    if (state.centerView === 'trades') {
      if (state.tradePanelTab === 'live' && !this.platformLiveTradingEnabled) {
        void this.router.navigateByUrl('/trades/paper');
        return;
      }
      if (state.tradePanelTab === 'paper' || state.tradePanelTab === 'live') {
        this.executionMode = state.tradePanelTab;
        localStorage.setItem(EXECUTION_MODE_KEY, state.tradePanelTab);
      }
      this.applyTradePanelSideEffects(state.tradePanelTab);
    }
  }

  zoneBacktestTargetSymbol(): string {
    if (!this.isIndex(this.selectedName)) {
      return this.selectedName;
    }
    const fromDecision = this.paperAgent?.recentDecisions?.find(
      (d) => d.strategy === STOCK_B1_REJECTION_STRATEGY && d.underlying && this.isIndex(d.underlying),
    )?.underlying;
    return fromDecision ?? this.selectedName;
  }

  isB1RejectionStrategy(strategy: string | undefined): boolean {
    return strategy === STOCK_B1_REJECTION_STRATEGY;
  }

  paperTargetTrades(): PaperTradeHistoryItem[] {
    return filterCatalogPaperHistoryTrades(this.tradeHistory?.trades ?? []).filter(
      (trade) => trade.status === 'CLOSED' && isPaperTargetExit(trade.exitReason),
    );
  }

  paperStopLossTrades(): PaperTradeHistoryItem[] {
    return filterCatalogPaperHistoryTrades(this.tradeHistory?.trades ?? []).filter(
      (trade) => trade.status === 'CLOSED' && isPaperStopLossExit(trade.exitReason),
    );
  }

  /** Trade History tab — catalog strategies only (Ten AM, zone board, B1 rejection). */
  historyCatalogTrades(): PaperTradeHistoryItem[] {
    if (this.historyCustomDateOpen && !this.historyDateFrom) {
      return [];
    }
    let items = filterExecutedPaperHistoryTrades(
      filterCatalogPaperHistoryTrades(this.tradeHistory?.trades ?? []),
    );
    if (this.historyDateFilterActive()) {
      const from = this.historyDateFrom!;
      const to = this.historyDateTo || from;
      items = items.filter((t) => paperTradeHistoryInIstDateRange(t, from, to));
    }
    return items.sort((a, b) => {
      const at = Date.parse(a.closedAt ?? a.openedAt ?? a.createdAt ?? '') || 0;
      const bt = Date.parse(b.closedAt ?? b.openedAt ?? b.createdAt ?? '') || 0;
      return bt - at;
    });
  }

  /** Trade History tab — paper catalog rows plus live executions for the selected period. */
  historyMergedTrades(): PaperTradeHistoryItem[] {
    const paper = this.historyCatalogTrades().map((trade) => ({
      ...trade,
      executionSource: trade.executionSource ?? 'PAPER',
    }));
    let live = mapLiveTradesToHistoryItems(this.liveAgent?.trades ?? [], this.liveAgent?.brokerPositions ?? []);
    if (this.historyCustomDateOpen && !this.historyDateFrom) {
      live = [];
    } else if (this.historyDateFilterActive()) {
      const from = this.historyDateFrom!;
      const to = this.historyDateTo || from;
      live = live.filter((trade) => paperTradeHistoryInIstDateRange(trade, from, to));
    } else {
      const { from, to } = tradeHistoryPresetIstWindow(this.historyRange);
      live = live.filter((trade) => paperTradeHistoryInIstDateRange(trade, from, to));
    }
    const merged = [...paper, ...live];
    return merged.sort((a, b) => {
      const at = Date.parse(a.closedAt ?? a.openedAt ?? a.createdAt ?? '') || 0;
      const bt = Date.parse(b.closedAt ?? b.openedAt ?? b.createdAt ?? '') || 0;
      return bt - at;
    });
  }

  historySourceFilter(): 'all' | 'paper' | 'live' {
    const source = this.paperStrategyFocus.snapshot()?.source;
    if (source === 'live' || source === 'paper' || source === 'all') {
      return source;
    }
    return 'all';
  }

  historyCatalogSummary(): {
    closedTrades: number;
    winningTrades: number;
    losingTrades: number;
    openTrades: number;
    realizedPnl: number;
  } {
    const trades = this.historyCatalogTrades();
    const closed = trades.filter((t) => t.status === 'CLOSED');
    return {
      closedTrades: closed.length,
      winningTrades: closed.filter((t) => (t.pnl ?? 0) > 0).length,
      losingTrades: closed.filter((t) => (t.pnl ?? 0) < 0).length,
      openTrades: trades.filter((t) => t.status === 'OPEN').length,
      realizedPnl: closed.reduce((sum, t) => sum + (t.pnl ?? 0), 0),
    };
  }

  historyDateFilterActive(): boolean {
    return !!this.historyDateFrom;
  }

  historyTodayIstYmd(): string {
    return istTodayCalendarYmd();
  }

  openHistoryCustomDate(): void {
    this.historyCustomDateOpen = true;
    this.resetHistoryListState();
    this.bumpView();
    queueMicrotask(() => this.focusHistoryFromDatePicker());
  }

  focusHistoryFromDatePicker(): void {
    const input = document.querySelector<HTMLInputElement>('.history-date-input[data-history-from="1"]');
    if (!input) {
      return;
    }
    input.focus();
    try {
      input.showPicker?.();
    } catch {
      /* showPicker requires a user gesture in some browsers */
    }
  }

  historyDateFilterLabel(): string {
    const from = this.historyDateFrom;
    if (!from) {
      return '';
    }
    const to = this.historyDateTo || from;
    if (to === from) {
      return this.formatHistoryIstDayLabel(from);
    }
    return `${this.formatHistoryIstDayLabel(from)} – ${this.formatHistoryIstDayLabel(to)}`;
  }

  onHistoryDateFromChange(value: string): void {
    this.historyCustomDateOpen = true;
    this.historyDateFrom = value?.trim() || null;
    if (!this.historyDateFrom) {
      this.historyDateTo = null;
    } else if (this.historyDateTo && this.historyDateTo < this.historyDateFrom) {
      this.historyDateTo = this.historyDateFrom;
    }
    this.resetHistoryListState();
    if (this.historyDateFrom) {
      this.historyReload.next();
      this.syncStrategyFocusUrlFromState();
    }
  }

  onHistoryDateToChange(value: string): void {
    this.historyCustomDateOpen = true;
    this.historyDateTo = value?.trim() || null;
    if (this.historyDateFrom && this.historyDateTo && this.historyDateTo < this.historyDateFrom) {
      this.historyDateFrom = this.historyDateTo;
    }
    this.resetHistoryListState();
    if (this.historyDateFrom) {
      this.historyReload.next();
      this.syncStrategyFocusUrlFromState();
    }
  }

  clearHistoryDateFilter(): void {
    this.historyDateFrom = null;
    this.historyDateTo = null;
    this.historyCustomDateOpen = false;
    this.resetHistoryListState();
    this.historyReload.next();
  }

  historyTradeStrategyLabel(trade: PaperTradeHistoryItem): string {
    return formatPaperHistoryStrategyLabel(trade.strategy ?? '');
  }

  private historyPaperTradesQuery(): {
    range: TradeHistoryRange;
    from: string | null;
    to: string | null;
  } {
    if (this.historyDateFrom) {
      return {
        range: '1m',
        from: this.historyDateFrom,
        to: this.historyDateTo || this.historyDateFrom,
      };
    }
    return { range: this.historyRange, from: null, to: null };
  }

  private formatHistoryIstDayLabel(ymd: string): string {
    const [y, m, d] = ymd.split('-').map((part) => Number(part));
    if (!y || !m || !d) {
      return ymd;
    }
    const utc = Date.UTC(y, m - 1, d, 5, 30);
    return new Date(utc).toLocaleDateString('en-IN', {
      day: '2-digit',
      month: 'short',
      year: 'numeric',
      timeZone: 'UTC',
    });
  }

  private resetHistoryListState(): void {
    this.historyPage = 1;
    this.historyDetailTradeId = null;
    this.paperLedgerPage = 1;
    this.paperLedgerDetailTradeId = null;
  }

  /** Paper tab — every paper trade (all strategies), same period as history filters. */
  paperLedgerTrades(): PaperTradeHistoryItem[] {
    if (this.historyCustomDateOpen && !this.historyDateFrom) {
      return [];
    }
    let items = filterExecutedPaperHistoryTrades([...(this.tradeHistory?.trades ?? [])]);
    if (this.historyDateFilterActive()) {
      const from = this.historyDateFrom!;
      const to = this.historyDateTo || from;
      items = items.filter((t) => paperTradeHistoryInIstDateRange(t, from, to));
    }
    if (this.paperStrategyFilter) {
      items = items.filter((trade) => tradeMatchesCatalogStrategy(trade, this.paperStrategyFilter!));
    }
    return items.sort((a, b) => {
      const at = Date.parse(a.closedAt ?? a.openedAt ?? a.createdAt ?? '') || 0;
      const bt = Date.parse(b.closedAt ?? b.openedAt ?? b.createdAt ?? '') || 0;
      return bt - at;
    });
  }

  paperLedgerSummary(): {
    closedTrades: number;
    winningTrades: number;
    losingTrades: number;
    openTrades: number;
    realizedPnl: number;
  } {
    const trades = this.paperLedgerTrades();
    const closed = trades.filter((t) => t.status === 'CLOSED');
    return {
      closedTrades: closed.length,
      winningTrades: closed.filter((t) => (t.pnl ?? 0) > 0).length,
      losingTrades: closed.filter((t) => (t.pnl ?? 0) < 0).length,
      openTrades: trades.filter((t) => t.status === 'OPEN').length,
      realizedPnl: closed.reduce((sum, t) => sum + (t.pnl ?? 0), 0),
    };
  }

  paperLedgerTradesFiltered(): PaperTradeHistoryItem[] {
    const items = this.paperLedgerTrades();
    if (this.paperLedgerPnlFilter === 'positive') {
      return items.filter((t) => t.status === 'CLOSED' && (t.pnl ?? 0) > 0);
    }
    if (this.paperLedgerPnlFilter === 'negative') {
      return items.filter((t) => t.status === 'CLOSED' && (t.pnl ?? 0) < 0);
    }
    return items;
  }

  paperLedgerClosedWinners(): PaperTradeHistoryItem[] {
    return this.paperLedgerTrades().filter((t) => t.status === 'CLOSED' && (t.pnl ?? 0) > 0);
  }

  paperLedgerClosedLosers(): PaperTradeHistoryItem[] {
    return this.paperLedgerTrades().filter((t) => t.status === 'CLOSED' && (t.pnl ?? 0) < 0);
  }

  paperLedgerPositivePnlSum(): number {
    return this.paperLedgerClosedWinners().reduce((sum, t) => sum + (t.pnl ?? 0), 0);
  }

  paperLedgerNegativePnlSum(): number {
    return this.paperLedgerClosedLosers().reduce((sum, t) => sum + (t.pnl ?? 0), 0);
  }

  paperLedgerTradesPage(): PaperTradeHistoryItem[] {
    const start = (this.paperLedgerPage - 1) * this.paperLedgerPageSize;
    return this.paperLedgerTradesFiltered().slice(start, start + this.paperLedgerPageSize);
  }

  paperLedgerPageCount(): number {
    const total = this.paperLedgerTradesFiltered().length;
    return Math.max(1, Math.ceil(total / this.paperLedgerPageSize));
  }

  setPaperLedgerPnlFilter(filter: 'all' | 'positive' | 'negative'): void {
    this.paperLedgerPnlFilter = filter;
    this.paperLedgerPage = 1;
    this.paperLedgerDetailTradeId = null;
    this.bumpView();
  }

  setPaperLedgerPage(page: number): void {
    this.paperLedgerPage = Math.min(Math.max(1, page), this.paperLedgerPageCount());
    this.paperLedgerDetailTradeId = null;
    this.bumpView();
  }

  togglePaperLedgerDetail(tradeId: string): void {
    this.paperLedgerDetailTradeId = this.paperLedgerDetailTradeId === tradeId ? null : tradeId;
    this.bumpView();
  }

  paperLedgerDetailOpen(tradeId: string): boolean {
    return this.paperLedgerDetailTradeId === tradeId;
  }

  private clampPaperLedgerPage(): void {
    this.paperLedgerPage = Math.min(Math.max(1, this.paperLedgerPage), this.paperLedgerPageCount());
  }

  historyCatalogTradesFiltered(): PaperTradeHistoryItem[] {
    const items = this.historyCatalogTrades();
    if (this.historyPnlFilter === 'positive') {
      return items.filter((t) => t.status === 'CLOSED' && (t.pnl ?? 0) > 0);
    }
    if (this.historyPnlFilter === 'negative') {
      return items.filter((t) => t.status === 'CLOSED' && (t.pnl ?? 0) < 0);
    }
    return items;
  }

  historyCatalogClosedWinners(): PaperTradeHistoryItem[] {
    return this.historyCatalogTrades().filter((t) => t.status === 'CLOSED' && (t.pnl ?? 0) > 0);
  }

  historyCatalogClosedLosers(): PaperTradeHistoryItem[] {
    return this.historyCatalogTrades().filter((t) => t.status === 'CLOSED' && (t.pnl ?? 0) < 0);
  }

  historyCatalogPositivePnlSum(): number {
    return this.historyCatalogClosedWinners().reduce((sum, t) => sum + (t.pnl ?? 0), 0);
  }

  historyCatalogNegativePnlSum(): number {
    return this.historyCatalogClosedLosers().reduce((sum, t) => sum + (t.pnl ?? 0), 0);
  }

  private applyPaperStrategyFocus(focus: PaperStrategyFocus): void {
    this.paperStrategyFilter = focus.strategyId;
    const q = focus.query;
    if (q.from) {
      this.historyDateFrom = q.from;
      this.historyDateTo = q.to;
      this.historyCustomDateOpen = true;
    } else {
      this.historyRange = q.range;
      this.historyDateFrom = null;
      this.historyDateTo = null;
      this.historyCustomDateOpen = false;
    }
    this.resetHistoryListState();
    this.centerView = 'trades';
    const tab = focus.targetTab ?? 'history';
    this.tradePanelTab = tab === 'live' ? 'live' : tab;
    if (tab === 'paper') {
      this.executionMode = 'paper';
    } else if (tab === 'live') {
      this.executionMode = 'live';
      localStorage.setItem(EXECUTION_MODE_KEY, 'live');
    }
    this.historyReload.next();
    queueMicrotask(() => {
      const anchorId = tab === 'paper' ? 'paper-strategy-context' : 'trade-history-dashboard';
      document.getElementById(anchorId)?.scrollIntoView({ behavior: 'smooth', block: 'start' });
    });
  }

  private syncPaperStrategyFocusFromUrl(url: string): void {
    const focus = parsePaperStrategyFocusFromUrl(url);
    if (focus) {
      const current = this.paperStrategyFocus.snapshot();
      if (
        !current
        || current.strategyId !== focus.strategyId
        || JSON.stringify(current.query) !== JSON.stringify(focus.query)
        || current.targetTab !== focus.targetTab
        || current.source !== focus.source
      ) {
        this.paperStrategyFocus.open(focus.strategyId, focus.query, {
          targetTab: focus.targetTab,
          source: focus.source,
        });
      }
      this.applyPaperStrategyFocus(focus);
      return;
    }
    if (!urlHasPaperStrategyFocus(url)) {
      this.paperStrategyFilter = null;
      this.paperStrategyFocus.clear();
    }
  }

  clearPaperStrategyFocus(): void {
    this.paperStrategyFocus.clear();
    this.paperStrategyFilter = null;
    const path = this.tradePanelTab === 'history' ? '/trades/history' : '/trades/paper';
    void this.router.navigateByUrl(path);
  }

  navigateBackToStrategies(): void {
    void this.router.navigateByUrl('/strategies');
  }

  paperStrategyTradesList(): PaperTradeHistoryItem[] {
    return this.paperStrategyFilteredTrades();
  }

  paperStrategyFilteredTrades(): PaperTradeHistoryItem[] {
    const items = this.historyCatalogTrades();
    if (!this.paperStrategyFilter) {
      return items;
    }
    return items.filter((trade) => tradeMatchesCatalogStrategy(trade, this.paperStrategyFilter!));
  }

  paperStrategyInContext(): boolean {
    return !!this.paperStrategyFilter;
  }

  paperStrategyFilterLabel(): string {
    if (!this.paperStrategyFilter) {
      return '';
    }
    return formatPaperHistoryStrategyLabel(this.paperStrategyFilter);
  }

  paperStrategyBadgeLabel(): string {
    const id = this.paperStrategyFilter;
    if (!id) {
      return '';
    }
    const catalogAuto = new Set([
      'bala-10am-high-fade',
      'ten-am-low-break-to-day-open',
      'stock-b1-rejection-to-s2',
    ]);
    if (catalogAuto.has(id)) {
      return 'Paper Auto';
    }
    const zoneCodeMap: Record<string, string> = {
      'zone-b1-break-b2': 'ZONE_B1_BREAK_B2',
      'zone-s1-break-s2': 'ZONE_S1_BREAK_S2',
      'zone-s2-bounce-b1': 'ZONE_S2_BOUNCE_B1',
    };
    const code = zoneCodeMap[id];
    if (!code) {
      return 'Catalog';
    }
    const rows = (this.strategiesStore.snapshotZoneRollout()?.rows ?? []).filter((row) => row.strategyCode === code);
    const autos = rows.filter((row) => row.phase === 'paper_auto');
    if (autos.length) {
      return 'Paper Auto';
    }
    if (rows.some((row) => row.phase === 'monitor')) {
      return 'Monitor Only';
    }
    if (rows.some((row) => row.phase === 'live_blocked')) {
      return 'Live Blocked';
    }
    return 'Catalog';
  }

  paperStrategyUniverseLabel(): string {
    const id = this.paperStrategyFilter;
    if (!id) {
      return '';
    }
    if (id === 'stock-b1-rejection-to-s2') {
      return 'NIFTY · BANKNIFTY · FINNIFTY · SENSEX';
    }
    if (id.startsWith('zone-')) {
      const zoneCodeMap: Record<string, string> = {
        'zone-b1-break-b2': 'ZONE_B1_BREAK_B2',
        'zone-s1-break-s2': 'ZONE_S1_BREAK_S2',
        'zone-s2-bounce-b1': 'ZONE_S2_BOUNCE_B1',
      };
      const code = zoneCodeMap[id];
      const indices = (this.strategiesStore.snapshotZoneRollout()?.rows ?? [])
        .filter((row) => row.strategyCode === code && row.phase === 'paper_auto')
        .map((row) => row.index);
      return indices.length ? indices.join(' · ') : 'Index options';
    }
    return 'Indices · Nifty 50 · options';
  }

  paperTradePeriodLabel(): string {
    if (this.historyDateFilterActive()) {
      return `${this.historyDateFilterLabel()} (IST)`;
    }
    switch (this.historyRange) {
      case 'today':
        return 'Today (IST)';
      case '1w':
        return 'Last 7 days (IST)';
      case '1m':
        return 'Last 1 month';
      default:
        return 'Last 1 month';
    }
  }

  /** Changes when user switches 1D / 1W / 1M / custom — drives overview chart transitions. */
  historyPeriodAnimKey(): string {
    if (this.historyDateFilterActive()) {
      return `custom:${this.historyDateFrom ?? ''}:${this.historyDateTo ?? ''}`;
    }
    return `range:${this.historyRange}`;
  }

  historyCatalogTradesPage(): PaperTradeHistoryItem[] {
    const start = (this.historyPage - 1) * this.historyPageSize;
    return this.historyCatalogTradesFiltered().slice(start, start + this.historyPageSize);
  }

  historyCatalogPageCount(): number {
    const total = this.historyCatalogTradesFiltered().length;
    return Math.max(1, Math.ceil(total / this.historyPageSize));
  }

  setHistoryPnlFilter(filter: 'all' | 'positive' | 'negative'): void {
    this.historyPnlFilter = filter;
    this.resetHistoryListState();
    this.bumpView();
  }

  historyTradeCapital(trade: PaperTradeHistoryItem): number {
    return paperTradeCapitalUsed(trade);
  }

  historyTradeQuantityTotal(trade: PaperTradeHistoryItem): number {
    return (trade.legs ?? []).reduce((sum, leg) => sum + (leg.quantity ?? 0), 0);
  }

  historyTradeQuantityLabel(trade: PaperTradeHistoryItem): string {
    const legs = trade.legs ?? [];
    if (!legs.length) {
      return '—';
    }
    if (legs.length === 1) {
      return String(legs[0].quantity);
    }
    const total = this.historyTradeQuantityTotal(trade);
    return `${total}`;
  }

  historyTradeQuantityTitle(trade: PaperTradeHistoryItem): string {
    const legs = trade.legs ?? [];
    if (!legs.length) {
      return '';
    }
    return legs.map((leg) => `${leg.side} ${leg.symbol} × ${leg.quantity}`).join(' · ');
  }

  setHistoryPage(page: number): void {
    this.historyPage = Math.min(Math.max(1, page), this.historyCatalogPageCount());
    this.historyDetailTradeId = null;
    this.bumpView();
  }

  toggleHistoryDetail(tradeId: string): void {
    this.historyDetailTradeId = this.historyDetailTradeId === tradeId ? null : tradeId;
    this.bumpView();
  }

  historyDetailOpen(tradeId: string): boolean {
    return this.historyDetailTradeId === tradeId;
  }

  private clampHistoryPage(): void {
    this.historyPage = Math.min(Math.max(1, this.historyPage), this.historyCatalogPageCount());
  }

  historyDirectionShort(direction: string): string {
    if (direction === 'BULLISH' || direction === 'LONG') {
      return 'Bull';
    }
    if (direction === 'BEARISH' || direction === 'SHORT') {
      return 'Bear';
    }
    return direction.length > 5 ? direction.slice(0, 5) : direction;
  }

  /** When the position was actually opened (filled); falls back to created for open/closed rows. */
  historyTradeExecutedAt(trade: PaperTradeHistoryItem): string | null {
    if (trade.openedAt) {
      return trade.openedAt;
    }
    if (trade.status === 'OPEN' || trade.status === 'CLOSED') {
      return trade.createdAt;
    }
    return null;
  }

  historyTradeClosedAt(trade: PaperTradeHistoryItem): string | null {
    return trade.closedAt;
  }

  historyTradeHasDetails(trade: PaperTradeHistoryItem): boolean {
    return (
      !!trade.entryReason ||
      !!this.historyTradeExecutedAt(trade) ||
      !!trade.closedAt ||
      trade.legs.length > 0
    );
  }

  historyTradeDurationLabel(trade: PaperTradeHistoryItem): string | null {
    const start = this.historyTradeExecutedAt(trade);
    const end = trade.closedAt;
    if (!start || !end) {
      return null;
    }
    const ms = Date.parse(end) - Date.parse(start);
    if (!Number.isFinite(ms) || ms < 0) {
      return null;
    }
    const mins = Math.round(ms / 60_000);
    if (mins < 60) {
      return `${mins} min`;
    }
    const hours = Math.floor(mins / 60);
    const rem = mins % 60;
    return rem > 0 ? `${hours}h ${rem}m` : `${hours}h`;
  }

  historyTradeEntryFills(trade: PaperTradeHistoryItem): string {
    return trade.legs
      .filter((leg) => leg.entryPrice != null)
      .map(
        (leg) =>
          `${leg.side} ${leg.symbol} × ${leg.quantity} @ ₹${leg.entryPrice!.toFixed(2)}`,
      )
      .join(' · ');
  }

  historyTradeExitFills(trade: PaperTradeHistoryItem): string {
    return trade.legs
      .filter((leg) => leg.exitPrice != null)
      .map(
        (leg) =>
          `${leg.side} ${leg.symbol} × ${leg.quantity} @ ₹${leg.exitPrice!.toFixed(2)}`,
      )
      .join(' · ');
  }

  paperTodayCatalogExits(): PaperTradeHistoryItem[] {
    const seen = new Set<string>();
    const merged: PaperTradeHistoryItem[] = [];
    for (const trade of [...this.paperStopLossTrades(), ...this.paperTargetTrades()]) {
      if (seen.has(trade.id)) {
        continue;
      }
      seen.add(trade.id);
      merged.push(trade);
    }
    return merged.sort((a, b) => {
      const at = Date.parse(this.paperTradeExitAt(a) ?? '') || 0;
      const bt = Date.parse(this.paperTradeExitAt(b) ?? '') || 0;
      return bt - at;
    });
  }

  paperTodayDateLabel(): string {
    return new Intl.DateTimeFormat('en-IN', {
      timeZone: 'Asia/Kolkata',
      day: '2-digit',
      month: 'short',
      year: 'numeric',
    }).format(new Date());
  }

  paperTradeExitAt(trade: PaperTradeHistoryItem): string | null {
    return trade.closedAt ?? trade.createdAt ?? null;
  }

  paperTradeEntryAt(trade: PaperTradeHistoryItem): string | null {
    return trade.openedAt ?? trade.createdAt ?? null;
  }

  paperTradeEntryPrice(trade: PaperTradeHistoryItem): number | null {
    return paperTradeEntryPrice(trade);
  }

  paperTradeExitPrice(trade: PaperTradeHistoryItem): number | null {
    return paperTradeExitPrice(trade);
  }

  paperTradeStopLoss(trade: PaperTradeHistoryItem): number | null {
    return paperTradeStopLoss(trade);
  }

  paperTradeTarget(trade: PaperTradeHistoryItem): number | null {
    return paperTradeTarget(trade);
  }

  paperTradeCapital(trade: PaperTradeHistoryItem): number {
    return paperTradeCapitalUsed(trade);
  }

  paperPositionCapital(position: PaperPosition): number {
    return paperPositionCapitalUsed(position, this.tradeHistory?.trades);
  }

  paperTodayExitCapitalUsed(): number {
    let total = 0;
    for (const trade of [...this.paperStopLossTrades(), ...this.paperTargetTrades()]) {
      total += paperTradeCapitalUsed(trade);
    }
    return total;
  }

  paperProposalCapital(trade: PaperTradeProposal): number {
    if (trade.capitalRequired != null && trade.capitalRequired > 0) {
      return trade.capitalRequired;
    }
    return trade.indicativePrice * trade.quantity;
  }

  paperTodayPnlSummary(): { profit: number; loss: number; net: number } {
    const closed = (this.tradeHistory?.trades ?? []).filter(
      (t) => t.status === 'CLOSED' && t.pnl != null,
    );
    let profit = 0;
    let loss = 0;
    for (const trade of closed) {
      const pnl = trade.pnl ?? 0;
      if (pnl > 0) {
        profit += pnl;
      } else if (pnl < 0) {
        loss += pnl;
      }
    }
    return { profit, loss, net: profit + loss };
  }

  displayPaperTotalPnl(): number {
    if (this.paperAgent != null && this.paperAgent.totalPnl != null) {
      return this.paperAgent.totalPnl;
    }
    if (this.historyRange === 'today' && this.tradeHistory) {
      return this.tradeHistory.summary.realizedPnl;
    }
    return 0;
  }

  displayPaperRealizedPnl(): number {
    if (this.paperAgent?.realizedPnl != null) {
      return this.paperAgent.realizedPnl;
    }
    if (this.historyRange === 'today' && this.tradeHistory) {
      return this.tradeHistory.summary.realizedPnl;
    }
    return 0;
  }

  paperDisplayPositions(): PaperPosition[] {
    return mergePaperPositions(this.paperAgent?.positions, this.tradeHistory?.trades ?? []);
  }

  paperDisplayPositionsForView(): PaperPosition[] {
    const items = this.paperDisplayPositions();
    if (!this.paperStrategyFilter) {
      return items;
    }
    return items.filter(
      (position) => normalizeCatalogStrategyId(position.strategy) === this.paperStrategyFilter,
    );
  }

  paperDirectionIsUp(direction: string): boolean {
    return direction === 'LONG' || direction === 'BULLISH';
  }

  paperDirectionIsDown(direction: string): boolean {
    return direction === 'SHORT' || direction === 'BEARISH';
  }

  paperDisplayUnrealizedPnl(): number {
    if (this.paperAgent?.performance?.unrealizedPnl != null && (this.paperAgent.positions?.length ?? 0) > 0) {
      return this.paperAgent.performance.unrealizedPnl;
    }
    return this.paperDisplayPositions().reduce((sum, p) => sum + (p.pnl ?? 0), 0);
  }

  tradeDetailsPanelTitle(): string {
    switch (this.tradePanelTab) {
      case 'live':
        return 'Live Trading';
      case 'history':
        return 'Trade History';
      case 'agent':
        return 'Agent';
      case 'paper':
        return 'Paper trading';
      default:
        return 'Trade details';
    }
  }

  marketMomentumMode(): MarketSentimentMode {
    return resolveMarketSentimentMode(this.boardData?.counts);
  }

  marketMomentumLabel(): string {
    return marketMomentumBadgeLabel(this.marketMomentumMode());
  }

  themePreviewColor(): string {
    if (this.themeSelection.presetId === 'custom' && this.themeSelection.customBg) {
      return this.themeSelection.customBg;
    }
    const preset = this.themePresets.find((p) => p.id === this.themeSelection.presetId);
    return preset?.preview ?? '#1e1e1e';
  }

  themeCustomColorValue(): string {
    return resolveThemePalette(this.themeSelection).bg;
  }

  isThemePresetActive(id: AppThemePresetId): boolean {
    return this.themeSelection.presetId === id;
  }

  themeCommittedLabel(): string {
    if (this.themeSelection.presetId === 'custom') {
      return 'Custom color';
    }
    const preset = this.themePresets.find((p) => p.id === this.themeSelection.presetId);
    return preset?.label ?? 'Theme';
  }

  themeMenuPreviewBackground(): string {
    if (this.themeHoverPreviewSwatch) {
      return this.themeHoverPreviewSwatch;
    }
    return this.themePreviewColor();
  }

  toggleThemePicker(event: MouseEvent): void {
    event.stopPropagation();
    this.themePickerOpen = !this.themePickerOpen;
    if (!this.themePickerOpen) {
      this.clearThemeMenuHoverPreview();
    }
    this.bumpView();
  }

  closeThemePicker(): void {
    if (!this.themePickerOpen) {
      return;
    }
    this.themePickerOpen = false;
    this.clearThemeMenuHoverPreview();
    this.bumpView();
  }

  selectThemePreset(id: AppThemePresetId): void {
    this.clearThemeMenuHoverPreview();
    this.themeSelection = { presetId: id };
    saveThemeSelection(this.themeSelection);
    this.applyAppTheme();
    this.closeThemePicker();
  }

  onCustomThemeColor(hex: string): void {
    this.clearThemeMenuHoverPreview();
    this.themeSelection = { presetId: 'custom', customBg: hex };
    saveThemeSelection(this.themeSelection);
    this.applyAppTheme();
  }

  onThemePresetHover(id: AppThemePresetId): void {
    const preset = this.themePresets.find((p) => p.id === id);
    this.themePreviewLabel = preset?.label ?? '';
    this.themeHoverPreviewSwatch = preset?.preview ?? '';
  }

  onThemeMenuPointerLeave(): void {
    this.clearThemeMenuHoverPreview();
  }

  private clearThemeMenuHoverPreview(): void {
    this.themePreviewLabel = '';
    this.themeHoverPreviewSwatch = '';
  }

  private applyAppTheme(): void {
    const palette = resolveThemePalette(this.themeSelection);
    applyThemePalette(palette, this.themeSelection.presetId);
    this.syncChartTheme();
  }

  private syncChartTheme(): void {
    const palette = resolveThemePalette(this.themeSelection);
    this.tvChart?.applyOptions?.(lightweightChartDisplayOptions(resolveChartSurfaceFromPalette(palette)));
  }

  canConfirmLive(): boolean {
    return !!this.liveAgent?.featureEnabled
      && !!this.liveAgent?.brokerConnected
      && !!this.liveAgent?.marketSession?.isOpen
      && !!this.liveAgent?.armed
      && !this.liveBusy;
  }

  liveStatusMessage(): string {
    if (!this.liveAgent?.featureEnabled) {
      return 'Live trading is turned off in backend configuration.';
    }
    if (!this.brokerConnected || !this.liveAgent.brokerConnected) {
      return 'Connect your broker before live trading.';
    }
    if (!this.liveAgent.marketSession?.isOpen) {
      const next = this.liveAgent.marketSession?.nextEventAt;
      const nextLabel = next ? ` Next session ${new Date(next).toLocaleString('en-IN', { weekday: 'short', hour: '2-digit', minute: '2-digit', hour12: false, timeZone: 'Asia/Kolkata' })} IST.` : '';
      return `${this.liveAgent.marketSession?.message ?? 'Market is closed.'}${nextLabel}`;
    }
    if (!this.liveAgent.armed) {
      return 'Market is open. Arm live execution before confirming real orders.';
    }
    return this.liveAgent.reconciliationStatus;
  }

  confirmPendingTrade(proposalId: string): void {
    if (this.executionMode === 'paper') {
      this.sendAgentCommand('confirm');
      return;
    }
    if (!this.liveAgent?.featureEnabled) {
      this.liveError = 'Live trading is disabled in backend configuration.';
      return;
    }
    if (!this.brokerConnected || !this.liveAgent.brokerConnected) {
      this.liveError = 'Connect your broker before confirming a live trade.';
      return;
    }
    if (!this.liveAgent.marketSession?.isOpen) {
      this.liveError = this.liveStatusMessage();
      return;
    }
    if (!this.canConfirmLive()) {
      this.liveError = 'Arm live execution before confirming a real-money trade.';
      return;
    }
    this.confirmLive(proposalId);
  }

  armLive(): void {
    if (!this.platformLiveTradingEnabled) {
      this.liveError = 'Live trading is disabled on this server.';
      return;
    }
    if (this.liveBusy) return;
    this.liveBusy = true;
    this.liveError = '';
    this.board.armLive(this.liveAcknowledgement)
      .pipe(
        catchError(() => {
          this.liveError = 'Live agent API is unavailable.';
          return EMPTY;
        }),
        finalize(() => this.liveBusy = false),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((snapshot) => {
        this.liveAgent = snapshot;
        if (snapshot.armed) this.liveAcknowledgement = '';
      });
  }

  disarmLive(): void {
    this.runLiveAction(this.board.disarmLive());
  }

  armZoneAutoLive(): void {
    if (!this.platformLiveTradingEnabled) {
      this.liveError = 'Live trading is disabled on this server.';
      return;
    }
    if (this.liveBusy) return;
    this.liveBusy = true;
    this.liveError = '';
    this.board.armZoneAutoLive(this.zoneAutoLiveAcknowledgement, this.liveProduct)
      .pipe(
        catchError(() => {
          this.liveError = 'Zone go-live API is unavailable.';
          return EMPTY;
        }),
        finalize(() => this.liveBusy = false),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((snapshot) => {
        this.liveAgent = snapshot;
        if (snapshot.zoneAutoLiveArmed) this.zoneAutoLiveAcknowledgement = '';
      });
  }

  canArmZoneAutoLive(): boolean {
    return !!this.liveAgent?.featureEnabled
      && !!this.liveAgent?.brokerConnected
      && !!this.liveAgent?.marketSession?.isOpen
      && !this.liveAgent?.zoneAutoLiveArmed
      && this.zoneAutoLiveAcknowledgement.trim() === this.zoneAutoLivePhrase
      && !this.liveBusy;
  }

  confirmLive(proposalId: string): void {
    this.runLiveAction(this.board.confirmLive(proposalId, this.liveProduct));
  }

  exitLive(tradeId: string): void {
    this.runLiveAction(this.board.exitLive(tradeId));
  }

  handleLiveEod(tradeId: string, action: 'HOLD' | 'EXIT'): void {
    this.runLiveAction(this.board.liveEod(tradeId, action));
  }

  private runLiveAction(request: Observable<LiveExecutionSnapshot>): void {
    if (this.liveBusy) return;
    this.liveBusy = true;
    this.liveError = '';
    request
      .pipe(
        catchError(() => {
          this.liveError = 'Live broker action failed. Check broker status before retrying.';
          return EMPTY;
        }),
        finalize(() => this.liveBusy = false),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((snapshot) => this.liveAgent = snapshot);
  }

  showBrokerConnectionBar(): boolean {
    return this.centerView === 'market';
  }

  brokerHubTriggerLabel(): string {
    if (this.brokerConnected) {
      return this.brokerShortName();
    }
    return 'Broker';
  }

  @HostListener('document:pointerdown', ['$event'])
  onDocumentPointerDown(event: PointerEvent): void {
    const target = event.target;
    if (!(target instanceof Node)) {
      return;
    }

    const hub = this.brokerHubRef?.nativeElement;
    if (hub?.open && !hub.contains(target)) {
      this.closeBrokerHub();
    }

    const themeRoot = this.themePickerRoot?.nativeElement;
    if (this.themePickerOpen && themeRoot && !themeRoot.contains(target)) {
      this.closeThemePicker();
    }
  }

  @HostListener('document:keydown.escape')
  onDocumentEscape(): void {
    this.closeThemePicker();
  }

  closeBrokerHub(): void {
    const hub = this.brokerHubRef?.nativeElement;
    if (hub?.open) {
      hub.open = false;
    }
  }

  openBrokerHubForLogin(): void {
    const hub = this.brokerHubRef?.nativeElement;
    if (hub) {
      hub.open = true;
    }
  }

  brokerHasSavedCredentials(): boolean {
    return hasMStockCredentials();
  }

  loadBrokerCredentialsFromStorage(): void {
    const saved = loadMStockCredentials();
    if (!saved) {
      return;
    }
    this.brokerLoginUsername = saved.username;
    this.brokerLoginPassword = saved.password;
    this.brokerLoginApiKey = saved.apiKey ?? '';
    this.brokerEditingCredentials = false;
    if (!this.brokerConnected) {
      this.brokerMessage = `Saved login for ${saved.username}. Click Request OTP when you need to reconnect.`;
    }
  }

  startBrokerCredentialEdit(): void {
    this.brokerEditingCredentials = true;
    this.otpRequested = false;
    this.otp = '';
    this.brokerMessage = 'Enter your m.Stock user ID and password, then Request OTP.';
    this.bumpView();
  }

  forgetBrokerSavedLogin(): void {
    clearMStockCredentials();
    this.brokerLoginUsername = '';
    this.brokerLoginPassword = '';
    this.brokerLoginApiKey = '';
    this.brokerEditingCredentials = true;
    this.otpRequested = false;
    this.otp = '';
    this.brokerMessage = 'Saved login removed from this device. Enter credentials to connect again.';
    this.bumpView();
  }

  refreshBrokerCapabilities(): void {
    this.http
      .get<{ apiKeyConfigured?: boolean }>('/api/broker/capabilities')
      .pipe(catchError(() => of(null)))
      .subscribe((cap) => {
        if (cap === null) {
          this.brokerCapabilitiesLoaded = false;
        } else {
          this.brokerCapabilitiesLoaded = true;
          this.brokerServerApiKeyConfigured = !!cap.apiKeyConfigured;
        }
        this.bumpView();
      });
  }

  brokerNeedsApiKeyInUi(): boolean {
    if (!this.brokerCapabilitiesLoaded) {
      return false;
    }
    return !this.brokerServerApiKeyConfigured && !this.brokerLoginApiKey.trim();
  }

  brokerIsLoginError(): boolean {
    return brokerLoginRejectedMessage(this.brokerMessage);
  }

  private brokerOtpSendCredentials(): { username: string; password: string; apiKey?: string } | null {
    if (this.brokerHasSavedCredentials() && !this.brokerEditingCredentials) {
      const saved = loadMStockCredentials();
      if (saved) {
        return {
          username: saved.username,
          password: saved.password,
          apiKey: saved.apiKey,
        };
      }
    }
    const username = this.brokerLoginUsername.trim();
    const password = this.brokerLoginPassword;
    const apiKey = this.brokerLoginApiKey.trim();
    if (!username || !password) {
      return null;
    }
    return { username, password, apiKey: apiKey || undefined };
  }

  private resolveBrokerApiKeyForRequest(credentials: { apiKey?: string }): string | undefined {
    const key = credentials.apiKey?.trim();
    if (key) {
      return key;
    }
    if (this.brokerServerApiKeyConfigured) {
      return undefined;
    }
    return undefined;
  }

  private persistBrokerCredentialsAfterLogin(): void {
    const username = this.brokerLoginUsername.trim();
    const password = this.brokerLoginPassword;
    const apiKey = this.brokerLoginApiKey.trim();
    if (username && password) {
      saveMStockCredentials(username, password, apiKey || undefined);
      this.brokerEditingCredentials = false;
      return;
    }
    if (this.brokerHasSavedCredentials()) {
      this.brokerEditingCredentials = false;
    }
  }

  brokerHubStatusClass(): 'live' | 'busy' | 'off' {
    if (this.brokerBusy) {
      return 'busy';
    }
    return this.brokerConnected ? 'live' : 'off';
  }

  isBrokerChoiceActive(id: 'mstock'): boolean {
    return this.brokerProvider === id;
  }

  isBrokerChoiceConnected(id: 'mstock'): boolean {
    return this.brokerConnected && this.brokerProvider === id;
  }

  chooseBroker(id: 'mstock'): void {
    if (this.brokerConnected) {
      return;
    }
    this.brokerProvider = id;
    this.onBrokerProviderChange();
    this.bumpView();
  }

  disconnectBroker(): void {
    if (this.brokerBusy) {
      return;
    }
    this.brokerBusy = true;
    this.http.post<{ status?: string; message?: string }>('/api/broker/session/clear', {}).subscribe({
      next: (res) => {
        this.brokerBusy = false;
        this.brokerConnected = false;
        this.brokerAvailableFunds = null;
        this.brokerUtilizedFunds = null;
        this.otpRequested = false;
        this.otp = '';
        this.brokerMessage = res.message || 'Disconnected. Request OTP to connect again.';
        this.onBrokerProviderChange();
        this.bumpView();
      },
      error: () => {
        this.brokerBusy = false;
        this.brokerMessage = 'Could not disconnect. Try again.';
      },
    });
  }

  private brokerShortName(): string {
    return 'm.Stock';
  }

  showTradeDetails(): void {
    void this.router.navigateByUrl(routerPathForState({
      centerView: 'trades',
      tradePanelTab: this.tradePanelTab,
      marketKind: this.marketKind,
      marketNavTab: this.marketNavTab,
      strategyDetailId: null,
      operationsTab: this.operationsTab,
    }));
  }

  showStrategies(): void {
    void this.router.navigateByUrl('/strategies');
  }

  showManualTrading(): void {
    void this.router.navigateByUrl('/manual');
  }

  showOperations(): void {
    void this.router.navigateByUrl(routerPathForState({
      centerView: 'operations',
      operationsTab: this.operationsTab,
      tradePanelTab: this.tradePanelTab,
      marketKind: this.marketKind,
      marketNavTab: this.marketNavTab,
      strategyDetailId: null,
    }));
  }

  openOperationsAlerts(): void {
    this.operationsTab = 'notifications';
    void this.router.navigateByUrl(
      routerPathForState({
        centerView: 'operations',
        operationsTab: 'notifications',
        tradePanelTab: this.tradePanelTab,
        marketKind: this.marketKind,
        marketNavTab: this.marketNavTab,
        strategyDetailId: null,
      }),
    );
    this.bumpView();
  }

  onUserHubToggle(event: Event): void {
    const details = event.target as HTMLDetailsElement;
    if (!details.open) {
      this.userMenuPanel = 'main';
      this.changePasswordMessage = '';
    }
    this.bumpView();
  }

  onNotificationHubToggle(event: Event): void {
    const details = event.target as HTMLDetailsElement;
    if (details.open) {
      this.refreshNavNotifications();
    }
  }

  refreshNavNotifications(): void {
    this.navNotificationsLoading = true;
    this.bumpView();
    this.board
      .operationsNotifications(25, false)
      .pipe(
        catchError(() => of([] as OperationsNotification[])),
        take(1),
        finalize(() => {
          this.navNotificationsLoading = false;
          this.bumpView();
        }),
      )
      .subscribe((list) => {
        this.navNotifications = list;
        this.navUnreadCount = list.filter((n) => !n.read).length;
        this.bumpView();
      });
  }

  openNotification(note: OperationsNotification): void {
    if (!note.read) {
      void this.board.markOperationsNotificationRead(note.id).subscribe(() => {
        note.read = true;
        this.navUnreadCount = this.navNotifications.filter((n) => !n.read).length;
        this.bumpView();
      });
    }
    this.openOperationsAlerts();
  }

  notificationSeverityClass(severity: string): string {
    const s = (severity || '').toLowerCase();
    if (s.includes('critical') || s.includes('error') || s === 'high') {
      return 'sev-high';
    }
    if (s.includes('warn') || s === 'medium') {
      return 'sev-warn';
    }
    return 'sev-info';
  }

  navUnreadBadgeLabel(): string {
    return this.navUnreadCount > 99 ? '99+' : String(this.navUnreadCount);
  }

  showSuperIntelligence(): void {
    void this.router.navigateByUrl('/intelligence');
  }

  showDashboard(): void {
    this.exitOptionsWorkbench();
    this.marketNavTab = 'dashboard';
    void this.router.navigateByUrl('/market');
    this.persistNavigationHash();
    this.scheduleChartLoad();
    this.bumpView();
  }

  showOptions(): void {
    this.setMarketKind('options');
    this.marketNavTab = 'options';
    void this.router.navigateByUrl('/market/options');
    this.persistNavigationHash();
    this.scheduleChartLoad();
    this.bumpView();
  }

  /** On Dashboard: open cash index chart. On Options: switch index spot chart + option chain. */
  selectIndexTab(name: string): void {
    if (this.marketKind === 'options') {
      this.showOptionsUnderlyingChart(name);
      this.persistNavigationHash();
      return;
    }
    this.selectIndex(name);
    this.scheduleChartLoad(true);
    this.bumpView();
  }

  /** Options workbench: NIFTY / BANKNIFTY / … spot chart (CE/PE rails stay on same index). */
  showOptionsUnderlyingChart(name: string): void {
    this.optionUnderlying = name;
    this.selectIndex(name);
    this.ensureOptionExpiry();
    this.optionsChartMode = 'underlying';
    this.optionChartError = '';
    this.pendingChartName = name;
    this.prepareChartInstrumentSwitch();
    this.optionChainReload.next();
    this.scheduleChartLoad(true);
    this.bumpView();
  }

  private optionsChartShowsContractPremium(): boolean {
    return this.marketKind === 'options' && this.optionsChartMode === 'contract' && !!this.openedOption;
  }

  private exitOptionsWorkbench(): void {
    if (this.marketKind !== 'options') {
      return;
    }
    this.marketKind = 'stocks';
    this.openedOption = null;
    this.optionChartError = '';
    this.loadedChartName = '';
  }

  setHistoryRange(range: TradeHistoryRange): void {
    this.historyRange = range;
    this.historyDateFrom = null;
    this.historyDateTo = null;
    this.historyCustomDateOpen = false;
    this.historyPage = 1;
    this.historyDetailTradeId = null;
    this.historyPnlFilter = 'all';
    this.paperLedgerPage = 1;
    this.paperLedgerDetailTradeId = null;
    this.paperLedgerPnlFilter = 'all';
    this.historyReload.next();
    this.syncStrategyFocusUrlFromState();
  }

  private syncStrategyFocusUrlFromState(): void {
    if (!this.paperStrategyFilter) {
      return;
    }
    const query = this.historyPaperTradesQuery();
    const tab = this.tradePanelTab === 'paper' ? 'paper' : 'history';
    const source = this.paperStrategyFocus.snapshot()?.source ?? 'paper';
    this.paperStrategyFocus.open(this.paperStrategyFilter, query, { targetTab: tab, source });
    void this.router.navigateByUrl(
      buildPaperStrategyTradesUrl(this.paperStrategyFilter, query, { tab, source }),
      { replaceUrl: true },
    );
  }

  setMarketKind(kind: MarketKind): void {
    this.marketKind = kind;
    if (kind === 'options') {
      this.optionsChartMode = 'contract';
      this.optionUnderlying = this.isIndex(this.selectedName) ? this.selectedName : 'NIFTY';
      this.selectIndex(this.optionUnderlying);
      this.ensureOptionExpiry();
      if (!this.openedOption) {
        this.openedOption = this.atmContract(this.optionUnderlying, this.optionExpiry, this.optionSide);
      }
      this.loadedChartName = '';
      this.optionChainReload.next();
    } else {
      this.openedOption = null;
      this.optionChartError = '';
      this.loadedChartName = '';
    }
  }

  openPicker(kind: MarketKind = this.marketKind): void {
    if (this.centerView !== 'market') {
      this.showDashboard();
    }
    this.pickerKind = kind;
    this.pickerQuery = '';
    this.pickerOpen = true;
    if (kind === 'options') {
      this.ensureOptionExpiry();
    }
    this.bumpView();
    queueMicrotask(() => {
      this.symbolSearchInput?.nativeElement.focus();
      this.positionSymbolSearchPanel();
    });
  }

  onSymbolSearchFocus(): void {
    this.pickerOpen = true;
    queueMicrotask(() => this.positionSymbolSearchPanel());
    this.bumpView();
  }

  onSymbolSearchInput(): void {
    this.pickerOpen = true;
    const parsed = parseSymbolSearchQuery(this.pickerQuery);
    if (parsed.indexNames[0]) {
      this.optionUnderlying = parsed.indexNames[0];
      this.ensureOptionExpiry();
    }
    this.positionSymbolSearchPanel();
    this.pickerSearchQuery$.next(this.pickerQuery);
    this.bumpView();
  }

  positionSymbolSearchPanel(): void {
    const el = this.symbolSearchInput?.nativeElement;
    if (!el) {
      return;
    }
    const rect = el.getBoundingClientRect();
    const width = Math.min(Math.max(rect.width, 320), 480);
    const margin = 8;
    let left = rect.left;
    if (left + width > window.innerWidth - margin) {
      left = window.innerWidth - width - margin;
    }
    left = Math.max(margin, left);
    this.symbolSearchPanelTop = rect.bottom + 8;
    this.symbolSearchPanelLeft = left;
    this.symbolSearchPanelWidth = width;
    this.bumpView();
  }

  closePicker(clearQuery = false): void {
    this.pickerOpen = false;
    if (clearQuery) {
      this.pickerQuery = '';
      this.pickerCatalogHits = [];
      this.pickerSearchQuery$.next('');
    }
    this.bumpView();
  }

  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent): void {
    if (!this.pickerOpen) {
      return;
    }
    const root = this.symbolSearchRoot?.nativeElement;
    const panel = document.getElementById('symbolSearchPanel');
    const target = event.target;
    if (!(target instanceof Node)) {
      return;
    }
    if (root?.contains(target) || panel?.contains(target)) {
      return;
    }
    this.pickerOpen = false;
    this.bumpView();
  }

  pickPickerUnderlying(name: string): void {
    this.optionUnderlying = name;
    this.ensureOptionExpiry();
  }

  setPickerKind(kind: MarketKind): void {
    this.pickerKind = kind;
    if (kind === 'options') {
      this.ensureOptionExpiry();
    }
    this.bumpView();
  }

  openStock(row: { name: string }): void {
    const onBoard = this.boardData?.symbols.find((s) => s.name === row.name);
    if (onBoard) {
      this.finishOpenStock(onBoard);
      return;
    }
    this.board
      .ensureSymbol(row.name)
      .pipe(take(1))
      .subscribe({
        next: (snap) => this.finishOpenStock(snap),
        error: () => this.bumpView(),
      });
  }

  private finishOpenStock(row: SymbolSnapshot): void {
    this.exitOptionsWorkbench();
    this.marketKind = 'stocks';
    this.marketNavTab = 'dashboard';
    this.openedOption = null;
    this.mergeBoardSymbol(row);
    this.select(row);
    void this.router.navigateByUrl('/market');
    this.persistNavigationHash();
    this.scheduleChartLoad();
    this.closePicker();
    this.bumpView();
  }

  /** Keep zone chips in sync when a symbol is added from NSE search (not yet on the polled board). */
  private mergeBoardSymbol(row: SymbolSnapshot): void {
    if (!this.boardData) {
      return;
    }
    const idx = this.boardData.symbols.findIndex((s) => s.name === row.name);
    if (idx >= 0) {
      this.boardData.symbols[idx] = row;
    } else {
      this.boardData.symbols = [...this.boardData.symbols, row];
    }
  }

  openOption(contract: OptionContract): void {
    this.marketNavTab = 'options';
    this.marketKind = 'options';
    this.optionsChartMode = 'contract';
    this.openedOption = contract;
    this.optionUnderlying = contract.underlying;
    this.optionExpiry = contract.expiry;
    this.optionSide = contract.side;
    this.selectIndex(contract.underlying);
    this.optionChartError = '';
    this.loadedChartName = '';
    this.optionChainReload.next();
    void this.router.navigateByUrl('/market/options');
    this.persistNavigationHash();
    this.scheduleChartLoad();
    this.closePicker();
    this.bumpView();
  }

  setOptionUnderlying(name: string): void {
    this.showOptionsUnderlyingChart(name);
    this.openedOption = this.atmContract(name, this.optionExpiry, this.optionSide);
    this.optionQuotes = new Map();
  }

  setOptionExpiry(expiry: string): void {
    this.optionExpiry = expiry;
    if (this.openedOption) {
      this.optionsChartMode = 'contract';
      this.openedOption = this.makeContract(
        this.optionUnderlying,
        expiry,
        this.openedOption.strike,
        this.optionSide,
      );
      this.optionChartError = '';
      this.loadedChartName = '';
      this.optionQuotes = new Map();
      this.optionChainReload.next();
      this.scheduleChartLoad();
    }
  }

  setOptionSide(side: OptionSide): void {
    this.optionSide = side;
    if (this.openedOption) {
      this.optionsChartMode = 'contract';
      this.openedOption = this.makeContract(
        this.openedOption.underlying,
        this.openedOption.expiry,
        this.openedOption.strike,
        side,
      );
      this.optionChartError = '';
      this.loadedChartName = '';
      this.scheduleChartLoad();
    }
  }

  pickerQueryActive(): boolean {
    return this.pickerQuery.trim().length > 0;
  }

  pickerStockRows(): Array<{ name: string; last: number | null; subtitle?: string }> {
    const parsed = parseSymbolSearchQuery(this.pickerQuery);
    const rows = this.boardData?.symbols ?? [];
    if (!parsed.raw) {
      const indexSet = new Set(this.indexTabs);
      const indices = this.indexTabs
        .map((name) => rows.find((s) => s.name === name))
        .filter((s): s is SymbolSnapshot => !!s)
        .map((s) => ({ name: s.name, last: this.pickerRowLast(s), subtitle: undefined }));
      const rest = rows
        .filter((s) => !indexSet.has(s.name))
        .slice(0, 6)
        .map((s) => ({ name: s.name, last: this.pickerRowLast(s), subtitle: undefined }));
      return [...indices, ...rest];
    }

    const boardMatches = rows
      .filter((s) => symbolMatchesStockQuery(s.name, [], parsed))
      .map((s) => ({ name: s.name, last: this.pickerRowLast(s), subtitle: undefined }));
    const boardNames = new Set(boardMatches.map((r) => r.name.toUpperCase()));
    const catalogMatches = this.pickerCatalogHits
      .filter((h) => !boardNames.has(h.name.toUpperCase()))
      .filter((h) => symbolMatchesStockQuery(h.name, [h.companyName, h.exchange], parsed))
      .map((h) => ({
        name: h.name,
        last: null,
        subtitle: h.companyName || h.exchange,
      }));
    return [...boardMatches, ...catalogMatches].slice(0, 24);
  }

  pickerSmartOptionRows(): OptionContract[] {
    const parsed = parseSymbolSearchQuery(this.pickerQuery);
    if (!parsed.raw) {
      return [];
    }

    const digitPrefix = digitPrefixFromQuery(parsed.raw);
    const digitsOnly = /^\d[\d,]*$/.test(parsed.raw.replace(/\s/g, ''));
    const matchedStocks = this.pickerStockRows().map((r) => r.name);
    const matchedFno = matchedStocks.filter((s) => hasListedOptions(s));
    const showOptions =
      parsed.strike != null ||
      digitPrefix != null ||
      parsed.optionSide != null ||
      parsed.indexNames.length > 0 ||
      parsed.wantsOptions ||
      digitsOnly ||
      (matchedFno.length > 0 && parsed.alphaTokens.length > 0);

    if (!showOptions) {
      return [];
    }

    const lastPrice = (name: string) => {
      const row = this.indexRow(name) ?? this.boardData?.symbols.find((s) => s.name === name);
      return row?.last ?? 0;
    };

    const underlyings = resolveOptionSearchUnderlyings(parsed, matchedStocks, this.indexTabs, lastPrice);
    if (!underlyings.length) {
      return [];
    }

    const out: OptionContract[] = [];
    for (const underlying of underlyings.slice(0, 2)) {
      const expiry = this.expiries(underlying)[0]?.id ?? '';
      if (!expiry) {
        continue;
      }
      const last = lastPrice(underlying);
      const step = this.isIndex(underlying) ? this.strikeStep(underlying) : equityOptionStrikeStep(last || parsed.strike || 0);
      const strikes: number[] = [];
      if (parsed.strike != null) {
        const rounded = roundStrikeToStep(parsed.strike, step);
        strikes.push(rounded, rounded - step, rounded + step);
        if (isFnoEquityUnderlying(underlying) && !strikes.includes(parsed.strike)) {
          const near = roundStrikeToStep(parsed.strike, step);
          if (Math.abs(near - parsed.strike) <= step) {
            strikes.push(parsed.strike);
          }
        }
      } else if (digitPrefix) {
        strikes.push(...strikesMatchingDigitPrefix(digitPrefix, step, last));
      } else {
        const atm = step > 0 ? roundStrikeToStep(last, step) : last;
        if (atm > 0) {
          strikes.push(atm);
        }
      }
      const sides: OptionSide[] = parsed.optionSide ? [parsed.optionSide] : ['CE', 'PE'];
      for (const strike of [...new Set(strikes.filter((s) => s > 0))].slice(0, 3)) {
        for (const side of sides) {
          out.push(this.makeContract(underlying, expiry, strike, side));
        }
      }
    }
    return out.slice(0, 20);
  }

  pickerOptions(): OptionContract[] {
    const smart = this.pickerSmartOptionRows();
    if (smart.length) {
      return smart;
    }
    const parsed = parseSymbolSearchQuery(this.pickerQuery);
    const q = parsed.raw.toLowerCase();
    const rows = this.optionChain(this.optionUnderlying, this.optionExpiry);
    if (!q) {
      return [];
    }
    const strikeText = parsed.strike != null ? String(parsed.strike) : '';
    return rows
      .filter((c) => {
        if (strikeText && String(c.strike) === strikeText) {
          return true;
        }
        return c.label.toLowerCase().includes(q) || String(c.strike).includes(q);
      })
      .slice(0, 24);
  }

  pickerShowUnifiedSearch(): boolean {
    const parsed = parseSymbolSearchQuery(this.pickerQuery);
    if (!parsed.raw) {
      return false;
    }
    if (parsed.strike != null || digitPrefixFromQuery(parsed.raw)) {
      return true;
    }
    const compact = parsed.raw.replace(/\s/g, '');
    if (/^\d{2,6}$/.test(compact.replace(/,/g, ''))) {
      return true;
    }
    return compact.length >= 1;
  }

  expiries(underlying = this.optionUnderlying): Array<{ id: string; label: string }> {
    const weekday = this.expiryWeekday(underlying);
    const dates: Date[] = [];
    const day = new Date();
    day.setHours(0, 0, 0, 0);
    while (dates.length < 4) {
      if (day.getDay() === weekday) {
        dates.push(new Date(day));
      }
      day.setDate(day.getDate() + 1);
    }
    return dates.map((d) => ({
      id: this.localIsoDate(d),
      label: d.toLocaleDateString('en-GB', { day: '2-digit', month: 'short' }).toUpperCase(),
    }));
  }

  private localIsoDate(d: Date): string {
    const mm = String(d.getMonth() + 1).padStart(2, '0');
    const dd = String(d.getDate()).padStart(2, '0');
    return `${d.getFullYear()}-${mm}-${dd}`;
  }

  optionChain(underlying: string, expiry: string): OptionContract[] {
    const row = this.indexRow(underlying) ?? this.selected;
    const last = row?.last ?? 0;
    const step = this.strikeStep(underlying);
    const atm = step > 0 ? Math.round(last / step) * step : 0;
    const strikes: number[] = [];
    for (let i = -8; i <= 8; i++) {
      const strike = atm + i * step;
      if (strike > 0) {
        strikes.push(strike);
      }
    }
    const expiryId = expiry || this.expiries(underlying)[0]?.id || '';
    const sides: OptionSide[] = ['CE', 'PE'];
    return strikes.flatMap((strike) => sides.map((side) => this.makeContract(underlying, expiryId, strike, side)));
  }

  ceContracts(): OptionContract[] {
    return this.optionCeCache;
  }

  peContracts(): OptionContract[] {
    return this.optionPeCache;
  }

  private refreshOptionContractRails(): void {
    if (!this.optionExpiry && this.marketKind === 'options') {
      this.ensureOptionExpiry();
    }
    const row = this.indexRow(this.optionUnderlying) ?? this.selected;
    let quoteSig = '';
    for (const quote of this.optionQuotes.values()) {
      quoteSig += `${quote.strike}|${quote.side}:${quote.last};`;
    }
    const rev = `${this.optionUnderlying}|${this.optionExpiry}|${row?.last ?? 0}|${quoteSig}`;
    if (rev === this.optionListRev) {
      return;
    }
    this.optionListRev = rev;
    const chain = this.optionChain(this.optionUnderlying, this.optionExpiry);
    this.optionCeCache = chain.filter((contract) => contract.side === 'CE');
    this.optionPeCache = chain.filter((contract) => contract.side === 'PE');
  }

  private refreshOpenedOptionQuote(): void {
    if (!this.openedOption) {
      return;
    }
    const refreshed = this.makeContract(
      this.openedOption.underlying,
      this.openedOption.expiry,
      this.openedOption.strike,
      this.openedOption.side,
    );
    this.openedOption.last = refreshed.last;
    this.openedOption.changePoints = refreshed.changePoints;
    this.openedOption.changePercent = refreshed.changePercent;
    this.openedOption.previousClose = refreshed.previousClose;
  }

  optionSelected(contract: OptionContract): boolean {
    return (
      this.openedOption?.label === contract.label &&
      this.openedOption?.expiry === contract.expiry &&
      this.openedOption?.side === contract.side
    );
  }

  optionTone(contract: OptionContract): 'up' | 'down' | 'flat' {
    if ((contract.changePercent ?? 0) > 0) {
      return 'up';
    }
    if ((contract.changePercent ?? 0) < 0) {
      return 'down';
    }
    return 'flat';
  }

  optionIsAtm(contract: OptionContract): boolean {
    const row = this.indexRow(contract.underlying) ?? this.selected;
    const step = this.strikeStep(contract.underlying);
    if (!row || step <= 0) {
      return false;
    }
    const atm = Math.round(row.last / step) * step;
    return contract.strike === atm;
  }

  private optionQuoteKey(strike: number, side: OptionSide): string {
    return `${strike}|${side}`;
  }

  private ensureOptionExpiry(): void {
    const ids = this.expiries(this.optionUnderlying).map((e) => e.id);
    if (!this.optionExpiry || !ids.includes(this.optionExpiry)) {
      this.optionExpiry = ids[0] ?? '';
    }
  }

  private atmContract(underlying: string, expiry: string, side: OptionSide): OptionContract {
    const chain = this.optionChain(underlying, expiry).filter((c) => c.side === side);
    const mid = chain[Math.floor(chain.length / 2)];
    return mid ?? this.makeContract(underlying, expiry, 0, side);
  }

  private makeContract(underlying: string, expiry: string, strike: number, side: OptionSide): OptionContract {
    const expiryLabel =
      this.expiries(underlying).find((e) => e.id === expiry)?.label ?? expiry;
    const strikeText = Number.isInteger(strike) ? String(strike) : strike.toFixed(2);
    const quote = this.optionQuotes.get(this.optionQuoteKey(strike, side));
    return {
      underlying,
      expiry,
      expiryLabel,
      strike,
      side,
      label: `${underlying} ${expiryLabel} ${strikeText} ${side}`,
      last: quote?.last,
      changePoints:
        quote && quote.previousClose > 0
          ? quote.last - quote.previousClose
          : undefined,
      changePercent: quote?.changePercent,
      previousClose: quote?.previousClose,
    };
  }

  private expiryWeekday(underlying: string): number {
    if (underlying === 'BANKNIFTY') {
      return 3;
    }
    if (underlying === 'SENSEX') {
      return 5;
    }
    return 2;
  }

  private strikeStep(underlying: string): number {
    if (underlying === 'BANKNIFTY' || underlying === 'SENSEX') {
      return 100;
    }
    if (underlying === 'FINNIFTY' || underlying === 'NIFTY') {
      return 50;
    }
    const spot = this.indexRow(underlying)?.last ?? 0;
    return equityOptionStrikeStep(spot);
  }

  displayAvailableFunds(): number | null {
    return this.brokerAvailableFunds ?? this.paperAgent?.brokerAvailableFunds ?? null;
  }

  displayUtilizedFunds(): number | null {
    return this.brokerUtilizedFunds ?? this.paperAgent?.brokerUtilizedFunds ?? null;
  }

  setLiveBookView(view: LiveBookView): void {
    this.liveBookView = view;
    if (view === 'orders') {
      this.liveOrderFilter = 'executed';
    }
    this.bumpView();
  }

  setLiveOrderFilter(filter: LiveOrderFilter): void {
    this.liveOrderFilter = filter;
    this.bumpView();
  }

  /** Proposal awaiting live confirm or broker submit (live API first, then paper agent). */
  livePendingProposal(): PaperTradeProposal | null {
    return this.liveAgent?.pendingProposal ?? this.paperAgent?.pendingTrade ?? null;
  }

  liveExecutionTrades(): LiveTrade[] {
    const trades = this.liveAgent?.trades ?? [];
    return [...trades].sort((left, right) => Date.parse(right.createdAt) - Date.parse(left.createdAt));
  }

  liveActiveExecutionTrades(): LiveTrade[] {
    return this.liveExecutionTrades().filter((trade) => this.liveTradeIsInFlight(trade.status));
  }

  liveTradeIsInFlight(status: string): boolean {
    const normalized = status.trim().toUpperCase();
    return (
      normalized === 'SUBMITTING'
      || normalized === 'OPEN'
      || normalized === 'OPEN_NRML'
      || normalized === 'EXITING'
    );
  }

  liveTradeStatusLabel(status: string): string {
    const normalized = status.trim().toUpperCase();
    switch (normalized) {
      case 'SUBMITTING':
        return 'Submitting to broker';
      case 'OPEN':
        return 'Open (MIS)';
      case 'OPEN_NRML':
        return 'Open (NRML)';
      case 'EXITING':
        return 'Exiting';
      case 'CLOSED':
        return 'Closed';
      case 'FAILED':
      case 'EXIT_FAILED':
      case 'CONVERSION_FAILED':
        return 'Failed';
      default:
        return status;
    }
  }

  liveEventsForTrade(tradeId: string): LiveExecutionEvent[] {
    return (this.liveAgent?.events ?? [])
      .filter((event) => event.tradeId === tradeId)
      .sort((left, right) => Date.parse(right.at) - Date.parse(left.at))
      .slice(0, 6);
  }

  liveRecentExecutionEvents(): LiveExecutionEvent[] {
    return [...(this.liveAgent?.events ?? [])]
      .sort((left, right) => Date.parse(right.at) - Date.parse(left.at))
      .slice(0, 8);
  }

  liveOpenPositionCount(): number {
    return (this.liveAgent?.brokerPositions ?? []).filter((row) => row.quantity !== 0).length;
  }

  livePositionRows() {
    return this.liveAgent?.brokerPositions ?? [];
  }

  liveAllOrderRows() {
    return [...(this.liveAgent?.brokerOrders ?? [])].sort((left, right) => {
      const leftTime = left.updatedAt ? Date.parse(left.updatedAt) : 0;
      const rightTime = right.updatedAt ? Date.parse(right.updatedAt) : 0;
      return rightTime - leftTime;
    });
  }

  livePendingOrderCount(): number {
    return this.liveAllOrderRows().filter((row) => this.isPendingOrder(row)).length;
  }

  liveExecutedOrderCount(): number {
    return this.liveAllOrderRows().filter((row) => this.isExecutedOrder(row)).length;
  }

  liveTriggerOrderCount(): number {
    return this.liveAllOrderRows().filter((row) => this.isTriggerOrder(row)).length;
  }

  liveFilteredOrders() {
    return this.liveAllOrderRows().filter((row) => {
      if (this.liveOrderFilter === 'pending') return this.isPendingOrder(row);
      if (this.liveOrderFilter === 'executed') return this.isExecutedOrder(row);
      return this.isTriggerOrder(row);
    });
  }

  livePositionLtp(row: { lastPrice: number; averagePrice: number; quantity: number }): number | null {
    return row.lastPrice > 0 ? row.lastPrice : null;
  }

  livePositionLtpChange(row: { lastPrice: number; averagePrice: number; quantity: number }): { points: number; percent: number } | null {
    if (row.quantity === 0 || row.lastPrice <= 0 || row.averagePrice <= 0) {
      return null;
    }
    const points = row.lastPrice - row.averagePrice;
    return { points, percent: (points / row.averagePrice) * 100 };
  }

  liveOrderLtp(symbol: string): number | null {
    const position = (this.liveAgent?.brokerPositions ?? []).find((row) => row.tradingSymbol === symbol);
    return position && position.lastPrice > 0 ? position.lastPrice : null;
  }

  liveOrderLtpChange(symbol: string, tradedPrice: number): { points: number; percent: number } | null {
    const ltp = this.liveOrderLtp(symbol);
    if (ltp == null || tradedPrice <= 0) {
      return null;
    }
    const points = ltp - tradedPrice;
    return { points, percent: (points / tradedPrice) * 100 };
  }

  formatLiveSymbol(symbol: string): { title: string; subtitle: string; right: string } {
    const match = symbol.match(/^([A-Z]+)(\d{2})([A-Z]{3})(\d+)(CE|PE)$/i);
    if (!match) {
      return { title: symbol, subtitle: '', right: '' };
    }
    return {
      title: match[1],
      subtitle: `${match[2]} ${match[3]} ${match[4]}`,
      right: match[5].toUpperCase(),
    };
  }

  liveOrderStatusLabel(status: string): string {
    const normalized = status.trim().toUpperCase();
    if (normalized.includes('COMPLETE') || normalized.includes('FILLED')) {
      return 'Executed';
    }
    if (normalized.includes('CANCEL')) {
      return 'Cancelled';
    }
    if (normalized.includes('REJECT')) {
      return 'Rejected';
    }
    if (normalized.includes('TRIGGER')) {
      return 'Trigger pending';
    }
    return status || 'Pending';
  }

  isPendingOrder(order: { status: string; filledQuantity: number; quantity: number; orderType: string }): boolean {
    const status = order.status.trim().toUpperCase();
    if (this.isExecutedOrder(order) || this.isTriggerOrder(order)) {
      return false;
    }
    return status.includes('OPEN') || status.includes('PENDING') || status.includes('AMO') || order.filledQuantity < order.quantity;
  }

  isExecutedOrder(order: { status: string; filledQuantity: number; quantity: number }): boolean {
    const status = order.status.trim().toUpperCase();
    return status.includes('COMPLETE') || status.includes('FILLED')
      || (order.quantity > 0 && order.filledQuantity >= order.quantity);
  }

  isTriggerOrder(order: { status: string; orderType: string }): boolean {
    const type = order.orderType.trim().toUpperCase();
    const status = order.status.trim().toUpperCase();
    return type.includes('SL') || type.includes('TRIGGER') || type.includes('STOP') || status.includes('TRIGGER');
  }

  readonly liveTerminalIndices = ['NIFTY', 'BANKNIFTY', 'FINNIFTY', 'SENSEX'];
  readonly liveExecutionStepLabels = ['Signal', 'Submitted', 'Filled', 'Managing', 'Exit'];

  liveBrokerOnline(): boolean {
    return this.brokerConnected && !!this.liveAgent?.brokerConnected;
  }

  liveTerminalActiveCount(): number {
    return this.liveActiveExecutionTrades().length;
  }

  liveSyncLabel(): string {
    const at = this.liveAgent?.updatedAt;
    if (!at) {
      return '—';
    }
    return new Date(at).toLocaleString('en-IN', {
      day: '2-digit',
      month: 'short',
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit',
      hour12: false,
      timeZone: 'Asia/Kolkata',
    }) + ' IST';
  }

  liveActivityFeedEvents(): LiveExecutionEvent[] {
    return [...(this.liveAgent?.events ?? [])]
      .sort((left, right) => Date.parse(right.at) - Date.parse(left.at))
      .slice(0, 24);
  }

  liveTradeProposalFor(trade: LiveTrade): PaperTradeProposal | null {
    const pending = this.livePendingProposal();
    if (pending?.id === trade.id) {
      return pending;
    }
    return null;
  }

  liveTradeRiskFrame(trade: LiveTrade): {
    stopLoss: number | null;
    target: number | null;
    maxRisk: number;
    maxReward: number;
    rewardRisk: number;
  } {
    const proposal = this.liveTradeProposalFor(trade);
    if (proposal) {
      return {
        stopLoss: proposal.stopLoss,
        target: proposal.target,
        maxRisk: proposal.maxRisk || 0,
        maxReward: proposal.maxReward || 0,
        rewardRisk: proposal.rewardRisk || 0,
      };
    }
    const hist = (this.tradeHistory?.trades ?? []).find((row) => row.id === trade.id);
    if (hist) {
      const rr = hist.maxRisk > 0 ? hist.maxReward / hist.maxRisk : 0;
      return {
        stopLoss: hist.stopLoss ?? null,
        target: hist.target ?? null,
        maxRisk: hist.maxRisk,
        maxReward: hist.maxReward,
        rewardRisk: rr,
      };
    }
    return { stopLoss: null, target: null, maxRisk: 0, maxReward: 0, rewardRisk: 0 };
  }

  liveTradeEntryOrders(trade: LiveTrade): LiveOrder[] {
    const entry = trade.orders.filter((order) => order.purpose === 'ENTRY' && order.filledQuantity > 0 && order.averagePrice > 0);
    if (entry.length) {
      return entry;
    }
    return trade.orders.filter((order) => order.filledQuantity > 0 && order.averagePrice > 0);
  }

  liveTradeEntryAvg(trade: LiveTrade): number | null {
    const orders = this.liveTradeEntryOrders(trade);
    if (!orders.length) {
      return null;
    }
    let qty = 0;
    let value = 0;
    for (const order of orders) {
      const filled = order.filledQuantity;
      qty += filled;
      value += order.averagePrice * filled;
    }
    return qty > 0 ? value / qty : null;
  }

  liveTradeLivePnl(trade: LiveTrade): number {
    const symbols = new Set(trade.orders.map((order) => order.symbol));
    return (this.liveAgent?.brokerPositions ?? [])
      .filter((row) => symbols.has(row.tradingSymbol) && row.quantity !== 0)
      .reduce((sum, row) => sum + row.pnl, 0);
  }

  liveTradeMarkPrice(trade: LiveTrade): number | null {
    const orders = this.liveTradeEntryOrders(trade);
    if (!orders.length) {
      return null;
    }
    const positions = this.liveAgent?.brokerPositions ?? [];
    let qty = 0;
    let value = 0;
    for (const order of orders) {
      const position = positions.find((row) => row.tradingSymbol === order.symbol);
      const ltp = position && position.lastPrice > 0 ? position.lastPrice : order.averagePrice;
      const filled = order.filledQuantity;
      qty += filled;
      value += ltp * filled;
    }
    return qty > 0 ? value / qty : null;
  }

  liveTradeProgress(trade: LiveTrade): { percent: number; markerPercent: number } | null {
    const { stopLoss, target } = this.liveTradeRiskFrame(trade);
    const entry = this.liveTradeEntryAvg(trade);
    const current = this.liveTradeMarkPrice(trade);
    if (stopLoss == null || target == null || entry == null || current == null) {
      return null;
    }
    const span = target - stopLoss;
    if (Math.abs(span) < 1e-6) {
      return null;
    }
    const percent = Math.min(100, Math.max(0, ((current - stopLoss) / span) * 100));
    const markerPercent = Math.min(100, Math.max(0, ((entry - stopLoss) / span) * 100));
    return { percent, markerPercent };
  }

  liveTradeStepIndex(status: string): number {
    const normalized = status.trim().toUpperCase();
    if (normalized === 'SUBMITTING') {
      return 1;
    }
    if (normalized === 'OPEN' || normalized === 'OPEN_NRML') {
      return 3;
    }
    if (normalized === 'EXITING') {
      return 4;
    }
    if (normalized === 'CLOSED') {
      return 5;
    }
    if (normalized.includes('FAIL')) {
      return 1;
    }
    return 0;
  }

  liveTradeStepState(trade: LiveTrade, step: number): 'done' | 'active' | 'pending' | 'failed' {
    const normalized = trade.status.trim().toUpperCase();
    const failed = normalized.includes('FAIL');
    const current = this.liveTradeStepIndex(trade.status);
    if (failed && step === 1) {
      return 'failed';
    }
    if (step < current) {
      return 'done';
    }
    if (step === current) {
      return failed ? 'failed' : 'active';
    }
    return 'pending';
  }

  liveIndexMonitorState(index: string): 'active' | 'ready' | 'offline' {
    if (!this.liveBrokerOnline()) {
      return 'offline';
    }
    const active = this.liveActiveExecutionTrades().some(
      (trade) => trade.underlying.trim().toUpperCase() === index,
    );
    return active ? 'active' : 'ready';
  }

  liveTerminalShowEmpty(): boolean {
    return !this.livePendingProposal()
      && this.liveActiveExecutionTrades().length === 0;
  }

  formatLiveStrategyLabel(strategy: string): string {
    return formatPaperHistoryStrategyLabel(strategy);
  }

  /** History/live fallback when strategy name missing — defaults blank types to OPTION. */
  displayInstrumentType(raw: string | null | undefined): string {
    return formatInstrumentTypeDisplay(raw);
  }

  liveTradeCapitalUsed(trade: LiveTrade): number {
    const proposal = this.liveTradeProposalFor(trade);
    if (proposal) {
      return this.paperProposalCapital(proposal);
    }
    const hist = (this.tradeHistory?.trades ?? []).find((row) => row.id === trade.id);
    if (hist?.capitalRequired != null && hist.capitalRequired > 0) {
      return hist.capitalRequired;
    }
    const entry = this.liveTradeEntryAvg(trade);
    const qty = this.liveTradeTotalQuantity(trade);
    return entry != null && qty > 0 ? entry * qty : 0;
  }

  liveTradeTotalQuantity(trade: LiveTrade): number {
    const orders = this.liveTradeEntryOrders(trade);
    if (!orders.length) {
      return trade.orders.reduce((sum, order) => sum + order.quantity, 0);
    }
    return orders.reduce((sum, order) => sum + order.filledQuantity, 0);
  }

  liveTradeDirection(trade: LiveTrade): string {
    const proposal = this.liveTradeProposalFor(trade);
    if (proposal?.direction) {
      return proposal.direction;
    }
    const hist = (this.tradeHistory?.trades ?? []).find((row) => row.id === trade.id);
    return hist?.direction ?? '—';
  }

  liveFundsAvailableOk(): boolean {
    const funds = this.displayAvailableFunds();
    return funds != null && funds > 0;
  }

  liveMarketOpen(): boolean {
    return !!this.liveAgent?.marketSession?.isOpen;
  }

  liveActiveProductLabel(): string {
    if (this.liveAgent?.zoneAutoLiveArmed) {
      return this.liveAgent.zoneAutoLiveProduct || this.liveProduct;
    }
    return this.liveProduct;
  }

  liveActivityEventHeadline(event: LiveExecutionEvent): string {
    const type = event.type.trim().toUpperCase();
    if (type.includes('SIGNAL')) {
      return 'Signal generated';
    }
    if (type.includes('SUBMIT') || type.includes('ORDER')) {
      return 'Order submitted';
    }
    if (type.includes('FILL') || type.includes('COMPLETE')) {
      return 'Order filled';
    }
    if (type.includes('PNL') || type.includes('P/L')) {
      return 'P&L updated';
    }
    if (type.includes('EXIT')) {
      return 'Exit triggered';
    }
    return event.type || 'Event';
  }

  viewLiveTradeDetails(tradeId: string): void {
    this.liveDetailTradeId = tradeId;
    this.setLiveBookView('orders');
    this.bumpView();
    queueMicrotask(() => {
      document.getElementById('live-broker-book')?.scrollIntoView({ behavior: 'smooth', block: 'start' });
    });
  }

  liveTradeDetailsExpanded(tradeId: string): boolean {
    return this.liveDetailTradeId === tradeId;
  }

  onBrokerProviderChange(): void {
    if (this.brokerConnected || this.otpRequested || this.brokerBusy) {
      return;
    }
    this.otp = '';
    this.brokerMessage = 'Click Request OTP, then enter the code from your phone.';
    this.bumpView();
  }

  private brokerHttpErrorMessage(err: unknown, brokerLabel: string): string {
    if (err && typeof err === 'object' && 'name' in err && (err as { name: string }).name === 'TimeoutError') {
      return `${brokerLabel} request timed out. SMS may still arrive — enter the code if you received it.`;
    }
    if (err instanceof HttpErrorResponse) {
      if (err.status === 404) {
        return `${brokerLabel} login API not found (404). Restart the backend from the latest code — your running API on port 45211 is outdated.`;
      }
      if (err.status === 0) {
        return `Cannot reach the backend (port 45211). Run start-app.ps1 or restart the backend window, then use http://127.0.0.1:45212 (not a file:// URL).`;
      }
      if (err.status === 504 || err.status === 408) {
        return `${brokerLabel} request timed out. The server may still have triggered SMS.`;
      }
      const body = err.error as { message?: string } | null;
      if (body?.message) {
        return body.message;
      }
      return `${brokerLabel} request failed (HTTP ${err.status}).`;
    }
    return `${brokerLabel} connect failed. Is the backend running on port 45211?`;
  }

  private focusBrokerOtpInput(): void {
    queueMicrotask(() => {
      const input = document.querySelector<HTMLInputElement>('input.broker-otp-input:not([disabled])');
      input?.focus();
      input?.select();
    });
  }

  requestBrokerOtp(): void {
    if (this.brokerConnected) {
      return;
    }
    const credentials = this.brokerOtpSendCredentials();
    if (!credentials) {
      this.brokerMessage = 'Enter your m.Stock user ID and password, then Request OTP.';
      this.brokerEditingCredentials = true;
      this.openBrokerHubForLogin();
      this.bumpView();
      return;
    }
    if (this.brokerNeedsApiKeyInUi()) {
      this.brokerMessage =
        'm.Stock Type A API key is required. Set MStock:ApiKey in backend user secrets (see HOW-TO-RUN.md) or expand One-time setup in this panel and paste your key from trade.mstock.com → API.';
      this.brokerEditingCredentials = true;
      this.openBrokerHubForLogin();
      this.bumpView();
      return;
    }
    this.persistBrokerCredentialsAfterLogin();
    this.brokerProvider = 'mstock';
    // Keep entry open for the whole OTP flow (SMS can arrive even if HTTP times out).
    this.otpRequested = true;
    this.brokerBusy = true;
    this.otp = '';
    this.brokerMessage = 'Requesting OTP from m.Stock…';
    this.bumpView();
    this.http
      .post<{ status?: string; message?: string }>('/api/broker/otp/send', {
        username: credentials.username,
        password: credentials.password,
        apiKey: this.resolveBrokerApiKeyForRequest(credentials),
      })
      .pipe(timeout(90_000))
      .subscribe({
        next: (res) => {
          this.brokerBusy = false;
          this.brokerConnected = false;
          this.brokerAvailableFunds = null;
          this.brokerUtilizedFunds = null;
          if (brokerOtpSendSucceeded(res)) {
            this.otpRequested = true;
            this.brokerMessage = 'OTP sent. Enter the code below and click Submit OTP.';
            this.focusBrokerOtpInput();
          } else if (brokerLoginRejectedMessage(res.message)) {
            this.otpRequested = false;
            this.brokerMessage =
              (res.message || 'Login failed.')
              + ' Use your m.Stock client ID or registered mobile and the same password as the m.Stock app. Check API key if you use Type A API.';
            this.openBrokerHubForLogin();
          } else {
            this.otpRequested = true;
            this.brokerMessage =
              (res.message || 'Broker did not confirm OTP delivery.')
              + ' If you already received SMS, enter the code and click Submit.';
            this.focusBrokerOtpInput();
          }
          this.bumpView();
        },
        error: (err: unknown) => {
          this.brokerBusy = false;
          this.otpRequested = true;
          this.brokerMessage =
            this.brokerHttpErrorMessage(err, 'OTP')
            + ' If you received SMS, enter the code and click Submit.';
          this.bumpView();
          this.focusBrokerOtpInput();
        },
      });
  }

  submitBrokerOtp(): void {
    if (!this.otpRequested) {
      this.brokerMessage = 'Click Request OTP first.';
      return;
    }
    const code = this.otp.trim();
    if (!code) {
      this.brokerMessage = 'Enter the OTP from your phone.';
      return;
    }
    if (this.brokerNeedsApiKeyInUi()) {
      this.brokerMessage =
        'Before Submit OTP: set MStock:ApiKey once in backend user secrets, or add your Type A API key under One-time setup in this panel.';
      this.openBrokerHubForLogin();
      this.bumpView();
      return;
    }
    this.brokerBusy = true;
    this.brokerMessage = 'Verifying OTP with m.Stock…';
    this.bumpView();
    const credentials = this.brokerOtpSendCredentials();
    this.http
      .post<{ status?: string; connected?: boolean; message?: string }>('/api/broker/otp/verify', {
        otp: code,
        apiKey: credentials ? this.resolveBrokerApiKeyForRequest(credentials) : undefined,
      })
      .pipe(
        timeout(45_000),
        finalize(() => {
          this.brokerBusy = false;
          this.bumpView();
        }),
      )
      .subscribe({
      next: (res) => {
        this.brokerConnected = !!res.connected;
        if (this.brokerConnected) {
          this.otp = '';
          this.otpRequested = false;
          this.persistBrokerCredentialsAfterLogin();
        } else {
          this.otpRequested = true;
        }
        this.brokerMessage = res.message || (this.brokerConnected ? 'Live market prices active.' : 'OTP submit failed.');
        if (this.brokerConnected) {
          this.closeBrokerHub();
          this.brokerSession.refresh();
          this.boardPoll.retry();
        } else {
          this.brokerAvailableFunds = null;
          this.brokerUtilizedFunds = null;
          this.focusBrokerOtpInput();
        }
        this.bumpView();
      },
      error: (err: unknown) => {
        this.otpRequested = true;
        this.brokerMessage = this.brokerHttpErrorMessage(err, 'OTP submit');
        this.focusBrokerOtpInput();
      },
    });
  }

  private applyBrokerSession(res: {
    connected?: boolean;
    provider?: string | null;
    displayName?: string | null;
    availableFunds?: number | null;
    utilizedFunds?: number | null;
  }): void {
    const wasConnected = this.brokerConnected;
    this.brokerConnected = !!res.connected;
    if (this.brokerConnected && !wasConnected) {
      this.closeBrokerHub();
      this.loadedChartName = '';
      this.lastChart = undefined;
      this.scheduleChartLoad();
    }
    if (res.provider === 'mstock') {
      this.brokerProvider = 'mstock';
    }
    if (res.displayName) {
      this.brokerDisplayName = res.displayName;
    }
    if (this.brokerConnected) {
      this.brokerAvailableFunds = res.availableFunds ?? this.brokerAvailableFunds;
      this.brokerUtilizedFunds = res.utilizedFunds ?? this.brokerUtilizedFunds;
      if (!this.otpRequested && !this.brokerBusy) {
        this.brokerMessage = `Connected to ${this.brokerDisplayName}. Live market prices active.`;
      }
      this.bumpView();
      return;
    }

    this.brokerAvailableFunds = null;
    this.brokerUtilizedFunds = null;
    if (!this.otpRequested && !this.brokerBusy) {
      this.onBrokerProviderChange();
    }
    this.bumpView();
  }

  retry(): void {
    this.state = 'loading';
    this.lastAppliedBoardKey = '';
    this.boardPoll.retry();
    this.bumpView();
  }

  setChartInterval(id: string): void {
    this.chartInterval = id;
    this.loadedChartName = '';
    this.chartBusy = false;
    this.drawTool = 'none';
    this.trendFrom = undefined;
    this.drawingStatus = '';
    this.scheduleChartLoad(true);
    this.bumpView();
  }

  setChartViewMode(mode: 'full' | 'max'): void {
    this.chartViewMode = mode;
    this.bumpView();
    queueMicrotask(() => {
      this.tvChart?.timeScale().fitContent();
      this.tvChart?.resize(this.chartHost?.nativeElement?.clientWidth ?? 0, this.chartHost?.nativeElement?.clientHeight ?? 0);
    });
  }

  cycleChartViewMode(): void {
    this.setChartViewMode(this.chartViewMode === 'full' ? 'max' : 'full');
  }

  chartIstClock(): string {
    return new Intl.DateTimeFormat('en-IN', {
      timeZone: 'Asia/Kolkata',
      hour12: false,
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit',
    }).format(new Date());
  }

  refreshChart(): void {
    if (this.centerView !== 'market' || this.state !== 'ready') {
      return;
    }
    this.chartRefreshPending = true;
    this.chartFullHistoryPending = true;
    this.chartPaintedFromCache = !!this.lastChart?.candles?.length;
    this.loadedChartName = '';
    this.chartBusy = false;
    this.chartPollInFlight = false;
    this.scheduleChartLoad(true);
    this.bumpView();
  }

  chartDataRangeLabel(): string {
    const candles = this.lastChart?.candles;
    if (!candles?.length) {
      return '';
    }
    const tf = this.timeframes.find((t) => t.id === this.chartInterval);
    const sessions = sessionStartBars(candles).length;
    const first = istDateTime(candles[0].time, false);
    const last = istDateTime(candles[candles.length - 1].time, false);
    if (this.chartInterval === 'day') {
      return `${tf?.label ?? 'Daily'} · ${candles.length} trading days · ${first} – ${last}`;
    }
    const sessionNote =
      sessions <= 1
        ? this.chartFullHistoryPending
          ? ' · loading prior sessions…'
          : ' · only today’s session — try Daily for months, or Refresh'
        : ` · ${sessions} session(s)`;
    return `${tf?.label ?? this.chartInterval} · ${candles.length} bars${sessionNote} · ${first} – ${last}`;
  }

  activeTimeframeHint(): string {
    return this.timeframes.find((t) => t.id === this.chartInterval)?.hint ?? '';
  }

  chartHistoryStatusLabel(): string {
    const hasCandles = !!this.lastChart?.candles?.length;
    if (!hasCandles) {
      return '';
    }
    if (!isIstCashSessionOpen(Math.floor(Date.now() / 1000))) {
      return 'Market closed';
    }
    if (this.chartRefreshPending) {
      return this.chartPaintedFromCache ? 'Cached · refreshing…' : 'Refreshing…';
    }
    if (this.chartFullHistoryPending || (this.chartBusy && hasCandles)) {
      return this.chartPaintedFromCache ? 'Cached · updating history…' : 'Updating history…';
    }
    if (this.chartPaintedFromCache) {
      return 'Cached';
    }
    return this.chartStreamReceiving ? 'Live · stream' : 'Live';
  }

  chartHistoryStatusKind(): string {
    const label = this.chartHistoryStatusLabel();
    if (label.startsWith('Cached ·')) {
      return 'cached-updating';
    }
    if (label === 'Cached') {
      return 'cached';
    }
    if (label.includes('Updating') || label.includes('Refreshing')) {
      return 'updating';
    }
    if (label === 'Live' || label === 'Live · stream') {
      return 'live';
    }
    if (label === 'Market closed') {
      return 'cached';
    }
    return '';
  }

  chartViewModeLabel(): string {
    return this.chartViewMode === 'max' ? 'Max chart' : 'Chart';
  }

  clearDrawings(): void {
    this.drawings = [];
    this.selectedDrawingId = '';
    this.saveDrawings();
    this.renderDrawings();
  }

  setDrawTool(tool: DrawTool): void {
    this.drawTool = this.drawTool === tool ? 'none' : tool;
    this.trendFrom = undefined;
    this.drawingStatus =
      this.drawTool === 'hline'
        ? 'Click the chart to place a horizontal line'
        : this.drawTool === 'trend'
          ? 'Click the first point, then the second — line extends across the chart'
          : '';
    this.bumpView();
  }

  cancelDrawing(): void {
    this.drawTool = 'none';
    this.trendFrom = undefined;
    this.drawingStatus = '';
    this.bumpView();
  }

  toggleIndicator(kind: IndicatorKind): void {
    if (this.activeIndicators.has(kind)) {
      this.activeIndicators.delete(kind);
    } else {
      this.activeIndicators.add(kind);
    }
    this.activeIndicators = new Set(this.activeIndicators);
    saveChartIndicatorPrefs(this.activeIndicators);
    this.applyIndicatorLayout();
    this.updateIndicators();
    this.bumpView();
  }

  indicatorActive(kind: IndicatorKind): boolean {
    return this.activeIndicators.has(kind);
  }

  selectDrawing(id: string): void {
    this.selectedDrawingId = this.selectedDrawingId === id ? '' : id;
    this.renderDrawings();
  }

  deleteSelectedDrawing(): void {
    if (!this.selectedDrawingId) {
      return;
    }
    this.drawings = this.drawings.filter((drawing) => drawing.id !== this.selectedDrawingId);
    this.selectedDrawingId = '';
    this.saveDrawings();
    this.renderDrawings();
  }

  goLive(): void {
    this.tvChart?.timeScale().scrollToRealTime();
  }

  tvSymbol(name: string): string {
    const map: Record<string, string> = {
      NIFTY: 'NSE:NIFTY',
      SENSEX: 'BSE:SENSEX',
      BANKNIFTY: 'NSE:BANKNIFTY',
      FINNIFTY: 'NSE:CNXFINANCE',
    };
    return map[name] ?? `NSE:${name}`;
  }

  openTradeTicketFromStock(row: SymbolSnapshot, side: ManualTransactionType): void {
    this.tradeTickets.open(
      contextFromStock(row, side, manualExecutionFromAppMode(this.executionMode)),
    );
    this.bumpView();
  }

  openTradeTicketFromOption(contract: OptionContract, side: ManualTransactionType): void {
    const sameContract =
      this.openedOption?.strike === contract.strike &&
      this.openedOption?.side === contract.side &&
      this.openedOption?.underlying === contract.underlying &&
      this.openedOption?.expiry === contract.expiry;
    const tradingSymbol = sameContract ? this.lastChart?.tradingSymbol : undefined;
    this.tradeTickets.open(
      contextFromOption(
        contract,
        side,
        tradingSymbol,
        manualExecutionFromAppMode(this.executionMode),
      ),
    );
    this.bumpView();
  }

  openTradeTicketForChart(side: ManualTransactionType): void {
    if (!this.selected) {
      return;
    }
    if (this.marketKind === 'options' && this.optionsChartMode === 'contract' && this.openedOption) {
      this.openTradeTicketFromOption(this.openedOption, side);
      return;
    }
    this.openTradeTicketFromStock(this.selected, side);
  }

  openTradeTicketFromPickerStock(
    row: { name: string; last: number | null },
    side: ManualTransactionType,
  ): void {
    const onBoard = this.boardData?.symbols.find((s) => s.name === row.name);
    if (onBoard) {
      this.openTradeTicketFromStock(onBoard, side);
      this.closePicker();
      return;
    }
    this.board
      .ensureSymbol(row.name)
      .pipe(take(1))
      .subscribe({
        next: (snap) => {
          this.openTradeTicketFromStock(snap, side);
          this.closePicker();
        },
      });
  }

  openTradeTicketFromPickerOption(contract: OptionContract, side: ManualTransactionType): void {
    this.openTradeTicketFromOption(contract, side);
    this.closePicker();
  }

  openTradeTicketFromPaperPosition(position: PaperPosition): void {
    const kind = normalizeInstrumentType(position.instrumentType);
    let instrumentType: 'STOCK' | 'OPTION' | 'FUTURE' = 'STOCK';
    if (kind === InstrumentKind.Future) {
      instrumentType = 'FUTURE';
    } else if (kind === InstrumentKind.Option) {
      instrumentType = 'OPTION';
    }
    const underlying = (position.underlying || position.symbol).trim();
    const side: ManualTransactionType = position.direction === 'LONG' ? 'SELL' : 'BUY';
    this.tradeTickets.open({
      instrumentType,
      displayName: position.symbol,
      exchange: tradeTicketExchange(instrumentType, underlying),
      symbol: position.symbol,
      underlying,
      transactionType: side,
      ltp: position.currentPrice,
      previousClose: position.entryPrice,
      executionMode: 'PAPER',
    });
    this.bumpView();
  }

  select(symbol: SymbolSnapshot): void {
    if (this.centerView !== 'trades' && this.centerView !== 'strategies') {
      this.centerView = 'market';
    }
    const instrumentChanged = this.pendingChartName !== symbol.name;
    this.selectedName = symbol.name;
    this.selected = symbol;
    this.pendingChartName = symbol.name;
    if (instrumentChanged) {
      this.prepareChartInstrumentSwitch();
    }
    if (this.tradePanelTab === 'paper' && !this.isIndex(symbol.name)) {
      this.zoneBacktestReload.next();
    }
    this.scheduleChartLoad(true);
    this.bumpView();
  }

  private prepareChartInstrumentSwitch(): void {
    this.loadedChartName = '';
    this.chartBusy = false;
    this.chartPollInFlight = false;
    this.crosshairCandle = undefined;
    const cached = this.chartSnapshotForKey(this.chartRequestKey());
    if (cached?.candles.length) {
      this.chartPaintedFromCache = true;
      this.ngZone.runOutsideAngular(() => {
        this.applyChartData(cached, true);
      });
    } else if (this.lastChart && this.lastChart.name !== this.pendingChartName) {
      this.lastChart = undefined;
    }
  }

  /** Never merge candles from a different symbol (e.g. NIFTY history into a stock chart). */
  private chartCandlesPriorForMerge(symbolName: string): ChartBar[] {
    const key = this.chartRequestKey();
    const cached = this.chartSnapshotForKey(key)?.candles ?? [];
    if (this.lastChart?.name === symbolName && this.lastChart.candles.length) {
      return this.lastChart.candles;
    }
    return cached;
  }

  private chartSnapshotForKey(key: string): ChartResponse | null {
    const memory = this.chartDataCache.get(key);
    if (memory?.candles.length) {
      return memory;
    }
    const persisted = this.chartPersistence.load(key);
    if (persisted?.candles.length) {
      this.chartDataCache.set(key, { ...persisted, candles: [...persisted.candles] });
      return persisted;
    }
    return null;
  }

  private rememberChartCache(key: string, data: ChartResponse): void {
    if (!data.candles.length) {
      return;
    }
    this.chartDataCache.set(key, { ...data, candles: [...data.candles] });
    this.chartPersistence.save(key, data);
    if (this.chartDataCache.size <= this.chartCacheMax) {
      return;
    }
    const oldest = this.chartDataCache.keys().next().value;
    if (oldest) {
      this.chartDataCache.delete(oldest);
    }
  }

  private mergeChartPayload(prior: ChartBar[], incoming: ChartResponse): ChartResponse {
    return {
      ...incoming,
      candles: mergeChartCandlesStable(prior, incoming.candles),
    };
  }

  private shouldRemountChart(prior: ChartBar[], merged: ChartBar[]): boolean {
    if (!merged.length) {
      return false;
    }
    if (!prior.length) {
      return true;
    }
    if (chartCandlesTailOnlyChange(prior, merged)) {
      return false;
    }
    return prior[0].time !== merged[0].time || prior.length !== merged.length;
  }

  selectIndex(name: string): void {
    const row = this.boardData?.symbols.find((s) => s.name === name);
    if (row) {
      this.select(row);
    }
    if (this.marketKind === 'options') {
      this.optionUnderlying = name;
      this.ensureOptionExpiry();
    }
  }

  isIndex(name: string): boolean {
    return this.indexTabs.includes(name);
  }

  tone(row: SymbolSnapshot | null | undefined): 'up' | 'down' | 'flat' {
    return rowTone(row);
  }

  indexRow(name: string): SymbolSnapshot | undefined {
    return this.boardData?.symbols.find((s) => s.name === name);
  }

  selling(): SymbolSnapshot[] {
    return this.railLists.selling;
  }

  buying(): SymbolSnapshot[] {
    return this.railLists.buying;
  }

  ranged(): SymbolSnapshot[] {
    return this.railLists.ranged;
  }

  badgeClass(mark: PressureMark): string {
    if (mark === 'BUYING') {
      return 'badge buy';
    }
    if (mark === 'SELLING') {
      return 'badge sell';
    }
    return 'badge range';
  }

  lastVs(symbol: SymbolSnapshot): string {
    const zone =
      symbol.last >= symbol.b1 ? 'above B1' : symbol.last <= symbol.s1 ? 'below S1' : 'between S1–B1';
    if (this.optionsChartShowsContractPremium()) {
      return `option premium · ${symbol.name} underlying ${symbol.last.toFixed(2)}`;
    }
    return zone;
  }

  private apply(data: BoardResponse): void {
    const boardKey = `${this.boardSymbolsRevision(data.symbols)}|${data.feed}`;
    const boardChanged = boardKey !== this.lastAppliedBoardKey || this.state !== 'ready';
    const recovering = this.state === 'error';

    this.ingestBoard(data);

    if (!data.symbols.length) {
      this.state = 'empty';
      this.selected = null;
      this.lastAppliedBoardKey = boardKey;
      this.bumpView();
      return;
    }

    if (this.state === 'error' || this.state === 'loading' || recovering) {
      this.state = 'ready';
    }

    const preferred = this.marketKind === 'options' ? this.optionUnderlying : this.selectedName;
    const preferredRow =
      this.boardData?.symbols.find((s) => s.name === preferred) ?? this.boardData!.symbols[0];
    const selectionChanged = this.selected?.name !== preferredRow.name;
    if (this.selected?.name === preferredRow.name) {
      Object.assign(this.selected, preferredRow);
    } else {
      this.selected = preferredRow;
      this.selectedName = preferredRow.name;
      this.pendingChartName = preferredRow.name;
    }

    if (boardChanged) {
      this.lastAppliedBoardKey = boardKey;
      this.applyBrokerMessageFromBoardFeed(data);
      this.refreshOptionContractRails();
    }

    if (
      this.centerView === 'market'
      && this.candleSeries
      && !this.chartPollInFlight
      && !this.chartBusy
    ) {
      this.ngZone.runOutsideAngular(() => {
        this.syncLiveChart();
      });
    }

    if (boardChanged || selectionChanged) {
      this.scheduleChartLoad();
    }

    this.bumpView();
  }

  private bumpView(): void {
    this.cdr.markForCheck();
  }

  ngAfterViewInit(): void {
    this.hydrateChartFromStorageEarly();
    this.scheduleChartLoad();
  }

  private hydrateChartFromStorageEarly(): void {
    if (this.centerView !== 'market') {
      return;
    }
    const snap = this.chartSnapshotForKey(this.chartRequestKey());
    if (!snap?.candles.length) {
      return;
    }
    this.chartPaintedFromCache = true;
    this.ngZone.runOutsideAngular(() => {
      this.applyChartData(snap, true);
    });
    this.bumpView();
  }

  private scheduleChartLoad(force = false): void {
    if (this.centerView !== 'market' || this.state !== 'ready') {
      return;
    }
    const key = this.chartRequestKey();
    const hasInstrument =
      (this.marketKind === 'options' &&
        (this.optionsChartMode === 'underlying' || !!this.openedOption)) ||
      !!this.pendingChartName;
    if (!hasInstrument || (!force && (key === this.loadedChartName || this.chartBusy))) {
      return;
    }
    cancelAnimationFrame(this.chartLoadFrame);
    this.chartLoadFrame = requestAnimationFrame(() => {
      this.chartLoadFrame = 0;
      const host = this.chartHost?.nativeElement;
      const instrumentReady =
        (this.marketKind === 'options' &&
          (this.optionsChartMode === 'underlying' || !!this.openedOption)) ||
        !!this.pendingChartName;
      if (!host || !instrumentReady || this.chartRequestKey() !== key) {
        return;
      }
      if (!force && this.chartBusy) {
        return;
      }
      if (host.clientWidth < 16 || host.clientHeight < 16) {
        this.scheduleChartLoad(force);
        return;
      }
      if (!force && key === this.loadedChartName) {
        return;
      }
      this.loadChart(this.pendingChartName, this.chartInterval);
    });
  }

  private ingestBoard(data: BoardResponse): void {
    const rev = this.boardSymbolsRevision(data.symbols);
    if (!this.boardData) {
      this.boardData = {
        counts: data.counts,
        updatedAt: data.updatedAt,
        feed: data.feed,
        symbols: data.symbols.map((symbol) => ({ ...symbol })),
      };
      this.boardSymbolsRev = rev;
      this.refreshRailLists();
      this.marketBreadth.setCounts(data.counts);
      return;
    }

    this.boardData.counts = data.counts;
    this.marketBreadth.setCounts(data.counts);
    this.boardData.updatedAt = data.updatedAt;
    this.boardData.feed = data.feed;
    const existingByName = new Map(this.boardData.symbols.map((symbol) => [symbol.name, symbol]));
    this.boardData.symbols = data.symbols.map((incoming) => {
      const existing = existingByName.get(incoming.name);
      if (existing) {
        Object.assign(existing, incoming);
        return existing;
      }
      return { ...incoming };
    });
    if (rev !== this.boardSymbolsRev) {
      this.boardSymbolsRev = rev;
      this.refreshRailLists();
    }
  }

  private boardSymbolsRevision(symbols: SymbolSnapshot[]): string {
    return symbols
      .map(
        (symbol) =>
          `${symbol.name}:${symbol.mark}:${symbol.last}:${symbol.b2}:${symbol.b1}:${symbol.s}:${symbol.s1}:${symbol.s2}:${symbol.tightRange}:${symbol.quoteVerified ?? ''}:${symbol.quoteQuality ?? ''}:${symbol.quoteTimestamp ?? ''}`,
      )
      .join('|');
  }

  private refreshRailLists(): void {
    const symbols = this.boardData?.symbols ?? [];
    this.railLists.selling = symbols
      .filter(
        (symbol) =>
          symbol.mark === 'SELLING' && symbolHasVerifiedQuote(symbol) && !this.isIndex(symbol.name),
      )
      .sort((a, b) => a.last - a.s1 - (b.last - b.s1));
    this.railLists.buying = symbols
      .filter(
        (symbol) =>
          symbol.mark === 'BUYING' && symbolHasVerifiedQuote(symbol) && !this.isIndex(symbol.name),
      )
      .sort((a, b) => b.last - b.b1 - (a.last - a.b1));
    this.railLists.ranged = symbols.filter(
      (symbol) =>
        symbol.mark === 'RANGE BOUND' && symbolHasVerifiedQuote(symbol) && !this.isIndex(symbol.name),
    );
  }

  symbolLastDisplay(row: SymbolSnapshot | null | undefined): number | null {
    return boardSymbolDisplayLast(row);
  }

  symbolQuoteQualityLabel(row: SymbolSnapshot | null | undefined): string {
    return quoteQualityLabel(row?.quoteQuality);
  }

  symbolQuoteQualityClass(row: SymbolSnapshot | null | undefined): string {
    return quoteQualityCssClass(row?.quoteQuality);
  }

  selectedQuoteQualityLabel(): string {
    return quoteQualityLabel(this.selected?.quoteQuality);
  }

  selectedQuoteQualityClass(): string {
    return quoteQualityCssClass(this.selected?.quoteQuality);
  }

  selectedQuoteQualityLive(): boolean {
    return quoteQualityShowCheck(this.selected?.quoteQuality);
  }

  symbolQuoteFreshnessLabel(row: SymbolSnapshot | null | undefined): string {
    return symbolQuoteFreshness(row);
  }

  selectedQuoteFreshnessLabel(): string {
    return symbolQuoteFreshness(this.selected);
  }

  private pickerRowLast(symbol: SymbolSnapshot): number | null {
    return boardSymbolDisplayLast(symbol);
  }

  private shouldReplacePaperAgent(snapshot: PaperAgentSnapshot): boolean {
    const current = this.paperAgent;
    if (!current) {
      return true;
    }
    if (snapshot.updatedAt !== current.updatedAt) {
      return true;
    }
    if (snapshot.state !== current.state || snapshot.running !== current.running) {
      return true;
    }
    if (snapshot.totalPnl !== current.totalPnl || snapshot.positions.length !== current.positions.length) {
      return true;
    }
    return (
      (snapshot.recentDecisions?.length ?? 0) !== (current.recentDecisions?.length ?? 0) ||
      (snapshot.messages?.length ?? 0) !== (current.messages?.length ?? 0)
    );
  }

  private applyBrokerMessageFromBoardFeed(data: BoardResponse): void {
    let next = this.brokerMessage;
    if (data.feed.includes('session expired')) {
      this.brokerConnected = false;
      this.brokerAvailableFunds = null;
      this.brokerUtilizedFunds = null;
      if (preserveBrokerOtpEntry(data.feed, this.otpRequested, this.brokerBusy)) {
        next = 'OTP sent. Enter the code below and click Submit OTP.';
      } else if (!this.otpRequested && !this.brokerBusy) {
        next = 'Session expired. Click Request OTP, then submit the new code.';
      }
    } else if (data.feed.includes('quotes empty')) {
      next = 'Connected but quotes empty. Wait for market hours or reconnect with Request OTP.';
    } else if (this.brokerConnected && !this.otpRequested && !this.brokerBusy) {
      next = 'Connected. Live market prices active.';
      this.prefetchIndexChartsInBackground();
    } else if (data.feed.includes('awaiting broker login') && !this.otpRequested && !this.brokerBusy) {
      this.onBrokerProviderChange();
      next = this.brokerMessage;
    }
    if (next !== this.brokerMessage) {
      this.brokerMessage = next;
    }
  }

  private loadChart(name: string, interval: string): void {
    const host = this.chartHost?.nativeElement;
    if (!host) {
      return;
    }
    const requestKey = this.chartRequestKey();
    const cachedSnapshot = this.chartSnapshotForKey(requestKey);
    if (cachedSnapshot?.candles.length && !this.lastChart?.candles?.length) {
      this.ngZone.runOutsideAngular(() => {
        this.applyChartData(cachedSnapshot, true);
      });
      this.bumpView();
    }
    this.chartBusy = true;
    this.chartFullHistoryPending = true;
    this.loadedChartName = requestKey;
    this.optionChartError = '';
    if (cachedSnapshot?.candles.length || this.lastChart?.candles?.length) {
      this.chartPaintedFromCache = true;
    }

    const priorBars = this.chartCandlesPriorForMerge(name);
    const needsFastSession =
      this.marketKind !== 'options'
      && interval !== 'day'
      && sessionStartBars(priorBars).length <= 1;

    if (needsFastSession) {
      this.chartRequest(name, interval, 'fast')
        .pipe(catchError(() => EMPTY))
        .subscribe((data) => {
          if (requestKey !== this.chartRequestKey()) {
            return;
          }
          const row = this.selected?.name === name ? this.selected : this.indexRow(name);
          const payload =
            data.candles.length > 0 || !row ? data : this.fallbackChart(name, row);
          const mergePrior = this.chartCandlesPriorForMerge(name);
          const mergedPayload = this.mergeChartPayload(mergePrior, payload);
          const remount =
            this.lastChart?.name !== name || this.shouldRemountChart(mergePrior, mergedPayload.candles);
          this.ngZone.runOutsideAngular(() => {
            this.applyChartData(mergedPayload, remount);
          });
          this.rememberChartCache(requestKey, mergedPayload);
          this.bumpView();
        });
    }

    void this.chartLive.syncWatch(name, this.chartInterval).catch(() => {
      this.chartStreamReceiving = false;
      this.bumpView();
    });

    this.chartRequest(name, interval, 'full').subscribe({
      next: (data) => {
        if (requestKey === this.chartRequestKey()) {
          const row = this.selected?.name === name ? this.selected : this.indexRow(name);
          const allowIndexFallback = this.marketKind !== 'options' && !!row;
          const payload =
            data.candles.length > 0 || !allowIndexFallback ? data : this.fallbackChart(name, row!);
          const mergePrior = this.chartCandlesPriorForMerge(name);
          const mergedPayload = this.mergeChartPayload(mergePrior, payload);
          const remount =
            this.lastChart?.name !== name || this.shouldRemountChart(mergePrior, mergedPayload.candles);
          this.ngZone.runOutsideAngular(() => {
            this.applyChartData(mergedPayload, remount);
          });
          this.rememberChartCache(requestKey, mergedPayload);
        }
        this.chartBusy = false;
        this.chartRefreshPending = false;
        this.chartFullHistoryPending = false;
        this.chartPaintedFromCache = false;
        this.bumpView();
      },
      error: () => {
        this.chartBusy = false;
        this.chartRefreshPending = false;
        this.chartFullHistoryPending = false;
        const row = this.selected?.name === name ? this.selected : this.indexRow(name);
        if (requestKey === this.chartRequestKey() && row && this.marketKind !== 'options') {
          this.ngZone.runOutsideAngular(() => {
            this.applyChartData(this.fallbackChart(name, row), true);
          });
        } else {
          this.loadedChartName = '';
        }
        if (this.marketKind === 'options' && this.openedOption) {
          this.optionChartError = `No live ${this.brokerDisplayName} quote for ${this.openedOption.label}. Try another expiry or strike, or reconnect the broker.`;
        }
        this.scheduleChartLoad();
        this.bumpView();
      },
    });
  }

  private prefetchIndexChartsInBackground(): void {
    if (!this.brokerConnected || this.indexChartPrefetchStarted || this.marketKind === 'options') {
      return;
    }
    this.indexChartPrefetchStarted = true;
    const activeKey = this.chartRequestKey();
    for (const indexName of this.indexTabs) {
      const key = `${indexName}|${this.chartInterval}`;
      if (key === activeKey || this.chartDataCache.has(key) || this.chartPersistence.load(key)) {
        continue;
      }
      this.board
        .chart(indexName, this.chartInterval, 'full')
        .pipe(catchError(() => EMPTY), take(1))
        .subscribe((data) => {
          if (data.candles.length) {
            this.rememberChartCache(key, data);
          }
        });
    }
  }

  private applyChartLiveUpdate(update: ChartLiveUpdate): void {
    if (!isIstCashSessionOpen(Math.floor(Date.now() / 1000))) {
      return;
    }
    if (
      this.optionsChartShowsContractPremium()
      || !this.candleSeries
      || !this.lastChart?.candles.length
    ) {
      return;
    }
    const name = this.pendingChartName;
    if (!update.name || !name || update.name.toUpperCase() !== name.toUpperCase()) {
      return;
    }
    if (update.interval !== this.chartInterval) {
      return;
    }

    const bar = clampIntradayBarWicks(
      {
        time: update.bar.time,
        open: Number(update.bar.open),
        high: Number(update.bar.high),
        low: Number(update.bar.low),
        close: Number(update.bar.close),
      },
      this.chartInterval,
    );
    const candles = this.lastChart.candles;
    const tail = candles[candles.length - 1];
    if (tail.time === bar.time) {
      candles[candles.length - 1] = bar;
      this.candleSeries.update(this.toTvCandles([bar])[0]);
    } else if (bar.time > tail.time) {
      candles.push(bar);
      this.candleSeries.update(this.toTvCandles([bar])[0]);
    }

    if (this.selected?.name === update.name) {
      this.selected.last = update.last;
      this.selected.b2 = update.b2;
      this.selected.b1 = update.b1;
      this.selected.s = update.s;
      this.selected.s1 = update.s1;
      this.selected.s2 = update.s2;
    }
    this.lastChart = {
      ...this.lastChart,
      last: update.last,
      b2: update.b2,
      b1: update.b1,
      s: update.s,
      s1: update.s1,
      s2: update.s2,
      candles: [...candles],
    };
    this.updateZonePriceLines(this.chartZoneLevels(this.lastChart));
    this.updateIndicators();
  }

  private chartRequest(
    name: string,
    interval: string,
    depth: 'fast' | 'full' = 'full',
  ): Observable<ChartResponse> {
    if (this.marketKind === 'options' && this.optionsChartMode === 'underlying') {
      return this.board.chart(this.optionUnderlying, interval, depth);
    }
    if (this.marketKind === 'options' && this.openedOption) {
      return this.board.optionChart(this.openedOption, interval);
    }
    return this.board.chart(name, interval, depth);
  }

  private chartRequestKey(): string {
    let instrument = this.pendingChartName;
    if (this.marketKind === 'options' && this.optionsChartMode === 'underlying') {
      instrument = `${this.optionUnderlying}|spot`;
    } else if (this.marketKind === 'options' && this.openedOption) {
      instrument = `${this.openedOption.underlying}|${this.openedOption.expiry}|${this.openedOption.strike}|${this.openedOption.side}`;
    }
    return `${instrument}|${this.chartInterval}`;
  }

  displayLast(): number | null {
    if (this.optionsChartShowsContractPremium()) {
      const last = this.openedOption?.last ?? this.lastChart?.last ?? 0;
      return last > 0 ? last : null;
    }
    const fromBoard = boardSymbolDisplayLast(this.selected);
    if (fromBoard != null) {
      const chartClose = this.lastChart?.candles.at(-1)?.close ?? 0;
      const sessionOpen = isIstCashSessionOpen(Math.floor(Date.now() / 1000));
      if (
        this.selected?.quoteQuality === 'VerifiedLive'
        || this.selected?.quoteQuality === 'VerifiedClose'
      ) {
        return fromBoard;
      }
      return boardDisplayLast(fromBoard, chartClose, this.isIndex(this.selectedName), sessionOpen);
    }
    return null;
  }

  displayOpen(): number {
    if (this.optionsChartShowsContractPremium()) {
      return (
        this.lastChart?.todayOpen ??
        this.openedOption?.previousClose ??
        this.lastChart?.candles.at(-1)?.open ??
        0
      );
    }
    return this.selected?.todayOpen ?? this.lastChart?.todayOpen ?? 0;
  }

  private applyChartData(data: ChartResponse, remount: boolean): void {
    if (this.lastChart && this.lastChart.name !== data.name) {
      remount = true;
    }
    const prev =
      !remount && this.lastChart?.name === data.name ? (this.lastChart?.candles ?? []) : [];
    const normalized = sanitizeChartCandles(data.candles, this.chartInterval);
    const candles = remount
      ? normalized
      : sanitizeChartCandles(mergeChartCandlesStable(prev, data.candles), this.chartInterval);
    const payload: ChartResponse = { ...data, candles };

    if (remount || !this.candleSeries || !candles.length) {
      if (!candles.length) {
        return;
      }
      this.lastChart = { ...payload, candles: [...candles] };
      this.mountChart(payload);
      return;
    }

    if (!prev.length) {
      this.lastChart = { ...payload, candles: [...candles] };
      this.mountChart(payload);
      return;
    }

    if (chartCandlesTailOnlyChange(prev, candles)) {
      const from = firstChangedCandle(prev, candles);
      const tvCandles = this.toTvCandles(candles);
      for (let i = from; i < tvCandles.length; i++) {
        this.candleSeries!.update(tvCandles[i]);
      }
      this.lastChart = { ...payload, candles: [...candles] };
      this.updateZonePriceLines(this.chartZoneLevels(payload));
      this.updateIndicators();
      this.syncLiveChart();
      return;
    }

    this.replaceChartSeries(payload);
    this.syncLiveChart();
  }

  private syncLiveChart(): void {
    if (!isIstCashSessionOpen(Math.floor(Date.now() / 1000))) {
      return;
    }
    if (
      this.optionsChartShowsContractPremium() ||
      !this.candleSeries ||
      !this.selected ||
      !symbolHasVerifiedQuote(this.selected) ||
      !this.lastChart?.candles.length
    ) {
      return;
    }

    const live = this.selected.last;
    if (!live || live <= 0) {
      return;
    }

    const synced = syncLiveCandle(this.lastChart.candles, live, this.chartInterval);
    if (!synced) {
      return;
    }

    this.candleSeries.update(synced.bar);
    if (synced.isNew) {
      this.lastChart.candles.push(synced.bar);
    } else {
      this.lastChart.candles[this.lastChart.candles.length - 1] = synced.bar;
    }
    this.lastChart = {
      ...this.lastChart,
      last: live,
      b2: this.selected.b2,
      b1: this.selected.b1,
      s: this.selected.s,
      s1: this.selected.s1,
      s2: this.selected.s2,
    };
    this.updateZonePriceLines(this.lastChart);
    this.updateIndicators();
  }

  /** Board row is the source of truth for B1/B2/S/S1/S2 (same formula as chips). */
  private chartZoneLevels(data: ChartResponse): Pick<ChartResponse, 'b2' | 'b1' | 's' | 's1' | 's2'> {
    const row = this.selected?.name === data.name ? this.selected : this.indexRow(data.name);
    const fromRow =
      row && row.b1 > 0 && row.b2 > 0 && row.s1 > 0 && row.s2 > 0
        ? { b2: row.b2, b1: row.b1, s: row.s, s1: row.s1, s2: row.s2 }
        : null;
    const fromChart =
      data.b1 > 0 && data.b2 > 0 && data.s1 > 0 && data.s2 > 0
        ? { b2: data.b2, b1: data.b1, s: data.s, s1: data.s1, s2: data.s2 }
        : null;
    return fromRow ?? fromChart ?? { b2: data.b2, b1: data.b1, s: data.s, s1: data.s1, s2: data.s2 };
  }

  private updateZonePriceLines(zones: Pick<ChartResponse, 'b2' | 'b1' | 's' | 's1' | 's2'>): void {
    if (this.zonePriceLines.length !== 5) {
      return;
    }
    const levels = [zones.b2, zones.b1, zones.s, zones.s1, zones.s2];
    for (let i = 0; i < this.zonePriceLines.length; i++) {
      this.zonePriceLines[i].applyOptions({ price: levels[i] });
    }
  }

  private toTvCandles(candles: ChartResponse['candles']) {
    return normalizeChartCandles(candles).map((c) => ({
      time: c.time as number,
      open: Number(c.open),
      high: Number(c.high),
      low: Number(c.low),
      close: Number(c.close),
    }));
  }

  private replaceChartSeries(data: ChartResponse): void {
    if (!this.candleSeries) {
      this.mountChart(data);
      return;
    }
    const candles = this.toTvCandles(data.candles);
    this.candleSeries.setData(candles);
    this.lastChart = { ...data, candles: [...data.candles] };
    this.updateZonePriceLines(this.chartZoneLevels(data));
    this.updateSessionMarkers(data);
    this.updateIndicators();
  }

  private fallbackChart(name: string, row: SymbolSnapshot): ChartResponse {
    const stepSec = this.chartInterval === 'minute' ? 60 : this.chartInterval === '15minute' ? 900 : this.chartInterval === '60minute' ? 3600 : this.chartInterval === 'day' ? 86400 : 300;
    const bars = 60;
    const t0 = Math.floor(Date.now() / 1000) - stepSec * bars;
    const tick = Math.max(0.05, row.last * 0.00035);
    let price = row.todayOpen > 0 ? row.todayOpen : row.last;
    const candles = [];
    for (let i = 0; i < bars; i++) {
      const move = (Math.random() * 2 - 1) * tick;
      const open = price;
      const close = Math.min(Math.max(open + move, row.s2 * 0.995), row.b2 * 1.005);
      const wick = tick * 0.5;
      candles.push({
        time: t0 + i * stepSec,
        open,
        high: Math.max(open, close) + wick,
        low: Math.min(open, close) - wick,
        close,
      });
      price = close;
    }
    const last = candles.at(-1);
    if (last) {
      last.close = row.last;
      last.high = Math.max(last.high, row.last);
      last.low = Math.min(last.low, row.last);
    }
    return { name, last: row.last, b2: row.b2, b1: row.b1, s: row.s, s1: row.s1, s2: row.s2, candles };
  }

  private mountChart(data: ChartResponse): void {
    const host = this.chartHost?.nativeElement;
    const tv = (window as unknown as { LightweightCharts?: { createChart: (el: HTMLElement, opts: Record<string, unknown>) => TvChart } }).LightweightCharts;
    if (!host || !tv) {
      return;
    }
    this.tvChart?.remove();
    this.chartResize?.disconnect();
    this.lastIndicatorCandleKey = '';
    this.sessionMarkerKey = '';
    this.trendFrom = undefined;
    this.zonePriceLines = [];
    this.indicatorSeries.clear();
    this.bollingerSeries = undefined;
    this.rsiSeries = undefined;
    this.drawingPriceLines.clear();
    this.drawingTrendSeries.clear();
    this.crosshairCandle = undefined;
    const width = Math.max(host.clientWidth, 320);
    const height = Math.max(host.clientHeight, 360);
    const chartSurface = resolveChartSurfaceFromPalette(resolveThemePalette(this.themeSelection));
    this.tvChart = tv.createChart(host, {
      width,
      height,
      ...lightweightChartDisplayOptions(chartSurface),
      rightPriceScale: {
        borderColor: chartSurface.chartGrid,
        scaleMargins: { top: 0.08, bottom: 0.08 },
      },
      timeScale: {
        borderColor: chartSurface.chartGrid,
        visible: true,
        timeVisible: true,
        secondsVisible: this.chartInterval === 'minute',
        rightOffset: 6,
        barSpacing: 9,
        minBarSpacing: 4,
        fixLeftEdge: false,
        fixRightEdge: false,
        tickMarkFormatter: (time: number, tickMarkType: number) =>
          this.chartInterval === 'day' || tickMarkType <= 2
            ? istDate(time)
            : istDateTime(time, false),
      },
      handleScroll: { mouseWheel: true, pressedMouseMove: true, horzTouchDrag: true, vertTouchDrag: false },
      handleScale: {
        axisPressedMouseMove: { time: true, price: true },
        mouseWheel: true,
        pinch: true,
      },
      localization: {
        locale: 'en-IN',
        timeFormatter: (time: number) => istDateTime(time, true),
      },
    });
    this.bindChartResize(host);
    this.candleSeries = this.tvChart.addCandlestickSeries({
      ...CHART_CANDLE_STYLE,
      lastValueVisible: true,
      priceLineVisible: true,
      priceLineWidth: 1,
      priceLineColor: CHART_CANDLE_STYLE.downColor,
    });
    const candles = this.toTvCandles(data.candles);
    this.candleSeries.setData(candles);
    const zones = this.chartZoneLevels(data);
    const lines = this.optionsChartShowsContractPremium() ? [] : [
      { price: Number(zones.b2), color: '#238636', title: 'B2' },
      { price: Number(zones.b1), color: '#3fb950', title: 'B1' },
      { price: Number(zones.s), color: '#79b8ff', title: 'S' },
      { price: Number(zones.s1), color: '#f85149', title: 'S1' },
      { price: Number(zones.s2), color: '#da3633', title: 'S2' },
    ];
    this.zonePriceLines = [];
    for (const line of lines) {
      this.zonePriceLines.push(
        this.candleSeries.createPriceLine({
          price: line.price,
          color: line.color,
          lineWidth: 2,
          lineStyle: 2,
          axisLabelVisible: true,
          title: line.title,
        }),
      );
    }
    this.createIndicatorSeries();
    this.updateIndicators(data.candles);
    this.updateSessionMarkers(data);
    this.loadDrawings();
    this.renderDrawings();
    this.tvChart.timeScale().fitContent();
    this.tvChart.subscribeClick((param) => this.onChartClick(param));
    this.tvChart.subscribeCrosshairMove((param) => this.onCrosshairMove(param));
    this.ngZone.runOutsideAngular(() => {
      this.syncLiveChart();
    });
  }

  private createIndicatorSeries(): void {
    if (!this.tvChart) {
      return;
    }
    const overlay: Partial<Record<IndicatorKind, { color: string; title: string }>> = {
      sma20: { color: '#2962ff', title: 'SMA 20' },
      sma50: { color: '#ab47bc', title: 'SMA 50' },
      ema9: { color: '#ff9800', title: 'EMA 9' },
      ema21: { color: '#00bcd4', title: 'EMA 21' },
    };
    for (const kind of Object.keys(overlay) as IndicatorKind[]) {
      const meta = overlay[kind];
      if (!meta) {
        continue;
      }
      this.indicatorSeries.set(
        kind,
        this.tvChart.addLineSeries({
          color: meta.color,
          lineWidth: 2,
          title: meta.title,
          priceLineVisible: false,
          lastValueVisible: true,
          crosshairMarkerVisible: false,
        }),
      );
    }
    this.bollingerSeries = {
      upper: this.tvChart.addLineSeries({
        color: '#7c3aed',
        lineWidth: 1,
        lineStyle: 2,
        title: 'BB upper',
        priceLineVisible: false,
        lastValueVisible: false,
        crosshairMarkerVisible: false,
      }),
      middle: this.tvChart.addLineSeries({
        color: '#8b5cf6',
        lineWidth: 1,
        title: 'BB 20',
        priceLineVisible: false,
        lastValueVisible: false,
        crosshairMarkerVisible: false,
      }),
      lower: this.tvChart.addLineSeries({
        color: '#7c3aed',
        lineWidth: 1,
        lineStyle: 2,
        title: 'BB lower',
        priceLineVisible: false,
        lastValueVisible: false,
        crosshairMarkerVisible: false,
      }),
    };
    this.rsiSeries = this.tvChart.addLineSeries({
      color: '#e11d48',
      lineWidth: 2,
      title: 'RSI 14',
      priceScaleId: 'rsi',
      priceLineVisible: false,
      lastValueVisible: true,
      crosshairMarkerVisible: false,
    });
    this.tvChart.priceScale('rsi').applyOptions({
      scaleMargins: { top: 0.78, bottom: 0.02 },
      borderVisible: false,
    });
    this.applyIndicatorLayout();
  }

  private applyIndicatorLayout(): void {
    if (!this.tvChart) {
      return;
    }
    const rsiOn = this.activeIndicators.has('rsi14');
    this.tvChart.applyOptions?.({
      rightPriceScale: {
        scaleMargins: { top: 0.08, bottom: rsiOn ? 0.26 : 0.08 },
      },
    });
    this.tvChart.priceScale('rsi').applyOptions({
      visible: rsiOn,
      scaleMargins: { top: 0.78, bottom: 0.02 },
    });
  }

  private updateIndicators(candles = this.lastChart?.candles ?? []): void {
    const last = candles.at(-1);
    const candleKey = last ? `${candles.length}:${last.close}:${last.high}:${last.low}` : '';
    const activeKey = [...this.activeIndicators].sort().join(',');
    const revision = `${activeKey}|${candleKey}`;
    if (revision === this.lastIndicatorCandleKey) {
      return;
    }
    this.lastIndicatorCandleKey = revision;

    const definitions: Partial<Record<IndicatorKind, { period: number; kind: 'sma' | 'ema' }>> = {
      sma20: { period: 20, kind: 'sma' },
      sma50: { period: 50, kind: 'sma' },
      ema9: { period: 9, kind: 'ema' },
      ema21: { period: 21, kind: 'ema' },
    };
    for (const [name, series] of this.indicatorSeries) {
      const definition = definitions[name];
      if (!definition) {
        continue;
      }
      series.setData(
        this.activeIndicators.has(name)
          ? movingAverage(candles, definition.period, definition.kind)
          : [],
      );
    }
    if (this.bollingerSeries) {
      const bbOn = this.activeIndicators.has('bb20');
      if (bbOn && candles.length) {
        const bands = bollingerBands(candles, 20, 2);
        this.bollingerSeries.upper.setData(bands.map((b) => ({ time: b.time, value: b.upper })));
        this.bollingerSeries.middle.setData(bands.map((b) => ({ time: b.time, value: b.middle })));
        this.bollingerSeries.lower.setData(bands.map((b) => ({ time: b.time, value: b.lower })));
      } else {
        this.bollingerSeries.upper.setData([]);
        this.bollingerSeries.middle.setData([]);
        this.bollingerSeries.lower.setData([]);
      }
    }
    if (this.rsiSeries) {
      this.rsiSeries.setData(
        this.activeIndicators.has('rsi14') ? relativeStrengthIndex(candles, 14) : [],
      );
    }
  }

  private updateSessionMarkers(data: ChartResponse): void {
    if (!this.candleSeries) {
      return;
    }
    const bars = sessionStartBars(data.candles);
    const key = bars.map((bar) => bar.time).join(',');
    if (key === this.sessionMarkerKey) {
      return;
    }
    this.sessionMarkerKey = key;
    this.candleSeries.setMarkers(
      bars.map((bar) => ({
        time: bar.time,
        position: 'belowBar',
        color: '#787b86',
        shape: 'circle',
        text: `${istDate(bar.time)} · 09:15`,
        size: 0.7,
      })),
    );
  }

  private onCrosshairMove(param: TvCrosshairParam): void {
    if (param.time == null || !this.lastChart) {
      this.crosshairCandle = undefined;
      return;
    }
    const seriesValue = this.candleSeries ? param.seriesData?.get(this.candleSeries) : undefined;
    const fallback = this.lastChart.candles.find((candle) => candle.time === param.time);
    const value = seriesValue?.open != null ? seriesValue : fallback;
    if (!value || value.open == null || value.high == null || value.low == null || value.close == null) {
      this.crosshairCandle = undefined;
      return;
    }
    this.crosshairCandle = {
      time: istDateTime(param.time, true),
      open: value.open,
      high: value.high,
      low: value.low,
      close: value.close,
    };
    this.bumpView();
  }

  private loadDrawings(): void {
    if (!this.pendingChartName) {
      this.drawings = [];
      return;
    }
    this.drawings = parseDrawings(
      localStorage.getItem(drawingStorageKey(this.drawingInstrumentKey(), this.chartInterval)),
    );
    this.selectedDrawingId = '';
  }

  private saveDrawings(): void {
    if (!this.pendingChartName) {
      return;
    }
    localStorage.setItem(
      drawingStorageKey(this.drawingInstrumentKey(), this.chartInterval),
      JSON.stringify(this.drawings),
    );
  }

  private drawingInstrumentKey(): string {
    if (this.marketKind === 'options' && this.optionsChartMode === 'underlying') {
      return `${this.optionUnderlying}-spot`;
    }
    return this.openedOption
      ? `${this.openedOption.underlying}-${this.openedOption.expiry}-${this.openedOption.strike}-${this.openedOption.side}`
      : this.pendingChartName;
  }

  private renderDrawings(): void {
    if (!this.tvChart || !this.candleSeries) {
      return;
    }
    for (const line of this.drawingPriceLines.values()) {
      this.candleSeries.removePriceLine(line);
    }
    for (const series of this.drawingTrendSeries.values()) {
      this.tvChart.removeSeries(series);
    }
    this.drawingPriceLines.clear();
    this.drawingTrendSeries.clear();

    for (const drawing of this.drawings) {
      if (drawing.kind === 'hline' && drawing.price != null) {
        this.drawingPriceLines.set(
          drawing.id,
          this.candleSeries.createPriceLine({
            price: drawing.price,
            color: drawing.color,
            lineWidth: this.selectedDrawingId === drawing.id ? 2 : 1,
            lineStyle: 0,
            axisLabelVisible: true,
            title: 'H',
          }),
        );
      }
      if (drawing.kind === 'trend' && drawing.from && drawing.to && this.lastChart?.candles.length) {
        const candles = normalizeChartCandles(this.lastChart.candles);
        const timeMin = candles[0].time;
        const timeMax = candles.at(-1)!.time;
        const series = this.tvChart.addLineSeries({
          color: drawing.color,
          lineWidth: this.selectedDrawingId === drawing.id ? 3 : 2,
          priceLineVisible: false,
          lastValueVisible: false,
          crosshairMarkerVisible: false,
        });
        series.setData(
          extendTrendSegment(drawing.from, drawing.to, timeMin, timeMax),
        );
        this.drawingTrendSeries.set(drawing.id, series);
      }
    }
  }

  private newDrawingId(): string {
    return `drawing-${Date.now()}-${Math.random().toString(16).slice(2)}`;
  }

  private bindChartResize(host: HTMLElement): void {
    this.chartResize = new ResizeObserver(() => {
      if (!this.tvChart) {
        return;
      }
      const width = host.clientWidth;
      const height = host.clientHeight;
      if (width > 16 && height > 16) {
        this.tvChart.resize(width, height);
      }
    });
    this.chartResize.observe(host);
  }

  private onChartClick(param: { point?: { x: number; y: number }; time?: number }): void {
    if (this.drawTool === 'none' || !this.candleSeries || !param.point) {
      return;
    }
    const price = this.candleSeries.coordinateToPrice(param.point.y);
    if (price == null) {
      return;
    }
    if (this.drawTool === 'hline') {
      this.drawings = [
        ...this.drawings,
        { id: this.newDrawingId(), kind: 'hline', color: '#f9a825', price },
      ];
      this.saveDrawings();
      this.renderDrawings();
      this.cancelDrawing();
      this.bumpView();
      return;
    }
    if (this.drawTool === 'trend' && param.time != null && this.tvChart) {
      if (!this.trendFrom) {
        this.trendFrom = { time: param.time, value: price };
        this.drawingStatus = 'First point set — click the second trend point';
        this.bumpView();
        return;
      }
      const a = this.trendFrom.time <= param.time ? this.trendFrom : { time: param.time, value: price };
      const b = this.trendFrom.time <= param.time ? { time: param.time, value: price } : this.trendFrom;
      this.drawings = [
        ...this.drawings,
        {
          id: this.newDrawingId(),
          kind: 'trend',
          color: '#f9a825',
          from: a,
          to: b,
        },
      ];
      this.saveDrawings();
      this.renderDrawings();
      this.cancelDrawing();
      this.bumpView();
    }
  }

  chartToolsHint(): string {
    const parts: string[] = [];
    if (this.activeIndicators.size) {
      parts.push(`${this.activeIndicators.size} indicator(s) on`);
    }
    if (this.drawings.length) {
      parts.push(`${this.drawings.length} drawing(s)`);
    }
    return parts.join(' · ');
  }
}
