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
  { path: '', pathMatch: 'full', redirectTo: 'market' },
  { path: 'market', component: EmptyRouteComponent, ...protectedShell },
  { path: 'market/options', component: EmptyRouteComponent, ...protectedShell },
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
