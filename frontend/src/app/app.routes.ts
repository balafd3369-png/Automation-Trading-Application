import { Routes } from '@angular/router';

import { EmptyRouteComponent } from './empty-route.component';

import { adminGuard, authGuard } from './core/auth.guard';

const protectedShell = { canActivate: [authGuard] };

export const routes: Routes = [
  {
    path: 'login',
    loadComponent: () => import('./auth/login-page.component').then((m) => m.LoginPageComponent),
  },
  {
    path: 'dashboard',
    loadComponent: () =>
      import('./dashboard/user-dashboard.component').then((m) => m.UserDashboardComponent),
    canActivate: [authGuard],
  },
  {
    path: 'admin/open-vs-zones',
    loadComponent: () =>
      import('./dashboard/admin-open-vs-zones-page.component').then(
        (m) => m.AdminOpenVsZonesPageComponent,
      ),
    canActivate: [adminGuard],
  },
  {
    path: 'admin',
    loadComponent: () =>
      import('./dashboard/admin-dashboard.component').then((m) => m.AdminDashboardComponent),
    canActivate: [adminGuard],
  },
  {
    path: 'admin/monitoring',
    loadComponent: () =>
      import('./monitoring/monitoring-shell.component').then((m) => m.MonitoringShellComponent),
    canActivate: [adminGuard],
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'overview' },
      {
        path: 'overview',
        loadComponent: () =>
          import('./monitoring/monitoring-overview.component').then((m) => m.MonitoringOverviewComponent),
      },
      {
        path: 'components',
        loadComponent: () =>
          import('./monitoring/monitoring-components.component').then((m) => m.MonitoringComponentsComponent),
      },
      {
        path: 'incidents',
        loadComponent: () =>
          import('./monitoring/monitoring-incidents.component').then((m) => m.MonitoringIncidentsComponent),
      },
      {
        path: 'incidents/:id',
        loadComponent: () =>
          import('./monitoring/monitoring-incident-detail.component').then(
            (m) => m.MonitoringIncidentDetailComponent,
          ),
      },
      {
        path: 'notifications',
        loadComponent: () =>
          import('./monitoring/monitoring-notifications.component').then(
            (m) => m.MonitoringNotificationsComponent,
          ),
      },
      {
        path: 'settings',
        loadComponent: () =>
          import('./monitoring/monitoring-settings.component').then((m) => m.MonitoringSettingsComponent),
      },
      {
        path: 'escalation',
        loadComponent: () =>
          import('./monitoring/monitoring-escalation.component').then((m) => m.MonitoringEscalationComponent),
      },
      {
        path: 'analytics',
        loadComponent: () =>
          import('./monitoring/monitoring-analytics.component').then((m) => m.MonitoringAnalyticsComponent),
      },
      {
        path: 'reliability',
        loadComponent: () =>
          import('./monitoring/monitoring-reliability.component').then((m) => m.MonitoringReliabilityComponent),
      },
      {
        path: 'instances',
        loadComponent: () =>
          import('./monitoring/monitoring-instances.component').then((m) => m.MonitoringInstancesComponent),
      },
      {
        path: 'coordination-workers',
        loadComponent: () =>
          import('./monitoring/monitoring-coordination-workers.component').then(
            (m) => m.MonitoringCoordinationWorkersComponent,
          ),
      },
      {
        path: 'locks',
        loadComponent: () =>
          import('./monitoring/monitoring-locks.component').then((m) => m.MonitoringLocksComponent),
      },
      {
        path: 'recovery',
        loadComponent: () =>
          import('./monitoring/monitoring-recovery.component').then((m) => m.MonitoringRecoveryComponent),
      },
      {
        path: 'production-safety',
        loadComponent: () =>
          import('./monitoring/monitoring-production-safety.component').then(
            (m) => m.MonitoringProductionSafetyComponent,
          ),
      },
      {
        path: 'trading-safety',
        loadComponent: () =>
          import('./monitoring/monitoring-trading-safety.component').then(
            (m) => m.MonitoringTradingSafetyComponent,
          ),
      },
    ],
  },
  { path: '', pathMatch: 'full', redirectTo: 'market' },
  { path: 'market/index/:symbol', component: EmptyRouteComponent, ...protectedShell },
  { path: 'market/options/:underlying', component: EmptyRouteComponent, ...protectedShell },
  { path: 'market/options', component: EmptyRouteComponent, ...protectedShell },
  { path: 'market', component: EmptyRouteComponent, ...protectedShell },
  {
    path: 'market/select-stock',
    loadComponent: () =>
      import('./dashboard/select-stock-page.component').then((m) => m.SelectStockPageComponent),
    ...protectedShell,
  },
  { path: 'strategies', component: EmptyRouteComponent, ...protectedShell },
  { path: 'strategies/:strategyId', component: EmptyRouteComponent, ...protectedShell },
  { path: 'intelligence', component: EmptyRouteComponent, ...protectedShell },
  { path: 'manual', component: EmptyRouteComponent, ...protectedShell },
  { path: 'operations/:tab', component: EmptyRouteComponent, ...protectedShell },
  { path: 'trades/:tab', component: EmptyRouteComponent, ...protectedShell },
  { path: '**', redirectTo: 'market' },
];
