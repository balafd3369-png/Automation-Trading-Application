import { Component } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { MONITORING_NAV } from '../core/system-monitoring.util';

@Component({
  selector: 'app-monitoring-shell',
  standalone: true,
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  templateUrl: './monitoring-shell.component.html',
  styleUrl: './monitoring-shell.component.scss',
})
export class MonitoringShellComponent {
  readonly nav = MONITORING_NAV;
}