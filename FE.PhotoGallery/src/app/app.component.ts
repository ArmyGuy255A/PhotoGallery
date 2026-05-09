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
    // Initialize auth state from backend on app startup
    // This populates the token and user in localStorage so routes work correctly
    console.log('App init: Attempting to load current user from backend');
    this.authService.getCurrentUser().subscribe({
      next: (user) => {
        console.log('App init: User authenticated successfully:', user.email);
        if (this.isPublicRoute()) {
          return;
        }
        if (this.isRootRoute()) {
          this.router.navigate(['/dashboard']);
        }
      },
      error: () => {
        console.log('App init: Failed to load user');
        if (this.isPublicRoute()) {
          return;
        }
        this.router.navigate(['/login']);
      },
      complete: () => {
        console.log('App init: Auth initialization complete');
      }
    });
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
