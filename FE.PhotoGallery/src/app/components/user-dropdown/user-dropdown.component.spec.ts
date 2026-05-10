import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { BehaviorSubject } from 'rxjs';

import { UserDropdownComponent } from './user-dropdown.component';
import { AuthService, User } from '../../services/auth.service';
import { GravatarService } from '../../services/gravatar.service';

describe('UserDropdownComponent', () => {
  let fixture: ComponentFixture<UserDropdownComponent>;
  let component: UserDropdownComponent;
  let currentUser$: BehaviorSubject<User | null>;
  let authStub: Partial<AuthService>;

  const baseUser: User = {
    id: 'u1',
    email: 'someone@example.com',
    firstName: 'Some',
    lastName: 'One',
    roles: []
  };

  beforeEach(async () => {
    currentUser$ = new BehaviorSubject<User | null>(null);
    authStub = {
      currentUser$: currentUser$.asObservable(),
      logout: jasmine.createSpy('logout')
    };

    const gravatarStub: Partial<GravatarService> = {
      getGravatarUrl: () => 'https://example.test/avatar.png',
      getInitials: () => 'SO',
      getInitialsBackgroundColor: () => '#888'
    };

    await TestBed.configureTestingModule({
      imports: [UserDropdownComponent],
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: authStub },
        { provide: GravatarService, useValue: gravatarStub }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(UserDropdownComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  function openMenu(): void {
    const btn: HTMLButtonElement | null =
      fixture.nativeElement.querySelector('[data-testid="user-avatar-button"]');
    btn!.click();
    fixture.detectChanges();
  }

  function adminLink(): HTMLAnchorElement | null {
    return fixture.nativeElement.querySelector(
      '[data-testid="user-dropdown-admin-settings"]'
    );
  }

  it('creates the component', () => {
    expect(component).toBeTruthy();
  });

  describe('isAdmin getter', () => {
    it('is false when no user is signed in', () => {
      expect(component.isAdmin).toBeFalse();
    });

    it('is false when the user has no Admin role', () => {
      currentUser$.next({ ...baseUser, roles: ['User'] });
      expect(component.isAdmin).toBeFalse();
    });

    it('is true when the user has the Admin role', () => {
      currentUser$.next({ ...baseUser, roles: ['Admin'] });
      expect(component.isAdmin).toBeTrue();
    });

    it('is true when the user has Admin among other roles', () => {
      currentUser$.next({ ...baseUser, roles: ['User', 'Admin'] });
      expect(component.isAdmin).toBeTrue();
    });
  });

  describe('Admin Settings menu entry', () => {
    it('is hidden in the dropdown for non-admin users', () => {
      currentUser$.next({ ...baseUser, roles: ['User'] });
      fixture.detectChanges();
      openMenu();
      expect(adminLink()).toBeNull();
    });

    it('is shown in the dropdown for admin users', () => {
      currentUser$.next({ ...baseUser, roles: ['Admin'] });
      fixture.detectChanges();
      openMenu();
      const link = adminLink();
      expect(link).toBeTruthy();
      expect(link!.textContent?.trim()).toBe('Admin Settings');
      expect(link!.getAttribute('role')).toBe('menuitem');
      // RouterLink directive resolves href on the anchor.
      expect(link!.getAttribute('href')).toBe('/admin/settings');
    });

    it('clicking the Admin Settings entry closes the menu', () => {
      currentUser$.next({ ...baseUser, roles: ['Admin'] });
      fixture.detectChanges();
      openMenu();
      expect(component.isOpen).toBeTrue();

      adminLink()!.click();
      fixture.detectChanges();

      expect(component.isOpen).toBeFalse();
    });
  });
});
