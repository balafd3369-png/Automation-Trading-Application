import { SymbolSnapshot } from './board.models';
import { isListedIndex } from './lot-size.util';
import {
  ManualExecutionMode,
  ManualInstrumentType,
  ManualTransactionType,
} from './manual-trading.models';
import { TradeTicketContext } from '../trade-ticket/trade-ticket.models';

export interface OptionContractTicket {
  underlying: string;
  expiry: string;
  strike: number;
  side: 'CE' | 'PE';
  last?: number;
  previousClose?: number;
}

const INDEX_NAMES = new Set(['NIFTY', 'BANKNIFTY', 'FINNIFTY', 'SENSEX']);

export function tradeTicketExchange(
  instrumentType: ManualInstrumentType,
  underlying: string,
): string {
  const u = underlying.trim().toUpperCase();
  if (instrumentType === 'STOCK') {
    return 'NSE';
  }
  if (instrumentType === 'OPTION' || instrumentType === 'FUTURE') {
    return u === 'SENSEX' ? 'BFO' : 'NFO';
  }
  return 'NSE';
}

export function priceChangeFromClose(
  ltp: number | null | undefined,
  previousClose: number | null | undefined,
): { points: number; percent: number } | null {
  if (ltp == null || previousClose == null || previousClose <= 0) {
    return null;
  }
  const points = ltp - previousClose;
  const percent = (points / previousClose) * 100;
  return { points, percent };
}

export function defaultProduct(
  mode: ManualExecutionMode,
  instrumentType: ManualInstrumentType,
): string {
  if (instrumentType === 'FUTURE') {
    return 'NRML';
  }
  if (instrumentType === 'STOCK' && mode === 'LIVE') {
    return 'CNC';
  }
  return 'MIS';
}

export function contextFromStock(
  row: SymbolSnapshot,
  side: ManualTransactionType,
  mode?: ManualExecutionMode,
): TradeTicketContext {
  const name = row.name.trim();
  const isIndex = INDEX_NAMES.has(name);
  const instrumentType: ManualInstrumentType = isIndex ? 'FUTURE' : 'STOCK';
  return {
    instrumentType,
    displayName: name,
    exchange: tradeTicketExchange(instrumentType, name),
    symbol: name,
    underlying: name,
    transactionType: side,
    ltp: row.last,
    previousClose: row.previousClose,
    executionMode: mode,
  };
}

export function contextFromOption(
  contract: OptionContractTicket,
  side: ManualTransactionType,
  tradingSymbol?: string | null,
  mode?: ManualExecutionMode,
): TradeTicketContext {
  const strikeText = Number.isInteger(contract.strike)
    ? String(contract.strike)
    : contract.strike.toFixed(2);
  const displayName = `${contract.underlying} ${strikeText} ${contract.side}`;
  return {
    instrumentType: 'OPTION',
    displayName,
    exchange: tradeTicketExchange('OPTION', contract.underlying),
    symbol: tradingSymbol?.trim() || contract.underlying,
    underlying: contract.underlying,
    transactionType: side,
    ltp: contract.last,
    previousClose: contract.previousClose,
    strike: contract.strike,
    optionRight: contract.side,
    expiry: contract.expiry,
    executionMode: mode,
  };
}

export function contextFromRecommendation(
  symbol: string,
  direction: string,
  entryPrice: number,
  mode?: ManualExecutionMode,
): TradeTicketContext {
  const underlying = symbol.trim().toUpperCase();
  const long = direction.toUpperCase().includes('LONG') || direction.toUpperCase().includes('BUY');
  const instrumentType: ManualInstrumentType = isListedIndex(underlying) ? 'FUTURE' : 'STOCK';
  return {
    instrumentType,
    displayName: underlying,
    exchange: tradeTicketExchange(instrumentType, underlying),
    symbol: underlying,
    underlying,
    transactionType: long ? 'BUY' : 'SELL',
    ltp: entryPrice,
    previousClose: entryPrice,
    executionMode: mode ?? 'PAPER',
  };
}

export function manualExecutionFromAppMode(mode: 'paper' | 'live'): ManualExecutionMode {
  return mode === 'live' ? 'LIVE' : 'PAPER';
}
