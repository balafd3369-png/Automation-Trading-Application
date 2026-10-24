import { decisionMatchesCatalogStrategy } from './strategy-engine.util';
import { RankedDecision } from './board.models';

describe('strategy-engine.util', () => {
  const base: RankedDecision = {
    id: '1',
    strategy: 'TEN_AM_HIGH_FADE_DAY_LOW',
    underlying: 'NIFTY',
    direction: 'BEARISH',
    evidenceScore: 80,
    accepted: true,
    evidence: [],
    rejections: [],
    maxRisk: 100,
    maxReward: 200,
    rewardRisk: 2,
    evaluatedAt: '2026-01-01T10:00:00Z',
    scoreMeaning: 'rules',
  };

  it('matches catalog id to engine code', () => {
    expect(decisionMatchesCatalogStrategy(base, 'bala-10am-high-fade')).toBe(true);
    expect(decisionMatchesCatalogStrategy({ ...base, strategy: 'ZONE_B1_BREAK_B2' }, 'zone-b1-break-b2')).toBe(
      true,
    );
  });
});
