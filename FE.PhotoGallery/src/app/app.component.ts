import { Component, OnInit } from '@angular/core';
import { Router, RouterOutlet } from '@angular/router';
import { AuthService } from './services/auth.service';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet],
  templateUrl: './app.component.html',
  styleUrl: './app.component.scss'
})
export class AppComponent implements OnInit {
  title = 'FE.PhotoGallery';

  constructor(private authService: AuthService, private router: Router) {}

  ngOnInit(): void {
    // With the GIS popup flow, no startup /me roundtrip is required —
    // tokens live in localStorage. Just refresh in-memory auth state and
    // route based on what we already have.
    void this.authService.updateAuthenticationState();
    if (this.authService.isAuthenticatedSync()) {
      // Already signed in; landing on root forwards to dashboard.
      // Deep links (e.g. /code/:code, /albums/:id) flow through the
      // router and are *not* clobbered by this no-op.
      return;
    }
    if (!this.isPublicRoute()) {
      this.router.navigate(['/login']);
    }
  }

  private currentUrl(): string {
    // Router.url may not yet reflect a navigation in progress at bootstrap;
    // fall back to window.location.pathname when available.
    const routerUrl = this.router.url || '';
    if (routerUrl && routerUrl !== '/') {
      return routerUrl;
    }
    if (typeof window !== 'undefined' && window.location) {
      return window.location.pathname || routerUrl || '/';
    }
    return routerUrl || '/';
  }

  private isPublicRoute(): boolean {
    const url = this.currentUrl().split('?')[0].split('#')[0];
    return url === '/login' || url.startsWith('/code/') || url === '/code';
  }

  private isRootRoute(): boolean {
    const url = this.currentUrl().split('?')[0].split('#')[0];
    return url === '/' || url === '' || url === '/index.html';
  }
}
