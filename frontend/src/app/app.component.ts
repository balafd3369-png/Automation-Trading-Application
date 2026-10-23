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
import { NavigationEnd, Router, RouterOutlet } from '@angular/router';
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
  LiveExecutionSnapshot,
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
import { BoardService } from './core/board.service';
import { ChartPersistenceService } from './core/chart-persistence.service';
import { ChartStreamService } from './core/chart-stream.service';
import { LiveClockComponent } from './live-clock.component';
import { PaperStrategyFocus, PaperStrategyFocusService } from './core/paper-strategy-focus.service';
import { StrategiesPageComponent } from './strategies/strategies-page.component';
import { StrategiesPageStore } from './strategies/strategies-page.store';
import { StrategyTradesPanelComponent } from './strategy-trades-panel/strategy-trades-panel.component';
import { SuperIntelligencePageComponent } from './super-intelligence/super-intelligence-page.component';
import {
  ChartDrawing,
  IndicatorKind,
  drawingStorageKey,
  chartCandlesTailOnlyChange,
  firstChangedCandle,
  istDate,
  mergeChartCandlesStable,
  normalizeChartCandles,
  istDateTime,
  movingAverage,
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
  formatPaperHistoryStrategyLabel,
  paperTradeHistoryInIstDateRange,
  istTodayCalendarYmd,
  brokerLoginRejectedMessage,
  brokerOtpSendSucceeded,
  preserveBrokerOtpEntry,
  symbolHasVerifiedQuote,
} from './core/trading-ui.util';
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
  resolveChartSurface,
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
type CenterView = 'market' | 'trades' | 'strategies' | 'intelligence';
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
    SuperIntelligencePageComponent,
    StrategyTradesPanelComponent,
    RouterOutlet,
  ],
  templateUrl: './app.component.html',
  styleUrl: './app.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AppComponent implements OnInit, AfterViewInit {
  private readonly board = inject(BoardService);
  private readonly chartPersistence = inject(ChartPersistenceService);
  private readonly chartStream = inject(ChartStreamService);
  private readonly strategiesStore = inject(StrategiesPageStore);
  private readonly paperStrategyFocus = inject(PaperStrategyFocusService);
  private readonly http = inject(HttpClient);
  private readonly sanitizer = inject(DomSanitizer);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly ngZone = inject(NgZone);
  private readonly reload = new Subject<void>();
  private readonly optionChainReload = new Subject<void>();
  private readonly historyReload = new Subject<void>();
  private readonly zoneBacktestReload = new Subject<void>();
  @ViewChild('chartHost') chartHost?: ElementRef<HTMLDivElement>;
  @ViewChild('brokerHub') brokerHubRef?: ElementRef<HTMLDetailsElement>;
  @ViewChild('symbolSearchInput') symbolSearchInput?: ElementRef<HTMLInputElement>;
  @ViewChild('symbolSearchRoot') symbolSearchRoot?: ElementRef<HTMLElement>;
  private loadedChartName = '';
  private pendingChartName = '';
  chartBusy = false;
  private tvChart?: TvChart;
  private candleSeries?: TvCandle;
  private zonePriceLines: TvPriceLine[] = [];
  lastChart?: ChartResponse;
  private readonly chartRefreshSec = 5;
  private readonly chartStreamFullRefreshSec = 90;
  chartStreamConnected = false;
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
  private readonly drawingPriceLines = new Map<string, TvPriceLine>();
  private readonly drawingTrendSeries = new Map<string, TvLine>();
  drawTool: DrawTool = 'none';
  drawingStatus = '';
  drawings: ChartDrawing[] = [];
  selectedDrawingId = '';
  activeIndicators = new Set<IndicatorKind>(['sma20', 'ema9']);
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
  pickerOpen = false;
  pickerKind: MarketKind = 'stocks';
  pickerQuery = '';
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
  liveOrderFilter: LiveOrderFilter = 'executed';
  readonly themePresets = APP_THEME_PRESETS;
  themeSelection: AppThemeSelection = { presetId: 'default' };

  ngOnInit(): void {
    this.strategiesStore.ensurePolling();
    this.themeSelection = loadThemeSelection();
    this.applyAppTheme();
    this.paperStrategyFocus.focus$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((focus) => {
      if (focus) {
        this.applyPaperStrategyFocus(focus);
      } else {
        this.paperStrategyFilter = null;
      }
      this.bumpView();
    });
    const bootFocus = this.paperStrategyFocus.snapshot();
    if (bootFocus) {
      this.applyPaperStrategyFocus(bootFocus);
    }
    this.pendingChartName = this.selectedName;
    this.chartStream.live$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((update) => {
      this.chartStreamConnected = true;
      this.ngZone.runOutsideAngular(() => {
        this.applyChartLiveUpdate(update);
      });
      this.bumpView();
    });
    this.destroyRef.onDestroy(() => {
      void this.chartStream.disconnect();
    });
    this.router.events
      .pipe(
        filter((event): event is NavigationEnd => event instanceof NavigationEnd),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((event) => {
        this.applyNavigationState(parseRouterUrl(event.urlAfterRedirects));
        this.saveNavigationToStorage();
        this.bumpView();
      });

    this.reload
      .pipe(
        startWith(undefined),
        switchMap(() =>
          this.board.watch().pipe(
            tap((data) => this.apply(data)),
            catchError(() => {
              this.state = 'error';
              this.errorMessage =
                'Backend on port 45211 is not responding (board timed out). Run stop-app.ps1 then start-app.ps1, wait for the backend window, then refresh.';
              this.bumpView();
              return EMPTY;
            }),
          ),
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe();

    merge(
      interval(this.chartRefreshSec * 1000).pipe(
        startWith(0),
        filter(
          () =>
            this.centerView === 'market'
            && this.brokerConnected
            && !!this.pendingChartName
            && !!this.candleSeries
            && !this.chartStreamConnected,
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
            && this.chartStreamConnected,
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

    interval(4000)
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
        filter(() => this.state === 'ready'),
        exhaustMap(() => this.board.liveAgent().pipe(catchError(() => EMPTY))),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((snapshot) => {
        if (
          this.liveAgent &&
          snapshot.updatedAt === this.liveAgent.updatedAt &&
          snapshot.state === this.liveAgent.state
        ) {
          return;
        }
        this.liveAgent = snapshot;
        this.bumpView();
      });

    merge(interval(5000), this.historyReload)
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
        debounceTime(220),
        distinctUntilChanged(),
        switchMap((q) => {
          const trimmed = q.trim();
          if (!trimmed) {
            return of<SymbolSearchHit[]>([]);
          }
          return this.board.searchSymbols(trimmed).pipe(catchError(() => of<SymbolSearchHit[]>([])));
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((hits) => {
        this.pickerCatalogHits = hits;
        this.bumpView();
      });

    this.loadBrokerCredentialsFromStorage();
    this.refreshBrokerCapabilities();
    this.startBrokerSessionPoll();

    const onKey = (event: KeyboardEvent) => {
      if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'p') {
        event.preventDefault();
        this.openPicker();
      }
      if (event.key === 'Escape' && this.pickerOpen) {
        this.closePicker();
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
    this.destroyRef.onDestroy(() => {
      window.removeEventListener('keydown', onKey);
      cancelAnimationFrame(this.chartLoadFrame);
      this.chartResize?.disconnect();
    });
  }

  sendAgentCommand(messageOverride?: string): void {
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
      this.historyRange = 'today';
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
    let items = filterCatalogPaperHistoryTrades(this.tradeHistory?.trades ?? []);
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
    this.historyReload.next();
    queueMicrotask(() => {
      document.getElementById('paper-strategy-trades')?.scrollIntoView({ behavior: 'smooth', block: 'start' });
    });
  }

  clearPaperStrategyFocus(): void {
    this.paperStrategyFocus.clear();
  }

  paperStrategyTradesList(): PaperTradeHistoryItem[] {
    return this.historyCatalogTrades();
  }

  paperStrategyFilterLabel(): string {
    if (!this.paperStrategyFilter) {
      return '';
    }
    return formatPaperHistoryStrategyLabel(this.paperStrategyFilter);
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
    return Math.abs(trade.indicativePrice * trade.quantity);
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

  selectThemePreset(id: AppThemePresetId): void {
    this.themeSelection = { presetId: id };
    saveThemeSelection(this.themeSelection);
    this.applyAppTheme();
  }

  onCustomThemeColor(hex: string): void {
    this.themeSelection = { presetId: 'custom', customBg: hex };
    saveThemeSelection(this.themeSelection);
    this.applyAppTheme();
  }

  private applyAppTheme(): void {
    const palette = resolveThemePalette(this.themeSelection);
    applyThemePalette(palette, this.themeSelection.presetId);
    this.syncChartTheme();
  }

  private syncChartTheme(): void {
    this.tvChart?.applyOptions?.(lightweightChartDisplayOptions(resolveChartSurface()));
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
    return this.centerView === 'market' && this.marketNavTab === 'dashboard';
  }

  brokerHubTriggerLabel(): string {
    if (this.brokerConnected) {
      return this.brokerShortName();
    }
    return 'Broker';
  }

  @HostListener('document:pointerdown', ['$event'])
  onDocumentPointerDown(event: PointerEvent): void {
    const hub = this.brokerHubRef?.nativeElement;
    if (!hub?.open) {
      return;
    }
    const target = event.target;
    if (target instanceof Node && hub.contains(target)) {
      return;
    }
    this.closeBrokerHub();
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
      .pipe(catchError(() => of({ apiKeyConfigured: false })))
      .subscribe((cap) => {
        this.brokerServerApiKeyConfigured = !!cap.apiKeyConfigured;
        this.bumpView();
      });
  }

  brokerNeedsApiKeyInUi(): boolean {
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
    }));
  }

  showStrategies(): void {
    void this.router.navigateByUrl('/strategies');
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
    this.historyReload.next();
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
    queueMicrotask(() => this.symbolSearchInput?.nativeElement.focus());
  }

  onSymbolSearchFocus(): void {
    if (!this.pickerOpen) {
      this.pickerOpen = true;
      if (this.marketKind === 'options') {
        this.pickerKind = 'options';
        this.ensureOptionExpiry();
      }
      this.bumpView();
    }
  }

  onSymbolSearchInput(): void {
    this.pickerOpen = true;
    this.pickerSearchQuery$.next(this.pickerQuery);
    this.bumpView();
  }

  closePicker(): void {
    this.pickerOpen = false;
    this.bumpView();
  }

  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent): void {
    if (!this.pickerOpen) {
      return;
    }
    const root = this.symbolSearchRoot?.nativeElement;
    if (root && event.target instanceof Node && !root.contains(event.target)) {
      this.closePicker();
    }
  }

  pickPickerUnderlying(name: string): void {
    this.optionUnderlying = name;
    this.ensureOptionExpiry();
  }

  setPickerKind(kind: MarketKind): void {
    this.pickerKind = kind;
    this.pickerQuery = '';
    if (kind === 'options') {
      this.ensureOptionExpiry();
    }
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
    const q = this.pickerQuery.trim().toLowerCase();
    const rows = this.boardData?.symbols ?? [];
    if (!q) {
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
      .filter((s) => s.name.toLowerCase().includes(q))
      .map((s) => ({ name: s.name, last: this.pickerRowLast(s), subtitle: undefined }));
    const boardNames = new Set(boardMatches.map((r) => r.name.toUpperCase()));
    const catalogMatches = this.pickerCatalogHits
      .filter((h) => !boardNames.has(h.name.toUpperCase()))
      .map((h) => ({
        name: h.name,
        last: null,
        subtitle: h.companyName || h.exchange,
      }));
    return [...boardMatches, ...catalogMatches].slice(0, 24);
  }

  pickerOptions(): OptionContract[] {
    const q = this.pickerQuery.trim().toLowerCase();
    const rows = this.optionChain(this.optionUnderlying, this.optionExpiry);
    if (!q) {
      return [];
    }
    return rows
      .filter((c) => c.label.toLowerCase().includes(q) || String(c.strike).includes(q))
      .slice(0, 24);
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
    return 50;
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

  onBrokerProviderChange(): void {
    if (this.brokerConnected) {
      return;
    }
    this.otpRequested = false;
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
          this.pollBrokerSession();
          this.reload.next();
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

  private startBrokerSessionPoll(): void {
    this.pollBrokerSessionRequest().subscribe((res) => this.applyBrokerSession(res));
    interval(10_000)
      .pipe(
        exhaustMap(() => this.pollBrokerSessionRequest()),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((res) => this.applyBrokerSession(res));
  }

  private pollBrokerSession(): void {
    this.pollBrokerSessionRequest().subscribe((res) => this.applyBrokerSession(res));
  }

  private pollBrokerSessionRequest(): Observable<{
    connected?: boolean;
    provider?: string | null;
    displayName?: string | null;
    availableFunds?: number | null;
    utilizedFunds?: number | null;
  }> {
    return this.http
      .get<{
        connected?: boolean;
        provider?: string | null;
        displayName?: string | null;
        availableFunds?: number | null;
        utilizedFunds?: number | null;
      }>('/api/broker/session')
      .pipe(timeout(8_000), catchError(() => of({ connected: false })));
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
    this.reload.next();
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
    if (this.chartRefreshPending) {
      return this.chartPaintedFromCache ? 'Cached · refreshing…' : 'Refreshing…';
    }
    if (this.chartFullHistoryPending || (this.chartBusy && hasCandles)) {
      return this.chartPaintedFromCache ? 'Cached · updating history…' : 'Updating history…';
    }
    if (this.chartPaintedFromCache) {
      return 'Cached';
    }
    return this.chartStreamConnected ? 'Live · stream' : 'Live';
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
          ? 'Click the first trend point'
          : '';
  }

  cancelDrawing(): void {
    this.drawTool = 'none';
    this.trendFrom = undefined;
    this.drawingStatus = '';
  }

  toggleIndicator(kind: IndicatorKind): void {
    if (this.activeIndicators.has(kind)) {
      this.activeIndicators.delete(kind);
    } else {
      this.activeIndicators.add(kind);
    }
    this.activeIndicators = new Set(this.activeIndicators);
    this.updateIndicators();
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
    if (boardChanged) {
      this.lastAppliedBoardKey = boardKey;
      this.ingestBoard(data);
      if (!data.symbols.length) {
        this.state = 'empty';
        this.selected = null;
        this.bumpView();
        return;
      }
      this.state = 'ready';
      const preferred = this.marketKind === 'options' ? this.optionUnderlying : this.selectedName;
      const preferredRow =
        this.boardData?.symbols.find((s) => s.name === preferred) ?? this.boardData!.symbols[0];
      if (this.selected?.name === preferredRow.name) {
        Object.assign(this.selected, preferredRow);
      } else {
        this.selected = preferredRow;
        this.selectedName = preferredRow.name;
        this.pendingChartName = preferredRow.name;
        this.scheduleChartLoad();
      }
      this.applyBrokerMessageFromBoardFeed(data);
      this.refreshOptionContractRails();
      this.bumpView();
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
    if (boardChanged) {
      this.scheduleChartLoad();
    }
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
      return;
    }

    this.boardData.counts = data.counts;
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
      .map((symbol) => `${symbol.name}:${symbol.mark}:${symbol.last}:${symbol.b1}:${symbol.s1}`)
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
    return symbolHasVerifiedQuote(row) ? row!.last : null;
  }

  private pickerRowLast(symbol: SymbolSnapshot): number | null {
    return symbolHasVerifiedQuote(symbol) ? symbol.last : null;
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

    void this.chartStream.syncWatch(name, this.chartInterval).catch(() => {
      this.chartStreamConnected = false;
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

    const bar: ChartBar = {
      time: update.bar.time,
      open: Number(update.bar.open),
      high: Number(update.bar.high),
      low: Number(update.bar.low),
      close: Number(update.bar.close),
    };
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
    if (this.selected && !symbolHasVerifiedQuote(this.selected)) {
      return null;
    }
    const last = this.selected?.last ?? this.lastChart?.last ?? 0;
    return last > 0 ? last : null;
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
    const candles = remount
      ? normalizeChartCandles(data.candles)
      : mergeChartCandlesStable(prev, data.candles);
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
    this.drawingPriceLines.clear();
    this.drawingTrendSeries.clear();
    this.crosshairCandle = undefined;
    const width = Math.max(host.clientWidth, 320);
    const height = Math.max(host.clientHeight, 360);
    const chartSurface = resolveChartSurface();
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
    const options: Record<IndicatorKind, { color: string; title: string }> = {
      sma20: { color: '#2962ff', title: 'SMA 20' },
      sma50: { color: '#ab47bc', title: 'SMA 50' },
      ema9: { color: '#ff9800', title: 'EMA 9' },
      ema21: { color: '#00bcd4', title: 'EMA 21' },
    };
    for (const kind of Object.keys(options) as IndicatorKind[]) {
      this.indicatorSeries.set(
        kind,
        this.tvChart.addLineSeries({
          color: options[kind].color,
          lineWidth: 2,
          title: options[kind].title,
          priceLineVisible: false,
          lastValueVisible: true,
          crosshairMarkerVisible: false,
        }),
      );
    }
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

    const definitions: Record<IndicatorKind, { period: number; kind: 'sma' | 'ema' }> = {
      sma20: { period: 20, kind: 'sma' },
      sma50: { period: 50, kind: 'sma' },
      ema9: { period: 9, kind: 'ema' },
      ema21: { period: 21, kind: 'ema' },
    };
    for (const [name, series] of this.indicatorSeries) {
      const definition = definitions[name];
      series.setData(
        this.activeIndicators.has(name)
          ? movingAverage(candles, definition.period, definition.kind)
          : [],
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
      if (drawing.kind === 'trend' && drawing.from && drawing.to) {
        const series = this.tvChart.addLineSeries({
          color: drawing.color,
          lineWidth: this.selectedDrawingId === drawing.id ? 3 : 2,
          priceLineVisible: false,
          lastValueVisible: false,
        });
        series.setData([
          { time: drawing.from.time, value: drawing.from.value },
          { time: drawing.to.time, value: drawing.to.value },
        ]);
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
      return;
    }
    if (this.drawTool === 'trend' && param.time != null && this.tvChart) {
      if (!this.trendFrom) {
        this.trendFrom = { time: param.time, value: price };
        this.drawingStatus = 'First point set — click the second trend point';
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
    }
  }
}
