import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { map, of, switchMap, take } from 'rxjs';
import { environment } from '../../environments/environment';
import { AuthService } from './auth.service';

export const authGuard: CanActivateFn = () => {
  if (!environment.requireAuth) {
    return true;
  }

  const auth = inject(AuthService);
  const router = inject(Router);
  return auth.ensureSession().pipe(
    take(1),
    map((ok) => {
      if (ok) {
        return true;
      }
      return router.createUrlTree(['/login']);
    }),
  );
};

export const adminGuard: CanActivateFn = () => {
  if (!environment.requireAuth) {
    return true;
  }

  const auth = inject(AuthService);
  const router = inject(Router);
  return auth.ensureSession().pipe(
    take(1),
    switchMap((ok) => {
      if (!ok) {
        return of(router.createUrlTree(['/login']));
      }
      if (auth.isAdmin) {
        return of(true);
      }
      return of(router.createUrlTree(['/market']));
    }),
  );
};
