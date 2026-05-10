import { Component, ElementRef, HostListener, OnDestroy, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { Subject } from 'rxjs';
import { takeUntil } from 'rxjs/operators';
import { AuthService, User } from '../../services/auth.service';
import { GravatarService } from '../../services/gravatar.service';

/**
 * User profile dropdown showing avatar (Gravatar with initials fallback) and a
 * menu with account/shared-albums links and a sign-out action.
 */
@Component({
  selector: 'app-user-dropdown',
  standalone: true,
  imports: [CommonModule, RouterLink],
  template: `
    <div class="user-dropdown" *ngIf="currentUser">
      <button
        type="button"
        class="avatar-button"
        data-testid="user-avatar-button"
        [attr.aria-expanded]="isOpen"
        aria-haspopup="menu"
        [attr.aria-label]="'User menu for ' + displayName"
        (click)="toggle($event)"
      >
        <img
          *ngIf="!avatarFailed"
          class="avatar-img"
          [src]="avatarUrl"
          [alt]="displayName"
          (error)="onAvatarError()"
        />
        <span
          *ngIf="avatarFailed"
          class="avatar-initials"
          [style.background]="initialsBg"
          [attr.aria-hidden]="true"
        >{{ initials }}</span>
      </button>

      <div
        *ngIf="isOpen"
        class="dropdown-panel"
        data-testid="user-dropdown-panel"
        role="menu"
      >
        <div class="user-info">
          <div class="user-name">{{ displayName }}</div>
          <div class="user-email">{{ currentUser.email }}</div>
        </div>
        <div class="divider"></div>
        <a
          class="menu-item"
          routerLink="/account"
          data-testid="user-dropdown-account"
          role="menuitem"
          (click)="close()"
        >Account Settings</a>
        <a
          class="menu-item"
          routerLink="/shared-albums"
          data-testid="user-dropdown-shared"
          role="menuitem"
          (click)="close()"
        >Shared Albums</a>
        <a
          *ngIf="isAdmin"
          class="menu-item"
          routerLink="/admin/settings"
          data-testid="user-dropdown-admin-settings"
          role="menuitem"
          (click)="close()"
        >Admin Settings</a>
        <div class="divider"></div>
        <button
          type="button"
          class="menu-item signout"
          data-testid="user-dropdown-signout"
          role="menuitem"
          (click)="signOut()"
        >Sign out</button>
      </div>
    </div>
  `,
  styles: [`
    .user-dropdown {
      position: relative;
      display: inline-block;
    }

    .avatar-button {
      width: 40px;
      height: 40px;
      padding: 0;
      border: none;
      background: transparent;
      border-radius: 50%;
      cursor: pointer;
      overflow: hidden;
      display: inline-flex;
      align-items: center;
      justify-content: center;
      transition: box-shadow 0.2s;
    }

    .avatar-button:hover,
    .avatar-button:focus-visible {
      box-shadow: 0 0 0 2px rgba(52, 152, 219, 0.5);
      outline: none;
    }

    .avatar-img {
      width: 100%;
      height: 100%;
      object-fit: cover;
      display: block;
    }

    .avatar-initials {
      width: 100%;
      height: 100%;
      display: flex;
      align-items: center;
      justify-content: center;
      color: #fff;
      font-size: 14px;
      font-weight: 600;
      letter-spacing: 0.5px;
      text-transform: uppercase;
      user-select: none;
    }

    .dropdown-panel {
      position: absolute;
      top: calc(100% + 8px);
      right: 0;
      width: 240px;
      max-width: calc(100vw - 16px);
      background: #fff;
      border-radius: 8px;
      box-shadow: 0 4px 16px rgba(0, 0, 0, 0.12), 0 0 0 1px rgba(0, 0, 0, 0.04);
      padding: 8px 0;
      z-index: 1000;
    }

    .user-info {
      padding: 12px 16px 8px;
    }

    .user-name {
      font-weight: 600;
      color: #222;
      font-size: 14px;
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }

    .user-email {
      font-size: 12px;
      color: #777;
      margin-top: 2px;
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }

    .divider {
      height: 1px;
      background: #eee;
      margin: 4px 0;
    }

    .menu-item {
      display: block;
      width: 100%;
      padding: 10px 16px;
      font-size: 14px;
      color: #333;
      text-decoration: none;
      background: transparent;
      border: none;
      text-align: left;
      cursor: pointer;
      font-family: inherit;
    }

    .menu-item:hover,
    .menu-item:focus-visible {
      background: #f5f7fa;
      outline: none;
    }

    .menu-item.signout {
      color: #c0392b;
    }

    .menu-item.signout:hover,
    .menu-item.signout:focus-visible {
      background: #fdecea;
    }

    @media (max-width: 360px) {
      .dropdown-panel {
        width: 220px;
      }
    }
  `]
})
export class UserDropdownComponent implements OnInit, OnDestroy {
  isOpen = false;
  currentUser: User | null = null;
  avatarUrl = '';
  avatarFailed = false;
  initials = '';
  initialsBg = '#888';

  private destroy$ = new Subject<void>();

  constructor(
    private authService: AuthService,
    private gravatarService: GravatarService,
    private elementRef: ElementRef<HTMLElement>
  ) {}

  ngOnInit(): void {
    this.authService.currentUser$
      .pipe(takeUntil(this.destroy$))
      .subscribe(user => {
        this.currentUser = user;
        this.refreshAvatar();
      });
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  get displayName(): string {
    if (!this.currentUser) return '';
    const fn = this.currentUser.firstName?.trim() || '';
    const ln = this.currentUser.lastName?.trim() || '';
    const full = `${fn} ${ln}`.trim();
    return full || this.currentUser.email;
  }

  get isAdmin(): boolean {
    return Array.isArray(this.currentUser?.roles)
      && this.currentUser!.roles.includes('Admin');
  }

  toggle(event: MouseEvent): void {
    event.stopPropagation();
    this.isOpen = !this.isOpen;
  }

  close(): void {
    this.isOpen = false;
  }

  signOut(): void {
    this.close();
    this.authService.logout();
    window.location.href = '/login';
  }

  onAvatarError(): void {
    this.avatarFailed = true;
  }

  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent): void {
    if (!this.isOpen) return;
    const target = event.target as Node | null;
    if (target && !this.elementRef.nativeElement.contains(target)) {
      this.close();
    }
  }

  @HostListener('document:keydown.escape')
  onEscape(): void {
    if (this.isOpen) this.close();
  }

  private refreshAvatar(): void {
    if (!this.currentUser) {
      this.avatarUrl = '';
      this.initials = '';
      return;
    }
    this.avatarFailed = false;
    this.avatarUrl = this.gravatarService.getGravatarUrl(this.currentUser.email, 80);
    this.initials = this.gravatarService.getInitials(this.displayName);
    this.initialsBg = this.gravatarService.getInitialsBackgroundColor(
      this.currentUser.email || this.displayName
    );
  }
}
