import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';

@Component({
  selector: 'app-watchlist-hover-row',
  standalone: true,
  templateUrl: './watchlist-hover-row.component.html',
  styleUrl: './watchlist-hover-row.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class WatchlistHoverRowComponent {
  label = input('');
  buy = output<void>();
  sell = output<void>();
  chart = output<void>();
}
