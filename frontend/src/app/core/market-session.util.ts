import { istParts, isIstTradingWeekday } from './chart-ui.util';

/** NSE cash / index / F&O chart session (IST). */
export const NSE_CASH_SESSION_OPEN_HOUR = 9;
export const NSE_CASH_SESSION_OPEN_MINUTE = 15;
export const NSE_CASH_CHART_CLOSE_HOUR = 15;
export const NSE_CASH_CHART_CLOSE_MINUTE = 30;

export const NSE_CASH_SESSION_OPEN_MINUTES =
  NSE_CASH_SESSION_OPEN_HOUR * 60 + NSE_CASH_SESSION_OPEN_MINUTE;
export const NSE_CASH_CHART_CLOSE_MINUTES =
  NSE_CASH_CHART_CLOSE_HOUR * 60 + NSE_CASH_CHART_CLOSE_MINUTE;

/** True during 09:15–15:30 IST on weekdays (live tick + chart mutation allowed). */
export function isNseChartLiveWindow(timeSec = Math.floor(Date.now() / 1000)): boolean {
  if (!isIstTradingWeekday(timeSec)) {
    return false;
  }
  const { hour, minute } = istParts(timeSec);
  const minutes = hour * 60 + minute;
  return minutes >= NSE_CASH_SESSION_OPEN_MINUTES && minutes < NSE_CASH_CHART_CLOSE_MINUTES;
}

function formatIstDateKey(d: Date): string {
  const fmt = new Intl.DateTimeFormat('en-CA', {
    timeZone: 'Asia/Kolkata',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  });
  return fmt.format(d);
}

function parseIstDateKey(dateKey: string): Date {
  return new Date(`${dateKey}T12:00:00+05:30`);
}

/** Walk back to the previous weekday (Sat/Sun skipped; exchange holidays need server filter on bars). */
export function previousIstTradingDayKey(dateKey: string, maxSteps = 10): string {
  let cursor = parseIstDateKey(dateKey);
  for (let i = 0; i < maxSteps; i++) {
    cursor.setDate(cursor.getDate() - 1);
    const key = formatIstDateKey(cursor);
    const sec = Math.floor(cursor.getTime() / 1000);
    if (isIstTradingWeekday(sec)) {
      return key;
    }
  }
  return dateKey;
}

/**
 * Which session's intraday chart the user should see.
 * - From 09:15 through overnight until next 09:15: today's session (frozen after 15:30).
 * - Before 09:15 on a trading day: previous session (yesterday's close chart).
 */
export function chartSessionIstDateKey(timeSec = Math.floor(Date.now() / 1000)): string {
  const parts = istParts(timeSec);
  const minutes = parts.hour * 60 + parts.minute;
  if (isIstTradingWeekday(timeSec) && minutes >= NSE_CASH_SESSION_OPEN_MINUTES) {
    return parts.dateKey;
  }
  if (isIstTradingWeekday(timeSec) && minutes < NSE_CASH_SESSION_OPEN_MINUTES) {
    return previousIstTradingDayKey(parts.dateKey);
  }
  return previousIstTradingDayKey(parts.dateKey);
}

export function chartPersistenceKey(baseKey: string, timeSec = Math.floor(Date.now() / 1000)): string {
  return `${baseKey}|sess:${chartSessionIstDateKey(timeSec)}`;
}

/** Last bar of chart belongs to the active display session (intraday intervals). */
export function chartCandlesMatchSession(candles: { time: number }[], sessionKey?: string): boolean {
  if (!candles.length) {
    return false;
  }
  const sess = sessionKey ?? chartSessionIstDateKey();
  const tail = candles[candles.length - 1];
  return istParts(tail.time).dateKey === sess;
}

/** Live tick / stream updates may only mutate bars belonging to the active display session. */
export function barInActiveChartSession(barTime: number, sessionKey?: string): boolean {
  const sess = sessionKey ?? chartSessionIstDateKey();
  return istParts(barTime).dateKey === sess;
}
