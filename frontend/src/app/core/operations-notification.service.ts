import { Injectable, inject } from '@angular/core';
import { BehaviorSubject, Observable, interval, startWith, switchMap, catchError, of } from 'rxjs';
import { BoardService } from './board.service';
import { OperationsNotification } from './operations.models';

export interface OperationsToast {
  id: string;
  severity: string;
  message: string;
  at: number;
}

@Injectable({ providedIn: 'root' })
export class OperationsNotificationService {
  private readonly board = inject(BoardService);
  private readonly toastsSubject = new BehaviorSubject<OperationsToast[]>([]);
  private seen = new Set<string>();

  readonly toasts$ = this.toastsSubject.asObservable();

  startPolling(): Observable<OperationsNotification[]> {
    return interval(12_000).pipe(
      startWith(0),
      switchMap(() => this.board.operationsNotifications(50, true).pipe(catchError(() => of([])))),
    );
  }

  ingest(notifications: OperationsNotification[]): void {
    const fresh = notifications.filter((n) => !this.seen.has(n.id));
    if (fresh.length === 0) {
      return;
    }
    fresh.forEach((n) => this.seen.add(n.id));
    const next = [
      ...fresh.map((n) => ({
        id: n.id,
        severity: n.severity,
        message: n.message,
        at: Date.now(),
      })),
      ...this.toastsSubject.value,
    ].slice(0, 6);
    this.toastsSubject.next(next);
  }

  dismissToast(id: string): void {
    this.toastsSubject.next(this.toastsSubject.value.filter((t) => t.id !== id));
    void this.board.markOperationsNotificationRead(id).subscribe();
  }
}
