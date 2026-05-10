import { Component, OnDestroy, OnInit, inject } from '@angular/core';
import {GlobalStateService} from '../../services/global-state.service';
import {AuthService, User} from '../../services/auth.service';
import {MatIconModule} from '@angular/material/icon';
import {MatButtonModule} from '@angular/material/button';
import {MatToolbarModule} from '@angular/material/toolbar';
import {MatFormFieldModule} from '@angular/material/form-field';
import {MatInput} from '@angular/material/input';
import {CommonModule} from '@angular/common';
import {Router} from '@angular/router';
import {Subject} from 'rxjs';
import {takeUntil} from 'rxjs/operators';
import {ThemeComponent} from '../theme/theme.component';
import {UserDropdownComponent} from '../user-dropdown/user-dropdown.component';

@Component({
  selector: 'app-navbar',
  imports: [
    CommonModule, MatToolbarModule, MatButtonModule, MatIconModule, MatFormFieldModule, MatInput,
    ThemeComponent, UserDropdownComponent,
  ],
  templateUrl: './navbar.component.html',
  styleUrl: './navbar.component.scss'
})
export class NavbarComponent implements OnInit, OnDestroy {

  globalStateService: GlobalStateService = inject(GlobalStateService);
  private authService: AuthService = inject(AuthService);
  private router: Router = inject(Router);
  private destroy$ = new Subject<void>();

  isAuthenticated = false;

  ngOnInit(): void {
    // Seed synchronously so the right-zone control is correct on first paint.
    this.isAuthenticated = this.authService.isAuthenticatedSync();
    this.authService.currentUser$
      .pipe(takeUntil(this.destroy$))
      .subscribe((user: User | null) => {
        this.isAuthenticated = !!user;
      });
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  toggleSidebar() {
    this.globalStateService.toggleSidebar();
  }

  goToLogin() {
    this.router.navigate(['/login']);
  }
}
