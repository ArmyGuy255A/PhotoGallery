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
    }
  }
}
