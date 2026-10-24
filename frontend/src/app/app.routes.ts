import { Routes } from '@angular/router';
import { EmptyRouteComponent } from './empty-route.component';
import { LoginPageComponent } from './auth/login-page.component';
import { UserDashboardComponent } from './dashboard/user-dashboard.component';
import { AdminDashboardComponent } from './dashboard/admin-dashboard.component';
import { adminGuard, authGuard } from './core/auth.guard';

const protectedShell = { canActivate: [authGuard] };

export const routes: Routes = [
  { path: 'login', component: LoginPageComponent },
  { path: 'dashboard', component: UserDashboardComponent, canActivate: [authGuard] },
  { path: 'admin', component: AdminDashboardComponent, canActivate: [adminGuard] },
  { path: '', pathMatch: 'full', redirectTo: 'market' },
  { path: 'market', component: EmptyRouteComponent, ...protectedShell },
  { path: 'market/options', component: EmptyRouteComponent, ...protectedShell },
  { path: 'strategies', component: EmptyRouteComponent, ...protectedShell },
  { path: 'strategies/:strategyId', component: EmptyRouteComponent, ...protectedShell },
  { path: 'intelligence', component: EmptyRouteComponent, ...protectedShell },
  { path: 'manual', component: EmptyRouteComponent, ...protectedShell },
  { path: 'operations/:tab', component: EmptyRouteComponent, ...protectedShell },
  { path: 'trades/:tab', component: EmptyRouteComponent, ...protectedShell },
  { path: '**', redirectTo: 'market' },
];
