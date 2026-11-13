import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  OnInit,
  inject,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { take } from 'rxjs';
import { AuthService } from '../core/auth.service';
import { environment } from '../../environments/environment';

@Component({
  selector: 'app-login-page',
  standalone: true,
  imports: [FormsModule],
  template: `
    <section class="login">
      <h1>Trade With Beast</h1>
      <p>Multi-user testing platform — shared market data, personal paper trading.</p>
      <p class="hint">
        This is your <strong>platform account</strong> (email + password below). It is not the m.Stock
        broker login in the top bar.
      </p>
      @if (showDevDefaults) {
        <p class="dev-defaults">
          Local default admin: <code>admin&#64;tradewithbeast.local</code> /
          <code>ChangeMe-Admin-123!</code> (unless you changed
          <code>SystemIdentity:AdminPassword</code> in backend user secrets).
        </p>
      }
      <form (ngSubmit)="submit()">
        <label>
          Email
          <input type="email" [(ngModel)]="email" name="email" required />
        </label>
        <label>
          Password
          <input type="password" [(ngModel)]="password" name="password" required />
        </label>
        <label>
          Display name (register only)
          <input [(ngModel)]="displayName" name="displayName" />
        </label>
        <div class="actions">
          <button type="submit" (click)="mode = 'login'">Login</button>
          <button type="button" (click)="register()">Register</button>
        </div>
        @if (message) {
          <p class="msg err">{{ message }}</p>
        }
      </form>
    </section>
  `,
  styles: `
    .login {
      max-width: 420px;
      margin: 3rem auto;
      padding: 1.5rem;
      color: #e8ecf4;
    }
    label {
      display: block;
      margin-bottom: 0.75rem;
      font-size: 0.85rem;
    }
    input {
      width: 100%;
      margin-top: 0.25rem;
      padding: 0.45rem;
    }
    .actions {
      display: flex;
      gap: 0.5rem;
      margin-top: 1rem;
    }
    .hint,
    .dev-defaults {
      font-size: 0.8rem;
      line-height: 1.35;
      color: #a8b4c8;
    }
    .dev-defaults code {
      font-size: 0.78rem;
    }
    .msg {
      margin-top: 1rem;
      color: #f0c674;
    }
    .msg.err {
      color: #f07178;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LoginPageComponent implements OnInit {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly cdr = inject(ChangeDetectorRef);

  readonly showDevDefaults = !environment.production;

  ngOnInit(): void {
    if (!environment.requireAuth) {
      return;
    }
    this.auth.ensureSession().pipe(take(1)).subscribe((ok) => {
      const user = this.auth.user;
      if (ok && user) {
        void this.router.navigateByUrl(this.auth.homeRouteForUser(user));
      }
    });
  }

  email = '';
  password = '';
  displayName = '';
  mode = 'login';
  message = '';

  submit(): void {
    this.message = '';
    this.auth.login(this.email, this.password).subscribe({
      next: (session) => void this.router.navigateByUrl(this.auth.homeRouteForUser(session.user)),
      error: (err) => {
        this.message = this.readAuthError(err, 'Login failed');
        this.cdr.markForCheck();
      },
    });
  }

  register(): void {
    this.message = '';
    this.auth.register(this.email, this.password, this.displayName || this.email).subscribe({
      next: (session) => void this.router.navigateByUrl(this.auth.homeRouteForUser(session.user)),
      error: (err) => {
        this.message = this.readAuthError(err, 'Registration failed');
        this.cdr.markForCheck();
      },
    });
  }

  private readAuthError(err: unknown, fallback: string): string {
    const http = err as { status?: number; error?: { message?: string } | string };
    if (http.status === 0) {
      return 'Cannot reach the API. Start the backend on port 45211, then try again.';
    }
    if (typeof http.error === 'string' && http.error.trim()) {
      return http.error;
    }
    if (http.error && typeof http.error === 'object' && http.error.message) {
      return http.error.message;
    }
    return fallback;
  }
}
