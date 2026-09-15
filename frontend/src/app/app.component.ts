import { DatePipe, DecimalPipe, NgClass } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { AfterViewChecked, Component, DestroyRef, ElementRef, OnInit, ViewChild, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { EMPTY, Subject, catchError, startWith, switchMap, tap } from 'rxjs';
import { BoardResponse, ChartResponse, PressureMark, SymbolSnapshot } from './core/board.models';
import { BoardService } from './core/board.service';

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
}
type TvChart = {
  addCandlestickSeries(options: Record<string, unknown>): TvCandle;
  addLineSeries(options: Record<string, unknown>): { setData(data: Array<Record<string, unknown>>): void };
  timeScale(): { fitContent(): void };
  subscribeClick(handler: (param: { point?: { x: number; y: number }; time?: number }) => void): void;
  resize(width: number, height: number): void;
  remove(): void;
};
type TvCandle = {
  setData(data: Array<Record<string, unknown>>): void;
  createPriceLine(options: Record<string, unknown>): unknown;
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
  @ViewChild('chartHost') chartHost?: ElementRef<HTMLDivElement>;
  private loadedChartName = '';
  private pendingChartName = '';
  private chartBusy = false;
  private tvChart?: TvChart;
  private candleSeries?: TvCandle;
  private lastChart?: ChartResponse;
  private chartResize?: ResizeObserver;
  private trendFrom?: { time: number; value: number };
  drawTool: DrawTool = 'none';
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
  brokerBusy = false;
  brokerConnected = false;
  brokerMessage = 'Mock prices until m.Stock OTP is verified in this box (chat is too slow).';
  readonly indexTabs = ['NIFTY', 'SENSEX', 'BANKNIFTY', 'FINNIFTY'];
  marketKind: MarketKind = 'stocks';
  pickerOpen = false;
  pickerKind: MarketKind = 'stocks';
  pickerQuery = '';
  optionUnderlying = 'NIFTY';
  optionExpiry = '';
  optionSide: OptionSide = 'CE';
  openedOption: OptionContract | null = null;

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

    this.refreshSession();

    const onKey = (event: KeyboardEvent) => {
      if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'p') {
        event.preventDefault();
        this.openPicker();
      }
      if (event.key === 'Escape' && this.pickerOpen) {
        this.closePicker();
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
    } else {
      this.openedOption = null;
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
    this.closePicker();
  }

  setOptionUnderlying(name: string): void {
    this.optionUnderlying = name;
    this.selectIndex(name);
    this.ensureOptionExpiry();
    this.openedOption = this.atmContract(name, this.optionExpiry, this.optionSide);
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
    return {
      underlying,
      expiry,
      expiryLabel,
      strike,
      side,
      label: `${underlying} ${expiryLabel} ${strikeText} ${side}`,
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

  sendBrokerOtp(): void {
    this.brokerBusy = true;
    this.brokerMessage = 'Requesting OTP from m.Stock…';
    this.http.post<{ status?: string; message?: string; data?: { is_error?: string } }>('/api/broker/otp/send', {}).subscribe({
      next: (res) => {
        this.brokerBusy = false;
        if (res.status === 'success') {
          this.brokerMessage = 'OTP sent to your registered mobile. Type it here immediately.';
          return;
        }
        this.brokerMessage = res.message || 'Could not send OTP.';
      },
      error: () => {
        this.brokerBusy = false;
        this.brokerMessage = 'OTP send failed. Is the backend running?';
      },
    });
  }

  verifyBrokerOtp(): void {
    const code = this.otp.trim();
    if (!code) {
      this.brokerMessage = 'Enter the 6-digit OTP.';
      return;
    }
    this.brokerBusy = true;
    this.http.post<{ status?: string; connected?: boolean; message?: string }>('/api/broker/otp/verify', { otp: code }).subscribe({
      next: (res) => {
        this.brokerBusy = false;
        this.otp = '';
        this.brokerConnected = !!res.connected;
        this.brokerMessage = res.message || (this.brokerConnected ? 'Connected.' : 'Verify failed.');
      },
      error: () => {
        this.brokerBusy = false;
        this.brokerMessage = 'OTP verify failed.';
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
    const key = `${this.pendingChartName}|${this.chartInterval}`;
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
  }

  clearDrawings(): void {
    if (this.lastChart) {
      this.mountChart(this.lastChart);
    }
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
    if (!row) {
      return 'flat';
    }
    const base = row.previousClose || row.todayOpen;
    if (!base) {
      return 'flat';
    }
    if (row.last > base) {
      return 'up';
    }
    if (row.last < base) {
      return 'down';
    }
    return 'flat';
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
      return `${zone} · chart is ${symbol.name} (underlying)`;
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
    this.brokerConnected = data.feed.startsWith('m.Stock');
    if (this.brokerConnected && !this.brokerMessage.startsWith('m.Stock session')) {
      this.brokerMessage = 'm.Stock session active. Last prices from broker (no orders).';
    }
  }

  private loadChart(name: string, interval: string): void {
    const host = this.chartHost?.nativeElement;
    if (!host) {
      return;
    }
    this.chartBusy = true;
    this.loadedChartName = `${name}|${interval}`;
    this.board.chart(name, interval).subscribe({
      next: (data) => {
        this.lastChart = data;
        this.mountChart(data);
        this.chartBusy = false;
      },
      error: () => {
        if (this.selected && this.selected.name === name) {
          this.lastChart = this.fallbackChart(name, this.selected);
          this.mountChart(this.lastChart);
        }
        this.chartBusy = false;
      },
    });
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
    const width = Math.max(host.clientWidth, 320);
    const height = Math.max(host.clientHeight, 360);
    this.tvChart = tv.createChart(host, {
      width,
      height,
      layout: { background: { color: '#131313' }, textColor: '#9d9d9d' },
      grid: { vertLines: { color: '#222' }, horzLines: { color: '#222' } },
      rightPriceScale: { borderColor: '#333', scaleMargins: { top: 0.12, bottom: 0.12 } },
      timeScale: { borderColor: '#333', timeVisible: true, secondsVisible: false, rightOffset: 8, barSpacing: 8 },
      crosshair: { mode: 0 },
    });
    this.bindChartResize(host);
    this.candleSeries = this.tvChart.addCandlestickSeries({
      upColor: '#3fb950',
      downColor: '#f85149',
      borderVisible: false,
      wickUpColor: '#3fb950',
      wickDownColor: '#f85149',
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
    const lines = [
      { price: Number(data.b2), color: '#238636', title: 'B2' },
      { price: Number(data.b1), color: '#3fb950', title: 'B1' },
      { price: Number(data.s), color: '#79b8ff', title: 'S' },
      { price: Number(data.s1), color: '#f85149', title: 'S1' },
      { price: Number(data.s2), color: '#da3633', title: 'S2' },
    ];
    for (const line of lines) {
      this.candleSeries.createPriceLine({
        price: line.price,
        color: line.color,
        lineWidth: 2,
        lineStyle: 2,
        axisLabelVisible: true,
        title: line.title,
      });
    }
    this.tvChart.timeScale().fitContent();
    this.tvChart.subscribeClick((param) => this.onChartClick(param));
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
      this.candleSeries.createPriceLine({
        price,
        color: '#d29922',
        lineWidth: 1,
        lineStyle: 0,
        axisLabelVisible: true,
        title: 'line',
      });
      this.drawTool = 'none';
      return;
    }
    if (this.drawTool === 'trend' && param.time != null && this.tvChart) {
      if (!this.trendFrom) {
        this.trendFrom = { time: param.time, value: price };
        return;
      }
      const series = this.tvChart.addLineSeries({ color: '#d29922', lineWidth: 2 });
      const a = this.trendFrom.time <= param.time ? this.trendFrom : { time: param.time, value: price };
      const b = this.trendFrom.time <= param.time ? { time: param.time, value: price } : this.trendFrom;
      series.setData([
        { time: a.time, value: a.value },
        { time: b.time, value: b.value },
      ]);
      this.trendFrom = undefined;
      this.drawTool = 'none';
    }
  }
}
