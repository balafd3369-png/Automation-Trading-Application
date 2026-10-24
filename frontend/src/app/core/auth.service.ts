import { HttpClient } from '@angular/common/http';

import { Injectable, inject } from '@angular/core';

import { BehaviorSubject, Observable, catchError, map, of, tap } from 'rxjs';

import { AuthConfigResponse } from './user-dashboard.models';



export interface AuthUser {

  id: string;

  email: string;

  displayName: string;

  role: string;

}



export interface AuthSession {

  token: string;

  expiresAt: string;

  user: AuthUser;

}



const TOKEN_KEY = 'twb-session-token';



@Injectable({ providedIn: 'root' })

export class AuthService {

  private readonly http = inject(HttpClient);

  private readonly userSubject = new BehaviorSubject<AuthUser | null>(null);

  readonly user$ = this.userSubject.asObservable();



  constructor() {

    const token = this.token;

    if (token) {

      this.refreshMe().subscribe();

    }

  }



  get user(): AuthUser | null {

    return this.userSubject.value;

  }



  get token(): string | null {

    return localStorage.getItem(TOKEN_KEY);

  }



  get isAuthenticated(): boolean {

    return !!this.token && !!this.userSubject.value;

  }



  get isAdmin(): boolean {

    return this.userSubject.value?.role === 'Admin';

  }



  loadServerConfig(): Observable<AuthConfigResponse> {

    return this.http.get<AuthConfigResponse>('/api/auth/config').pipe(

      catchError(() =>

        of({

          requireAuthentication: true,

          allowAnonymousLocalUser: false,

        }),

      ),

    );

  }



  ensureSession(): Observable<boolean> {

    if (!this.token) {

      return of(false);

    }

    if (this.userSubject.value) {

      return of(true);

    }

    return this.refreshMe().pipe(map((user) => user !== null));

  }



  login(email: string, password: string): Observable<AuthSession> {

    return this.http.post<AuthSession>('/api/auth/login', { email, password }).pipe(

      tap((session) => this.persist(session)),

    );

  }



  register(email: string, password: string, displayName: string): Observable<AuthSession> {

    return this.http

      .post<AuthSession>('/api/auth/register', { email, password, displayName })

      .pipe(tap((session) => this.persist(session)));

  }



  changePassword(currentPassword: string, newPassword: string): Observable<{ message: string }> {
    return this.http.post<{ message: string }>('/api/auth/change-password', {
      currentPassword,
      newPassword,
    });
  }

  logout(): Observable<unknown> {
    if (!this.token) {
      this.clearSession();
      return of(null);
    }

    return this.http.post('/api/auth/logout', {}).pipe(
      tap(() => this.clearSession()),
      catchError(() => {
        this.clearSession();
        return of(null);
      }),
    );
  }



  homeRouteForUser(user: AuthUser): string {
    return user.role === 'Admin' ? '/admin' : '/market';
  }



  private refreshMe(): Observable<AuthUser | null> {

    return this.http.get<AuthUser>('/api/auth/me').pipe(

      tap((user) => this.userSubject.next(user)),

      catchError(() => {

        this.clearSession();

        return of(null);

      }),

    );

  }



  private persist(session: AuthSession): void {

    localStorage.setItem(TOKEN_KEY, session.token);

    this.userSubject.next(session.user);

  }



  private clearSession(): void {

    localStorage.removeItem(TOKEN_KEY);

    this.userSubject.next(null);

  }

}


