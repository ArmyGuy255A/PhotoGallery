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
        // Token is now stored in localStorage, navigate to dashboard
        this.router.navigate(['/dashboard']);
      },
      error: (error) => {
        console.log('App init: Failed to load user (redirecting to login)');
        // Navigate to login
        this.router.navigate(['/login']);
      },
      complete: () => {
        console.log('App init: Auth initialization complete');
      }
    });
  }
}
