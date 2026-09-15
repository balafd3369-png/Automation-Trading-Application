export type PressureMark = 'BUYING' | 'SELLING' | 'RANGE BOUND';

export interface BoardCounts {
  buying: number;
  selling: number;
  rangeBound: number;
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

export interface ChartResponse {
  name: string;
  candles: ChartBar[];
  last: number;
  b2: number;
  b1: number;
  s: number;
  s1: number;
  s2: number;
}
