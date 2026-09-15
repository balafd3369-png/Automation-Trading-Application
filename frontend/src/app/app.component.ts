import { DatePipe, DecimalPipe, NgClass } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { AfterViewChecked, Component, DestroyRef, ElementRef, OnInit, ViewChild, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { EMPTY, Observable, Subject, catchError, filter, interval, map, merge, startWith, switchMap, tap } from 'rxjs';
import { BoardResponse, ChartResponse, OptionMarketQuote, PressureMark, SymbolSnapshot } from './core/board.models';
import { BoardService } from './core/board.service';
import {
  ChartDrawing,
  IndicatorKind,
  drawingStorageKey,
  firstChangedCandle,
  istDate,
  istDateTime,
  movingAverage,
  parseDrawings,
  sessionStartBars,
} from './core/chart-ui.util';
import { brokerConnectedFromFeed, syncLiveCandle, tone as rowTone } from './core/trading-ui.util';

type UiState = 'loading' | 'ready' | 'error' | 'empty';
type ZoneKey = 'B2' | 'B1' | 'S' | 'S1' | 'S2';
type DrawTool = 'none' | 'hline' | 'trend';
type MarketKind = 'stocks' | 'options';
type OptionSide = 'CE' | 'PE';

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
  imports: [DecimalPipe, DatePipe, NgClass, FormsModule],
  templateUrl: './app.component.html',
  styleUrl: './app.component.scss',
})
export class AppComponent implements OnInit, AfterViewChecked {
  private readonly board = inject(BoardService);
  private readonly http = inject(HttpClient);
  private readonly destroyRef = inject(DestroyRef);
  private readonly reload = new Subject<void>();
  private readonly optionChainReload = new Subject<void>();
  @ViewChild('chartHost') chartHost?: ElementRef<HTMLDivElement>;
  private loadedChartName = '';
  private pendingChartName = '';
  private chartBusy = false;
  private tvChart?: TvChart;
  private candleSeries?: TvCandle;
  private zonePriceLines: TvPriceLine[] = [];
  lastChart?: ChartResponse;
  private readonly chartRefreshSec = 1;
  private chartResize?: ResizeObserver;
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
  readonly timeframes = [
    { id: 'minute', label: '1m' },
    { id: '5minute', label: '5m' },
    { id: '15minute', label: '15m' },
    { id: '60minute', label: '1h' },
    { id: 'day', label: '1D' },
  ];

  state: UiState = 'loading';
  errorMessage = '';
  boardData: BoardResponse | null = null;
  selectedName = 'NIFTY';
  selected: SymbolSnapshot | null = null;
  clock = new Date();
  otp = '';
  otpRequested = false;
  brokerBusy = false;
  brokerConnected = false;
  brokerMessage = 'Step 1: click Request OTP. Step 2: enter the code from your phone and submit.';
  readonly indexTabs = ['NIFTY', 'SENSEX', 'BANKNIFTY', 'FINNIFTY'];
  marketKind: MarketKind = 'stocks';
  pickerOpen = false;
  pickerKind: MarketKind = 'stocks';
  pickerQuery = '';
  optionUnderlying = 'NIFTY';
  optionExpiry = '';
  optionSide: OptionSide = 'CE';
  openedOption: OptionContract | null = null;
  optionChartError = '';
  optionQuotes = new Map<string, OptionMarketQuote>();

  ngOnInit(): void {
    this.reload
      .pipe(
        startWith(undefined),
        switchMap(() =>
          this.board.watch().pipe(
            tap((data) => this.apply(data)),
            catchError(() => {
              this.state = 'error';
              this.errorMessage =
                'Board API unreachable on /api/board. Is the backend on port 45211?';
              return EMPTY;
            }),
          ),
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe();

    interval(this.chartRefreshSec * 1000)
      .pipe(
        filter(() => this.brokerConnected && !!this.pendingChartName && !!this.candleSeries),
        switchMap(() => {
          const requestKey = this.chartRequestKey();
          return this.chartRequest(this.pendingChartName, this.chartInterval).pipe(
            map((data) => ({ data, requestKey })),
            catchError(() => EMPTY),
          );
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe(({ data, requestKey }) => {
        if (requestKey === this.chartRequestKey()) {
          this.applyChartData(data, false);
        }
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
      });

    this.refreshSession();

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
      this.chartResize?.disconnect();
    });
  }

  setMarketKind(kind: MarketKind): void {
    this.marketKind = kind;
    if (kind === 'options') {
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
    this.pickerKind = kind;
    this.pickerQuery = '';
    this.pickerOpen = true;
    if (kind === 'options') {
      this.ensureOptionExpiry();
    }
  }

  closePicker(): void {
    this.pickerOpen = false;
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

  openStock(row: SymbolSnapshot): void {
    this.marketKind = 'stocks';
    this.openedOption = null;
    this.select(row);
    this.closePicker();
  }

  openOption(contract: OptionContract): void {
    this.marketKind = 'options';
    this.openedOption = contract;
    this.optionUnderlying = contract.underlying;
    this.optionExpiry = contract.expiry;
    this.optionSide = contract.side;
    this.selectIndex(contract.underlying);
    this.optionChartError = '';
    this.loadedChartName = '';
    this.optionChainReload.next();
    this.closePicker();
  }

  setOptionUnderlying(name: string): void {
    this.optionUnderlying = name;
    this.selectIndex(name);
    this.ensureOptionExpiry();
    this.openedOption = this.atmContract(name, this.optionExpiry, this.optionSide);
    this.optionChartError = '';
    this.loadedChartName = '';
    this.optionQuotes = new Map();
    this.optionChainReload.next();
  }

  setOptionExpiry(expiry: string): void {
    this.optionExpiry = expiry;
    if (this.openedOption) {
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
    }
  }

  setOptionSide(side: OptionSide): void {
    this.optionSide = side;
    if (this.openedOption) {
      this.openedOption = this.makeContract(
        this.openedOption.underlying,
        this.openedOption.expiry,
        this.openedOption.strike,
        side,
      );
      this.optionChartError = '';
      this.loadedChartName = '';
    }
  }

  pickerStocks(): SymbolSnapshot[] {
    const q = this.pickerQuery.trim().toLowerCase();
    const rows = this.boardData?.symbols ?? [];
    if (!q) {
      return rows;
    }
    return rows.filter((s) => s.name.toLowerCase().includes(q));
  }

  pickerOptions(): OptionContract[] {
    const q = this.pickerQuery.trim().toLowerCase();
    const rows = this.optionChain(this.optionUnderlying, this.optionExpiry);
    if (!q) {
      return rows;
    }
    return rows.filter((c) => c.label.toLowerCase().includes(q) || String(c.strike).includes(q));
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
    return this.optionChain(this.optionUnderlying, this.optionExpiry).filter((c) => c.side === 'CE');
  }

  peContracts(): OptionContract[] {
    return this.optionChain(this.optionUnderlying, this.optionExpiry).filter((c) => c.side === 'PE');
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

  requestBrokerOtp(): void {
    this.brokerBusy = true;
    this.otpRequested = false;
    this.otp = '';
    this.brokerMessage = 'Requesting OTP from m.Stock…';
    this.http.post<{ status?: string; message?: string; data?: { is_error?: string } }>('/api/broker/otp/send', {}).subscribe({
      next: (res) => {
        this.brokerBusy = false;
        if (res.status === 'success') {
          this.otpRequested = true;
          this.brokerMessage = 'OTP sent. Step 2: enter the code below and click Submit OTP.';
          return;
        }
        this.brokerMessage = res.message || 'Could not request OTP.';
      },
      error: () => {
        this.brokerBusy = false;
        this.brokerMessage = 'OTP request failed. Is the backend running?';
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
    this.brokerBusy = true;
    this.http.post<{ status?: string; connected?: boolean; message?: string }>('/api/broker/otp/verify', { otp: code }).subscribe({
      next: (res) => {
        this.brokerBusy = false;
        this.otp = '';
        this.otpRequested = false;
        this.brokerConnected = !!res.connected;
        this.brokerMessage = res.message || (this.brokerConnected ? 'Live market prices active.' : 'OTP submit failed.');
      },
      error: () => {
        this.brokerBusy = false;
        this.brokerMessage = 'OTP submit failed.';
      },
    });
  }

  private refreshSession(): void {
    this.http.get<{ connected?: boolean }>('/api/broker/session').subscribe({
      next: (res) => (this.brokerConnected = !!res.connected),
      error: () => (this.brokerConnected = false),
    });
  }

  retry(): void {
    this.state = 'loading';
    this.reload.next();
  }

  ngAfterViewChecked(): void {
    const key = this.chartRequestKey();
    const host = this.chartHost?.nativeElement;
    if (!host || !this.pendingChartName || key === this.loadedChartName || this.chartBusy) {
      return;
    }
    if (host.clientWidth < 16 || host.clientHeight < 16) {
      return;
    }
    this.loadChart(this.pendingChartName, this.chartInterval);
  }

  setChartInterval(id: string): void {
    this.chartInterval = id;
    this.loadedChartName = '';
    this.drawTool = 'none';
    this.trendFrom = undefined;
    this.drawingStatus = '';
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
    this.selectedName = symbol.name;
    this.selected = symbol;
    this.pendingChartName = symbol.name;
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
    return (this.boardData?.symbols.filter((s) => s.mark === 'SELLING' && !this.isIndex(s.name)) ?? [])
      .slice()
      .sort((a, b) => a.last - a.s1 - (b.last - b.s1));
  }

  buying(): SymbolSnapshot[] {
    return (this.boardData?.symbols.filter((s) => s.mark === 'BUYING' && !this.isIndex(s.name)) ?? [])
      .slice()
      .sort((a, b) => b.last - b.b1 - (a.last - a.b1));
  }

  ranged(): SymbolSnapshot[] {
    return this.boardData?.symbols.filter((s) => s.mark === 'RANGE BOUND' && !this.isIndex(s.name)) ?? [];
  }

  zoneLines(symbol: SymbolSnapshot): Array<{ k: ZoneKey; v: number }> {
    return [
      { k: 'B2', v: symbol.b2 },
      { k: 'B1', v: symbol.b1 },
      { k: 'S', v: symbol.s },
      { k: 'S1', v: symbol.s1 },
      { k: 'S2', v: symbol.s2 },
    ];
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
    if (this.openedOption) {
      return `option premium · ${symbol.name} underlying ${symbol.last.toFixed(2)}`;
    }
    return zone;
  }

  lineActive(symbol: SymbolSnapshot, line: ZoneKey): boolean {
    const ordered: Array<{ name: ZoneKey; value: number }> = [
      { name: 'B2', value: symbol.b2 },
      { name: 'B1', value: symbol.b1 },
      { name: 'S', value: symbol.s },
      { name: 'S1', value: symbol.s1 },
      { name: 'S2', value: symbol.s2 },
    ];
    ordered.sort((a, b) => b.value - a.value);

    const idx = ordered.findIndex((x) => x.name === line);
    const next = ordered[idx + 1];
    if (!next) {
      return symbol.last <= ordered[idx].value;
    }
    return symbol.last <= ordered[idx].value && symbol.last > next.value;
  }

  private apply(data: BoardResponse): void {
    this.clock = new Date();
    this.boardData = data;
    if (!data.symbols.length) {
      this.state = 'empty';
      this.selected = null;
      return;
    }
    this.state = 'ready';
    const preferred = this.marketKind === 'options' ? this.optionUnderlying : this.selectedName;
    this.selected = data.symbols.find((s) => s.name === preferred) ?? data.symbols[0];
    this.selectedName = this.selected.name;
    this.pendingChartName = this.selected.name;
    this.brokerConnected = brokerConnectedFromFeed(data.feed);
    if (data.feed.includes('session expired')) {
      this.brokerConnected = false;
      this.otpRequested = false;
      this.brokerMessage = 'Session expired. Click Request OTP, then submit the new code.';
    } else if (data.feed.includes('quotes empty')) {
      this.brokerConnected = false;
      this.brokerMessage = 'Connected but quotes empty. Retry Request OTP or wait for market hours.';
    } else if (this.brokerConnected) {
      this.brokerMessage = 'Live market prices from m.Stock (no orders).';
    } else if (data.feed.includes('awaiting m.Stock OTP') && !this.otpRequested && !this.brokerBusy) {
      this.brokerMessage = 'Step 1: click Request OTP. Step 2: enter the code from your phone and submit.';
    }
    this.syncLiveChart();
  }

  private loadChart(name: string, interval: string): void {
    const host = this.chartHost?.nativeElement;
    if (!host) {
      return;
    }
    this.chartBusy = true;
    const requestKey = this.chartRequestKey();
    this.loadedChartName = requestKey;
    this.optionChartError = '';
    this.chartRequest(name, interval).subscribe({
      next: (data) => {
        if (requestKey === this.chartRequestKey()) {
          this.applyChartData(data, true);
        }
        this.chartBusy = false;
      },
      error: () => {
        this.chartBusy = false;
        this.loadedChartName = '';
        if (this.marketKind === 'options' && this.openedOption) {
          this.optionChartError = `No live m.Stock quote found for ${this.openedOption.label}. Try another expiry or strike.`;
        }
      },
    });
  }

  private chartRequest(name: string, interval: string): Observable<ChartResponse> {
    return this.marketKind === 'options' && this.openedOption
      ? this.board.optionChart(this.openedOption, interval)
      : this.board.chart(name, interval);
  }

  private chartRequestKey(): string {
    const instrument =
      this.marketKind === 'options' && this.openedOption
        ? `${this.openedOption.underlying}|${this.openedOption.expiry}|${this.openedOption.strike}|${this.openedOption.side}`
        : this.pendingChartName;
    return `${instrument}|${this.chartInterval}`;
  }

  displayLast(): number {
    return this.openedOption && this.lastChart?.tradingSymbol
      ? this.lastChart.last
      : this.selected?.last ?? 0;
  }

  displayOpen(): number {
    return this.openedOption
      ? this.lastChart?.todayOpen ?? this.lastChart?.candles.at(-1)?.open ?? 0
      : this.selected?.todayOpen ?? 0;
  }

  private applyChartData(data: ChartResponse, remount: boolean): void {
    if (remount || !this.candleSeries) {
      this.lastChart = { ...data, candles: [...data.candles] };
      this.mountChart(data);
      return;
    }
    this.setChartSeriesData(data);
    this.syncLiveChart();
  }

  private syncLiveChart(): void {
    if (this.openedOption || !this.candleSeries || !this.selected || !this.lastChart?.candles.length) {
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
    this.updateZonePriceLines(this.selected);
    this.updateIndicators();
  }

  private updateZonePriceLines(symbol: SymbolSnapshot): void {
    if (this.zonePriceLines.length !== 5) {
      return;
    }
    const levels = [symbol.b2, symbol.b1, symbol.s, symbol.s1, symbol.s2];
    for (let i = 0; i < this.zonePriceLines.length; i++) {
      this.zonePriceLines[i].applyOptions({ price: levels[i] });
    }
  }

  private setChartSeriesData(data: ChartResponse): void {
    if (!this.candleSeries) {
      return;
    }
    const candles = data.candles
      .map((c) => ({
        time: c.time as number,
        open: Number(c.open),
        high: Number(c.high),
        low: Number(c.low),
        close: Number(c.close),
      }))
      .filter((c) => c.open > 0 && c.high >= c.low);
    const prev = this.lastChart?.candles ?? [];
    const changedFrom = firstChangedCandle(prev, data.candles);
    if (prev.length > 0 && candles.length >= prev.length) {
      if (changedFrom >= candles.length) {
        this.lastChart = { ...data, candles: [...data.candles] };
        return;
      }
      if (changedFrom === 0 && candles.length !== prev.length) {
        this.candleSeries.setData(candles);
      } else {
        for (let i = changedFrom; i < candles.length; i++) {
          this.candleSeries.update(candles[i]);
        }
      }
    } else {
      this.candleSeries.setData(candles);
    }
    this.lastChart = { ...data, candles: [...data.candles] };
    const row = this.selected ?? this.indexRow(data.name);
    if (row) {
      this.updateZonePriceLines(row);
    }
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
    this.trendFrom = undefined;
    this.zonePriceLines = [];
    this.indicatorSeries.clear();
    this.drawingPriceLines.clear();
    this.drawingTrendSeries.clear();
    this.crosshairCandle = undefined;
    const width = Math.max(host.clientWidth, 320);
    const height = Math.max(host.clientHeight, 360);
    this.tvChart = tv.createChart(host, {
      width,
      height,
      layout: { background: { color: '#131313' }, textColor: '#d1d4dc' },
      grid: { vertLines: { color: '#2a2e39' }, horzLines: { color: '#2a2e39' } },
      rightPriceScale: { borderColor: '#363a45', scaleMargins: { top: 0.08, bottom: 0.08 } },
      timeScale: {
        borderColor: '#363a45',
        timeVisible: true,
        secondsVisible: false,
        rightOffset: 6,
        barSpacing: 6,
        minBarSpacing: 2,
        fixLeftEdge: false,
        fixRightEdge: false,
        tickMarkFormatter: (time: number, tickMarkType: number) =>
          this.chartInterval === 'day' || tickMarkType <= 2
            ? istDate(time)
            : istDateTime(time, false),
      },
      crosshair: { mode: 1, vertLine: { width: 1, color: '#758696', style: 2 }, horzLine: { width: 1, color: '#758696', style: 2 } },
      localization: {
        locale: 'en-IN',
        timeFormatter: (time: number) => istDateTime(time, true),
      },
    });
    this.bindChartResize(host);
    this.candleSeries = this.tvChart.addCandlestickSeries({
      upColor: '#26a69a',
      downColor: '#ef5350',
      borderVisible: false,
      wickUpColor: '#26a69a',
      wickDownColor: '#ef5350',
      lastValueVisible: true,
      priceLineVisible: true,
      priceLineWidth: 1,
      priceLineColor: '#ef5350',
    });
    const candles = data.candles
      .map((c) => ({
        time: c.time as number,
        open: Number(c.open),
        high: Number(c.high),
        low: Number(c.low),
        close: Number(c.close),
      }))
      .filter((c) => c.open > 0 && c.high >= c.low);
    this.candleSeries.setData(candles);
    const row = this.selected ?? this.indexRow(data.name);
    const zones = row ?? data;
    const lines = this.openedOption ? [] : [
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
    this.tvChart.timeScale().scrollToRealTime();
    this.tvChart.subscribeClick((param) => this.onChartClick(param));
    this.tvChart.subscribeCrosshairMove((param) => this.onCrosshairMove(param));
    this.syncLiveChart();
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
          lineWidth: 1,
          title: options[kind].title,
          priceLineVisible: false,
          lastValueVisible: true,
          crosshairMarkerVisible: false,
        }),
      );
    }
  }

  private updateIndicators(candles = this.lastChart?.candles ?? []): void {
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
    this.candleSeries.setMarkers(
      sessionStartBars(data.candles).map((bar) => ({
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
