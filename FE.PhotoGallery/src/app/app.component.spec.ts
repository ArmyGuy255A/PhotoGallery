import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { of, throwError } from 'rxjs';

import { AppComponent } from './app.component';
import { AuthService } from './services/auth.service';

describe('AppComponent', () => {
  let routerNavigate: jasmine.Spy;
  let getCurrentUserSpy: jasmine.Spy;
  let routerStub: { navigate: jasmine.Spy; url: string };
  let authStub: { getCurrentUser: jasmine.Spy };

  async function setup(url: string, authResult: 'user' | 'error') {
    routerNavigate = jasmine.createSpy('navigate');
    routerStub = { navigate: routerNavigate, url };
    getCurrentUserSpy = jasmine.createSpy('getCurrentUser').and.returnValue(
      authResult === 'user'
        ? of({ email: 'a@b.com' })
        : throwError(() => new Error('unauthorized'))
    );
    authStub = { getCurrentUser: getCurrentUserSpy };

    await TestBed.configureTestingModule({
      imports: [AppComponent],
      providers: [
        { provide: Router, useValue: routerStub },
        { provide: AuthService, useValue: authStub },
      ],
    }).compileComponents();

    // Patch window.location.pathname for the URL under test
    spyOnProperty(window, 'location', 'get').and.returnValue({
      ...window.location,
      pathname: url,
    } as Location);

    const fixture = TestBed.createComponent(AppComponent);
    fixture.componentInstance.ngOnInit();
    return fixture.componentInstance;
  }

  it('should create the app', async () => {
    await setup('/', 'error');
    const fixture = TestBed.createComponent(AppComponent);
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('redirects from / to /dashboard when authenticated', async () => {
    await setup('/', 'user');
    expect(routerNavigate).toHaveBeenCalledWith(['/dashboard']);
  });

  it('does NOT redirect from /login when authenticated', async () => {
    await setup('/login', 'user');
    expect(routerNavigate).not.toHaveBeenCalled();
  });

  it('does NOT redirect from /login when unauthenticated', async () => {
    await setup('/login', 'error');
    expect(routerNavigate).not.toHaveBeenCalled();
  });

  it('does NOT redirect from /code/ABC123 when authenticated', async () => {
    await setup('/code/ABC123', 'user');
    expect(routerNavigate).not.toHaveBeenCalled();
  });

  it('does NOT redirect from /code/ABC123 when unauthenticated', async () => {
    await setup('/code/ABC123', 'error');
    expect(routerNavigate).not.toHaveBeenCalled();
  });

  it('redirects to /login from a private route when unauthenticated', async () => {
    await setup('/dashboard', 'error');
    expect(routerNavigate).toHaveBeenCalledWith(['/login']);
  });

  it('does NOT redirect from a private route to /dashboard when already there and authenticated', async () => {
    await setup('/dashboard', 'user');
    expect(routerNavigate).not.toHaveBeenCalled();
  });
});
