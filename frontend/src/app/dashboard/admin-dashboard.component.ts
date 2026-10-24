import { DatePipe, DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, ChangeDetectorRef, Component, OnInit, inject } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { BoardService } from '../core/board.service';
import { AuthService } from '../core/auth.service';
import { AdminPlatformDashboard } from '../core/user-dashboard.models';

@Component({
  selector: 'app-admin-dashboard',
  standalone: true,
  imports: [DecimalPipe, DatePipe, RouterLink],
  template: `
    <section class="dash">
      <header>
        <h1>Admin platform</h1>
        <p>Operations overview for friend testing hosts.</p>
        <div class="actions">
          <a routerLink="/market">Market</a>
          <a routerLink="/operations/risk">Operations</a>
          <button type="button" (click)="logout()">Logout</button>
        </div>
      </header>
      @if (dash) {
        <div class="grid">
          <div class="card"><span>Total users</span><strong>{{ dash.totalUsers }}</strong></div>
          <div class="card"><span>Active users</span><strong>{{ dash.activeUsers }}</strong></div>
          <div class="card"><span>Online (15m)</span><strong>{{ dash.onlineUsers }}</strong></div>
          <div class="card"><span>Today's logins</span><strong>{{ dash.todaysLogins }}</strong></div>
          <div class="card"><span>Paper trades today</span><strong>{{ dash.paperTradesToday }}</strong></div>
          <div class="card"><span>Open paper positions</span><strong>{{ dash.openPaperPositions }}</strong></div>
          <div class="card"><span>Platform health</span><strong>{{ dash.brokerStatus }}</strong></div>
          <div class="card"><span>Broker</span><strong>{{ dash.brokerConnected ? 'Connected' : 'Disconnected' }}</strong></div>
        </div>
        <h2>Top traders</h2>
        <table>
          <thead>
            <tr>
              <th>User</th>
              <th>Email</th>
              <th>Trades</th>
              <th>Net P&amp;L</th>
            </tr>
          </thead>
          <tbody>
            @for (u of dash.topUsers; track u.userId) {
              <tr>
                <td>{{ u.displayName }}</td>
                <td>{{ u.email }}</td>
                <td>{{ u.tradeCount }}</td>
                <td>₹{{ u.netPnL | number: '1.2-2' }}</td>
              </tr>
            }
          </tbody>
        </table>
        <p class="meta">Updated {{ dash.updatedAt | date: 'medium' }}</p>
      } @else {
        <p>Loading admin dashboard…</p>
      }
    </section>
  `,
  styles: `
    .dash {
      min-height: 100vh;
      padding: 1.5rem 2rem;
      background: #0d1118;
      color: #e8ecf4;
    }
    .grid {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(160px, 1fr));
      gap: 0.75rem;
      margin-bottom: 1.25rem;
    }
    .card {
      background: #121820;
      border: 1px solid #2a3344;
      border-radius: 8px;
      padding: 0.75rem 1rem;
    }
    .card span {
      display: block;
      font-size: 0.8rem;
      opacity: 0.75;
    }
    table {
      width: 100%;
      border-collapse: collapse;
      font-size: 0.9rem;
    }
    th,
    td {
      border-bottom: 1px solid #2a3344;
      padding: 0.45rem 0.35rem;
      text-align: left;
    }
    .actions {
      display: flex;
      gap: 0.75rem;
      margin-top: 0.75rem;
    }
    a,
    button {
      color: #8ec8ff;
      background: transparent;
      border: 1px solid #3a4558;
      border-radius: 6px;
      padding: 0.35rem 0.65rem;
      cursor: pointer;
      text-decoration: none;
    }
    .meta {
      margin-top: 1rem;
      opacity: 0.7;
      font-size: 0.85rem;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdminDashboardComponent implements OnInit {
  private readonly board = inject(BoardService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly cdr = inject(ChangeDetectorRef);

  dash: AdminPlatformDashboard | null = null;

  ngOnInit(): void {
    this.board.adminDashboard().subscribe((d) => {
      this.dash = d;
      this.cdr.markForCheck();
    });
  }

  logout(): void {
    this.auth.logout().subscribe(() => void this.router.navigateByUrl('/login'));
  }
}
