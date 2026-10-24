import { BoardCounts } from './board.models';

export type MarketSentimentMode = 'sell' | 'buy' | 'range';

export interface SentimentParticle {
  id: number;
  left: number;
  delay: number;
  duration: number;
  size: number;
  tone: 0 | 1 | 2;
  drift: number;
}

const TIE_RATIO = 0.12;

export function resolveMarketSentimentMode(counts: BoardCounts | null | undefined): MarketSentimentMode {
  if (!counts) {
    return 'range';
  }
  const buying = counts.buying ?? 0;
  const selling = counts.selling ?? 0;
  const pressure = buying + selling;
  if (pressure === 0) {
    return 'range';
  }
  if (buying === selling) {
    return 'range';
  }
  if (Math.abs(buying - selling) / pressure < TIE_RATIO) {
    return 'range';
  }
  return selling > buying ? 'sell' : 'buy';
}

export function marketMomentumBadgeLabel(mode: MarketSentimentMode): string {
  switch (mode) {
    case 'sell':
      return 'Bearish Momentum';
    case 'buy':
      return 'Bullish Momentum';
    default:
      return 'Range Bound';
  }
}

export function marketSentimentLabels(mode: MarketSentimentMode): { title: string; subtitle: string } {
  switch (mode) {
    case 'sell':
      return { title: 'Bearish Market', subtitle: 'Selling Pressure Dominant' };
    case 'buy':
      return { title: 'Bullish Market', subtitle: 'Buying Pressure Dominant' };
    default:
      return { title: 'Range Bound Market', subtitle: 'Balanced pressure' };
  }
}

/** Small arrows for pressure rail backgrounds (sell or buy only). */
export function buildRailPressureParticles(mode: 'sell' | 'buy', count: number): SentimentParticle[] {
  const particles: SentimentParticle[] = [];
  for (let i = 0; i < count; i += 1) {
    particles.push({
      id: i,
      left: 6 + ((i * 19) % 88),
      delay: (i * 0.55) % 5,
      duration: 14 + (i % 4) * 2.5,
      size: 6 + (i % 3) * 2,
      tone: (i % 3) as 0 | 1 | 2,
      drift: -8 + (i % 5) * 3,
    });
  }
  return particles;
}

export function buildSentimentParticles(mode: MarketSentimentMode, count: number): SentimentParticle[] {
  const particles: SentimentParticle[] = [];
  for (let i = 0; i < count; i += 1) {
    particles.push({
      id: i,
      left: 4 + ((i * 17) % 92),
      delay: (i * 0.41) % 4.2,
      duration: 9 + (i % 5) * 1.8,
      size: mode === 'range' ? 4 + (i % 3) : 10 + (i % 4) * 4,
      tone: (i % 3) as 0 | 1 | 2,
      drift: -12 + (i % 7) * 4,
    });
  }
  return particles;
}
