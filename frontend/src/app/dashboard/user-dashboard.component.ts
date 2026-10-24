import { DatePipe, DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, ChangeDetectorRef, Component, OnInit, inject } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { catchError, forkJoin, of } from 'rxjs';
import { BoardService } from '../core/board.service';
import { AuthService } from '../core/auth.service';
import { ManualOrder, ManualTradeHistoryItem } from '../core/manual-trading.models';
import { UserDashboardSummary } from '../core/user-dashboard.models';

interface DashboardActivityItem {
  id: string;
  label: string;
  text: string;
  at: string;
}

interface TodayStats {
  tradeCount: number;
  pnl: number;
  winRate: number;
  best: number | null;
  worst: number | null;
}

@Component({
  selector: 'app-user-dashboard',
  standalone: true,
  imports: [DecimalPipe, DatePipe, RouterLink],
  templateUrl: './user-dashboard.component.html',
  styleUrl: './user-dashboard.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class UserDashboardComponent implements OnInit {
  private readonly board = inject(BoardService);
  readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly cdr = inject(ChangeDetectorRef);

  summary: UserDashboardSummary | null = null;
  loadError = '';
  recentTrades: ManualTradeHistoryItem[] = [];
  activityItems: DashboardActivityItem[] = [];
  todayStats: TodayStats = {
    tradeCount: 0,
    pnl: 0,
    winRate: 0,
    best: null,
    worst: null,
  };

  ngOnInit(): void {
    forkJoin({
      summary: this.board.userDashboard().pipe(catchError(() => of(null))),
      manual: this.board.manualTrading('PAPER').pipe(catchError(() => of(null))),
    }).subscribe(({ summary, manual }) => {
      if (!summary) {
        this.loadError = 'Could not load dashboard. Check that the backend is running.';
        this.cdr.markForCheck();
        return;
      }
      this.summary = summary;
      const history = manual?.tradeHistory ?? [];
      this.recentTrades = [...history]
        .sort((a, b) => Date.parse(b.createdAt) - Date.parse(a.createdAt))
        .slice(0, 8);
      this.todayStats = this.computeTodayStats(history);
      this.activityItems = this.buildActivity(history, manual?.orders ?? []);
      this.cdr.markForCheck();
    });
  }

  tradeTimeLabel(t: ManualTradeHistoryItem): string {
    const raw = t.closedAt ?? t.openedAt ?? t.createdAt;
    return new Date(raw).toLocaleString('en-IN', {
      day: 'numeric',
      month: 'short',
      hour: '2-digit',
      minute: '2-digit',
      hour12: false,
      timeZone: 'Asia/Kolkata',
    });
  }

  logout(): void {
    this.auth.logout().subscribe(() => void this.router.navigateByUrl('/login'));
  }

  private istDateKey(iso: string): string {
    return new Intl.DateTimeFormat('en-CA', { timeZone: 'Asia/Kolkata' }).format(new Date(iso));
  }

  private todayIstKey(): string {
    return new Intl.DateTimeFormat('en-CA', { timeZone: 'Asia/Kolkata' }).format(new Date());
  }

  private computeTodayStats(history: ManualTradeHistoryItem[]): TodayStats {
    const today = this.todayIstKey();
    const todayTrades = history.filter((t) => {
      const keys = [t.createdAt, t.openedAt, t.closedAt].filter(Boolean) as string[];
      return keys.some((k) => this.istDateKey(k) === today);
    });
    const closedToday = todayTrades.filter((t) => t.status === 'CLOSED' && t.closedAt && this.istDateKey(t.closedAt) === today);
    const pnls = closedToday.map((t) => t.pnl ?? 0);
    const pnl = pnls.reduce((a, b) => a + b, 0);
    const wins = closedToday.filter((t) => (t.pnl ?? 0) > 0).length;
    const winRate = closedToday.length > 0 ? (wins * 100) / closedToday.length : 0;
    return {
      tradeCount: todayTrades.length,
      pnl,
      winRate,
      best: pnls.length ? Math.max(...pnls) : null,
      worst: pnls.length ? Math.min(...pnls) : null,
    };
  }

  private buildActivity(history: ManualTradeHistoryItem[], orders: ManualOrder[]): DashboardActivityItem[] {
    const items: DashboardActivityItem[] = [];

    for (const o of orders.slice(0, 8)) {
      items.push({
        id: `o-${o.id}`,
        label: 'Order',
        text: `${o.transactionType} ${o.symbol} · ${o.status}`,
        at: o.createdAt,
      });
    }

    for (const t of history.slice(0, 5)) {
      const pnlText = t.pnl != null ? ` · P&L ₹${t.pnl.toFixed(2)}` : '';
      items.push({
        id: `t-${t.id}`,
        label: t.status === 'CLOSED' ? 'Closed' : 'Trade',
        text: `${t.underlying} ${t.direction}${pnlText}`,
        at: t.closedAt ?? t.openedAt ?? t.createdAt,
      });
    }

    return items
      .sort((a, b) => Date.parse(b.at) - Date.parse(a.at))
      .slice(0, 12);
  }
}
