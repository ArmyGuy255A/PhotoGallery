import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { By } from '@angular/platform-browser';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { BehaviorSubject } from 'rxjs';

import { NavbarComponent } from './navbar.component';
import { AuthService, User } from '../../services/auth.service';

class AuthServiceStub {
  authenticated = false;
  currentUserSubject = new BehaviorSubject<User | null>(null);
  currentUser$ = this.currentUserSubject.asObservable();
  isAuthenticatedSync(): boolean {
    return this.authenticated;
  }
}

const FAKE_USER: User = {
  id: 'u-1',
  email: 'someone@example.com',
  firstName: 'Test',
  lastName: 'User',
  roles: ['User']
};

describe('NavbarComponent', () => {
  let component: NavbarComponent;
  let fixture: ComponentFixture<NavbarComponent>;
  let authStub: AuthServiceStub;
  let routerNavigate: jasmine.Spy;

  async function setup(authenticated: boolean): Promise<void> {
    authStub = new AuthServiceStub();
    authStub.authenticated = authenticated;
    authStub.currentUserSubject.next(authenticated ? FAKE_USER : null);
    routerNavigate = jasmine.createSpy('navigate');

    await TestBed.configureTestingModule({
      imports: [NavbarComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: authStub },
        { provide: Router, useValue: { navigate: routerNavigate } }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(NavbarComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  it('should create', async () => {
    await setup(false);
    expect(component).toBeTruthy();
  });

  it('renders the login icon (and not the user dropdown) when unauthenticated', async () => {
    await setup(false);
    expect(fixture.debugElement.query(By.css('app-user-dropdown'))).toBeNull();
    expect(fixture.debugElement.query(By.css('button.login-icon'))).toBeTruthy();
  });

  it('renders the user dropdown (and not the login icon) when authenticated', async () => {
    await setup(true);
    expect(fixture.debugElement.query(By.css('app-user-dropdown'))).toBeTruthy();
    expect(fixture.debugElement.query(By.css('button.login-icon'))).toBeNull();
  });

  it('navigates to /login when the login icon is clicked', async () => {
    await setup(false);
    const btn = fixture.debugElement.query(By.css('button.login-icon'));
    btn.triggerEventHandler('click', null);
    expect(routerNavigate).toHaveBeenCalledWith(['/login']);
  });
});
