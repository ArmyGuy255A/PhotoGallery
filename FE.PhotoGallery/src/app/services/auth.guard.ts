import { Injectable, inject } from '@angular/core';
import {
  Router,
  CanActivateFn,
  ActivatedRouteSnapshot,
  RouterStateSnapshot
} from '@angular/router';
import { AuthService } from './auth.service';

/**
 * Route guard that requires authentication
 */
export const authGuard: CanActivateFn = (
  route: ActivatedRouteSnapshot,
  state: RouterStateSnapshot
) => {
  const router = inject(Router);
  const authService = inject(AuthService);
  
  const isAuthenticated = authService.isAuthenticatedSync();

  if (!isAuthenticated) {
    // Redirect to login instead of calling loginWithGoogle
    router.navigate(['/login']);
    return false;
  }

  // Check for required roles
  const requiredRoles = route.data['roles'] as string[];
  if (requiredRoles && requiredRoles.length > 0) {
    const hasRole = requiredRoles.some(role => authService.hasRole(role));
    if (!hasRole) {
      router.navigate(['/unauthorized']);
      return false;
    }
  }

  return true;
};

/**
 * Route guard that checks for admin role
 */
export const adminGuard: CanActivateFn = (
  route: ActivatedRouteSnapshot,
  state: RouterStateSnapshot
) => {
  const router = inject(Router);
  const authService = inject(AuthService);
  
  if (!authService.isAdmin()) {
    router.navigate(['/unauthorized']);
    return false;
  }

  return true;
};

/**
 * Route guard that allows Admin OR AlbumCreator. Used for /albums/create
 * and /albums/:id/edit so the new AlbumCreator role can actually reach
 * the screens it has permission to use.
 */
export const albumCreatorGuard: CanActivateFn = (
  route: ActivatedRouteSnapshot,
  state: RouterStateSnapshot
) => {
  const router = inject(Router);
  const authService = inject(AuthService);

  if (!authService.canCreateAlbums()) {
    router.navigate(['/unauthorized']);
    return false;
  }
  return true;
};
