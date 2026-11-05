import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

export const authGuard: CanActivateFn = (_, state) =>
  inject(AuthService).isAuthenticated() ||
  inject(Router).createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });

/** Pages limited to roles (route data `roles`). The API checks the same roles on every call. */
export const roleGuard: CanActivateFn = (route) => {
  const roles = route.data['roles'] as string[] | undefined;
  const role = inject(AuthService).user()?.role;
  return !roles || (!!role && roles.includes(role)) || inject(Router).createUrlTree(['/dashboard']);
};

export const guestGuard: CanActivateFn = () =>
  !inject(AuthService).isAuthenticated() || inject(Router).createUrlTree(['/dashboard']);
