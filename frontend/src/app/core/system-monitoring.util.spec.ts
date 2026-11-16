import { buildComponentRows, mapComponentState, mapOverallStatus } from './system-monitoring.util';

describe('system-monitoring.util', () => {
  it('maps component health states to UI statuses', () => {
    expect(mapComponentState('Healthy')).toBe('HEALTHY');
    expect(mapComponentState('Stalled')).toBe('UNHEALTHY');
    expect(mapComponentState(undefined)).toBe('UNKNOWN');
  });

  it('maps overall health including critical', () => {
    expect(mapOverallStatus('Critical')).toBe('UNHEALTHY');
    expect(mapOverallStatus('Degraded')).toBe('DEGRADED');
  });

  it('builds component rows from summaries', () => {
    const rows = buildComponentRows({
      summaries: [
        {
          component: 'database',
          status: 'Healthy',
          lastSuccessfulCheck: '2026-01-01T00:00:00Z',
          lastFailure: null,
          consecutiveFailures: 0,
          lastError: null,
          checkedAt: '2026-01-01T00:00:00Z',
          metrics: null,
        },
      ],
      workers: [],
      workerHeartbeats: [],
      broker: null,
      marketData: null,
      signalR: null,
    });
    expect(rows.length).toBe(1);
    expect(rows[0].status).toBe('HEALTHY');
  });
});