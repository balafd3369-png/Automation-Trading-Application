import { DatePipe, DecimalPipe, NgClass } from '@angular/common';
import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  OnDestroy,
  OnInit,
  inject,
} from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { Subscription, interval, startWith, switchMap } from 'rxjs';
import { stashMarketSymbolPick } from '../core/app-navigation.util';
import { BoardService } from '../core/board.service';
import { AuthService } from '../core/auth.service';
import { OpenVsZonesBoardResponse, OpenVsZonesRow } from '../core/user-dashboard.models';

type ZoneFilter = 'all' | 'aboveB2' | 'aboveB1' | 'belowS1' | 'belowS2';

@Component({
  selector: 'app-admin-open-vs-zones',
  standalone: true,
  imports: [DecimalPipe, DatePipe, NgClass, RouterLink],
  templateUrl: './admin-open-vs-zones-page.component.html',
  styleUrl: './admin-open-vs-zones-page.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdminOpenVsZonesPageComponent implements OnInit, OnDestroy {
  private readonly board = inject(BoardService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly cdr = inject(ChangeDetectorRef);

  data: OpenVsZonesBoardResponse | null = null;
  error = '';
  kind: 'indices' | 'stocks' = 'indices';
  filter: ZoneFilter = 'all';
  private poll?: Subscription;

  ngOnInit(): void {
    this.poll = interval(15_000)
      .pipe(
        startWith(0),
        switchMap(() => this.board.adminOpenVsZones()),
      )
      .subscribe({
        next: (payload) => {
          this.data = payload;
          this.error = '';
          this.cdr.markForCheck();
        },
        error: () => {
          this.error = 'Could not load open vs zones (admin only).';
          this.cdr.markForCheck();
        },
      });
  }

  ngOnDestroy(): void {
    this.poll?.unsubscribe();
  }

  rows(): OpenVsZonesRow[] {
    const list = this.kind === 'indices' ? (this.data?.indices ?? []) : (this.data?.stocks ?? []);
    return list.filter((row) => this.matchesFilter(row));
  }

  counts(kind: 'indices' | 'stocks'): { aboveB2: number; aboveB1: number; belowS1: number; belowS2: number } {
    const list = kind === 'indices' ? (this.data?.indices ?? []) : (this.data?.stocks ?? []);
    return {
      aboveB2: list.filter((r) => r.openAboveB2).length,
      aboveB1: list.filter((r) => r.openAboveB1).length,
      belowS1: list.filter((r) => r.openBelowS1).length,
      belowS2: list.filter((r) => r.openBelowS2).length,
    };
  }

  setKind(kind: 'indices' | 'stocks'): void {
    this.kind = kind;
    this.cdr.markForCheck();
  }

  setFilter(filter: ZoneFilter): void {
    this.filter = filter;
    this.cdr.markForCheck();
  }

  trade(row: OpenVsZonesRow): void {
    stashMarketSymbolPick(row.name);
    void this.router.navigateByUrl('/market');
  }

  logout(): void {
    this.auth.logout().subscribe(() => void this.router.navigateByUrl('/login'));
  }

  private matchesFilter(row: OpenVsZonesRow): boolean {
    switch (this.filter) {
      case 'aboveB2':
        return row.openAboveB2;
      case 'aboveB1':
        return row.openAboveB1;
      case 'belowS1':
        return row.openBelowS1;
      case 'belowS2':
        return row.openBelowS2;
      default:
        return true;
    }
  }
}
