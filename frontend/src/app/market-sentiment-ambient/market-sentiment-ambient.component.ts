import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  HostListener,
  Input,
  OnChanges,
  OnInit,
  SimpleChanges,
  inject,
} from '@angular/core';
import { BoardCounts } from '../core/board.models';
import { MarketBreadthStore } from '../core/market-breadth.store';
import {
  MarketSentimentMode,
  SentimentParticle,
  buildRailPressureParticles,
  buildSentimentParticles,
  marketSentimentLabels,
  resolveMarketSentimentMode,
} from '../core/market-sentiment.util';

@Component({
  selector: 'app-market-sentiment-ambient',
  standalone: true,
  templateUrl: './market-sentiment-ambient.component.html',
  styleUrl: './market-sentiment-ambient.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MarketSentimentAmbientComponent implements OnInit, OnChanges {
  private readonly breadthStore = inject(MarketBreadthStore);
  private readonly cdr = inject(ChangeDetectorRef);

  @Input() buying: number | null | undefined;
  @Input() selling: number | null | undefined;
  @Input() rangeBound: number | null | undefined;
  /** Locks animation to sell / buy / range (used on pressure rails). */
  @Input() forcedMode: MarketSentimentMode | null = null;
  @Input() variant: 'default' | 'rail' = 'default';
  @Input() showLabel = true;
  @Input() compact = false;
  @Input() enabled = true;

  mode: MarketSentimentMode = 'range';
  particles: SentimentParticle[] = [];
  title = '';
  subtitle = '';
  motionEnabled = true;

  ngOnInit(): void {
    this.syncMotionPreference();
    this.rebuild();
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (
      changes['buying']
      || changes['selling']
      || changes['rangeBound']
      || changes['enabled']
      || changes['compact']
      || changes['forcedMode']
      || changes['variant']
    ) {
      this.rebuild();
    }
  }

  @HostListener('window:resize')
  onResize(): void {
    this.syncMotionPreference();
    this.rebuild();
  }

  private counts(): BoardCounts | null {
    if (this.buying != null || this.selling != null || this.rangeBound != null) {
      return {
        buying: this.buying ?? 0,
        selling: this.selling ?? 0,
        rangeBound: this.rangeBound ?? 0,
      };
    }
    return this.breadthStore.snapshot();
  }

  private syncMotionPreference(): void {
    const reduced = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    this.motionEnabled = !reduced && this.enabled;
    this.cdr.markForCheck();
  }

  private rebuild(): void {
    if (!this.enabled) {
      this.particles = [];
      this.cdr.markForCheck();
      return;
    }
    this.mode = this.forcedMode ?? resolveMarketSentimentMode(this.counts());
    const labels = marketSentimentLabels(this.mode);
    this.title = labels.title;
    this.subtitle = labels.subtitle;
    const narrow = window.matchMedia('(max-width: 768px)').matches;
    let count = 0;
    if (this.motionEnabled) {
      if (this.variant === 'rail') {
        count = narrow ? 8 : 12;
      } else {
        count = this.compact ? (narrow ? 6 : 10) : narrow ? 8 : 14;
      }
    }
    if (this.variant === 'rail' && (this.mode === 'sell' || this.mode === 'buy')) {
      this.particles = buildRailPressureParticles(this.mode, count);
    } else {
      this.particles = buildSentimentParticles(this.mode, count);
    }
    this.cdr.markForCheck();
  }
}
