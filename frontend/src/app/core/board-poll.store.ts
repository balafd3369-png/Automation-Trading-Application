import { DestroyRef, Injectable, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import {
  Observable,
  Subject,
  catchError,
  exhaustMap,
  interval,
  map,
  of,
  share,
  startWith,
  switchMap,
} from 'rxjs';
import { environment } from '../../environments/environment';
import { BoardResponse } from './board.models';
import { BoardService } from './board.service';

export type BoardPollEvent =
  | { kind: 'data'; data: BoardResponse }
  | { kind: 'error'; message: string };

const BOARD_ERROR_MESSAGE =
  'Backend on port 45211 is not responding (board timed out). Run stop-app.ps1 then start-app.ps1, wait for the backend window, then refresh.';

@Injectable({ providedIn: 'root' })
export class BoardPollStore {
  private readonly board = inject(BoardService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly reload$ = new Subject<void>();
  private started = false;

  private readonly eventsInternal$ = this.reload$.pipe(
    startWith(undefined),
    switchMap(() =>
      interval(environment.boardPollMs).pipe(
        startWith(0),
        exhaustMap(() =>
          this.board.fetchBoard().pipe(
            map((data) => ({ kind: 'data' as const, data })),
            catchError(() => of({ kind: 'error' as const, message: BOARD_ERROR_MESSAGE })),
          ),
        ),
      ),
    ),
    share(),
  );

  readonly events$: Observable<BoardPollEvent> = this.eventsInternal$;

  start(): void {
    if (this.started) {
      return;
    }
    this.started = true;
    this.eventsInternal$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe();
  }

  retry(): void {
    this.reload$.next();
  }
}
