import { Routes } from '@angular/router';
import { EmptyRouteComponent } from './empty-route.component';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'market' },
  { path: 'market', component: EmptyRouteComponent },
  { path: 'market/options', component: EmptyRouteComponent },
  { path: 'strategies', component: EmptyRouteComponent },
  { path: 'intelligence', component: EmptyRouteComponent },
  { path: 'trades/:tab', component: EmptyRouteComponent },
  { path: '**', redirectTo: 'market' },
];
