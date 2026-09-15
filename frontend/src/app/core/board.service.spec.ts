import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { BoardResponse, ChartResponse } from './board.models';
import { BoardService } from './board.service';

describe('BoardService', () => {
  let service: BoardService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [BoardService, provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(BoardService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('loads board snapshot from /api/board', async () => {
    const payload: BoardResponse = {
      counts: { buying: 1, selling: 2, rangeBound: 3 },
      symbols: [
        {
          name: 'NIFTY',
          last: 23300,
          todayOpen: 23576.15,
          yesterdayHigh: 23600,
          yesterdayLow: 23200,
          highTwoDaysAgo: 23700,
          lowTwoDaysAgo: 23100,
          b2: 23650,
          b1: 23588.08,
          s: 23458.72,
          s1: 23388.08,
          s2: 23150,
          mark: 'SELLING',
          tightRange: false,
          previousClose: 23576.15,
        },
      ],
      updatedAt: '2026-09-15T05:00:00Z',
      feed: 'm.Stock live',
    };

    const boardPromise = firstValueFrom(service.watch());
    const req = httpMock.expectOne('/api/board');
    expect(req.request.method).toBe('GET');
    req.flush(payload);

    const board = await boardPromise;
    expect(board.symbols[0].name).toBe('NIFTY');
    expect(board.feed).toBe('m.Stock live');
  });

  it('loads chart data with interval query param', async () => {
    const payload: ChartResponse = {
      name: 'NIFTY',
      last: 23300,
      b2: 23650,
      b1: 23588.08,
      s: 23458.72,
      s1: 23388.08,
      s2: 23150,
      candles: [{ time: 1726386300, open: 23500, high: 23520, low: 23480, close: 23300 }],
    };

    const chartPromise = firstValueFrom(service.chart('NIFTY', '5minute'));
    const req = httpMock.expectOne((r) => r.url === '/api/board/chart/NIFTY' && r.params.get('interval') === '5minute');
    req.flush(payload);

    const chart = await chartPromise;
    expect(chart.name).toBe('NIFTY');
    expect(chart.candles.length).toBe(1);
    expect(chart.candles[0].close).toBe(23300);
  });
});
