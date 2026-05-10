import { Injectable, inject } from '@angular/core';
import {
  HttpInterceptor,
  HttpInterceptorFn,
  HttpRequest,
  HttpHandler,
  HttpHandlerFn,
  HttpEvent
} from '@angular/common/http';
import { Observable } from 'rxjs';
import { AuthService, TokenType } from './auth.service';

/**
 * Functional HTTP interceptor (Angular 19 style) that automatically attaches
 * the AppToken JWT to outgoing requests.
 *
 * Wired in app.config.ts via provideHttpClient(withInterceptors([jwtInterceptor])).
 *
 * IMPORTANT: do NOT register this via the legacy HTTP_INTERCEPTORS DI token —
 * provideHttpClient() ignores that channel by default in Angular 19, which
 * silently drops the Authorization header and causes 401s on every protected
 * endpoint with no client-side error.
 */
export const jwtInterceptor: HttpInterceptorFn = (req, next) => {
  const authService = inject(AuthService);
  const token = authService.getToken(TokenType.AppToken);

  if (token) {
    req = req.clone({
      setHeaders: {
        Authorization: `Bearer ${token}`
      }
    });
  }

  return next(req);
};

/**
 * Legacy class-based interceptor kept for backwards compatibility with any
 * code that still references it. New wiring should use the functional
 * jwtInterceptor above. This class is NOT registered in app.config.ts.
 */
@Injectable()
export class JwtInterceptor implements HttpInterceptor {
  constructor(private authService: AuthService) {}

  intercept(
    req: HttpRequest<any>,
    next: HttpHandler
  ): Observable<HttpEvent<any>> {
    const token = this.authService.getToken(TokenType.AppToken);

    if (token) {
      req = req.clone({
        setHeaders: {
          Authorization: `Bearer ${token}`
        }
      });
    }

    return next.handle(req);
  }
}
