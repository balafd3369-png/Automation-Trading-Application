import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, ChangeDetectorRef, Component, DestroyRef, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { interval } from 'rxjs';

@Component({
  selector: 'app-live-clock',
  imports: [DatePipe],
  template: `<span class="title-meta">{{ now | date: 'HH:mm:ss' }}</span>`,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LiveClockComponent {
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly destroyRef = inject(DestroyRef);
  now = new Date();

  constructor() {
    interval(1000)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => {
        this.now = new Date();
        this.cdr.markForCheck();
      });
  }
}
