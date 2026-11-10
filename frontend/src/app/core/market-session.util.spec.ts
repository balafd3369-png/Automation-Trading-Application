import {
  chartSessionIstDateKey,
  isNseChartLiveWindow,
  NSE_CASH_CHART_CLOSE_MINUTES,
  NSE_CASH_SESSION_OPEN_MINUTES,
} from './market-session.util';

describe('market-session.util', () => {
  it('defines NSE cash chart window 09:15–15:30', () => {
    expect(NSE_CASH_SESSION_OPEN_MINUTES).toBe(9 * 60 + 15);
    expect(NSE_CASH_CHART_CLOSE_MINUTES).toBe(15 * 60 + 30);
  });

  it('treats 10:00 IST on a weekday as live', () => {
    const sec = Math.floor(Date.parse('2026-10-29T10:00:00+05:30') / 1000);
    expect(isNseChartLiveWindow(sec)).toBe(true);
    expect(chartSessionIstDateKey(sec)).toBe('2026-10-29');
  });

  it('before 09:15 uses previous session date key', () => {
    const sec = Math.floor(Date.parse('2026-10-29T08:00:00+05:30') / 1000);
    expect(isNseChartLiveWindow(sec)).toBe(false);
    expect(chartSessionIstDateKey(sec)).toBe('2026-10-28');
  });

  it('after 15:30 keeps same session date until midnight', () => {
    const sec = Math.floor(Date.parse('2026-10-29T18:00:00+05:30') / 1000);
    expect(isNseChartLiveWindow(sec)).toBe(false);
    expect(chartSessionIstDateKey(sec)).toBe('2026-10-29');
  });
});
