import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';

import { AppComponent } from './app.component';
import { AuthService } from './services/auth.service';

describe('AppComponent', () => {
  let routerNavigate: jasmine.Spy;
  let isAuthenticatedSyncSpy: jasmine.Spy;
  let updateAuthenticationStateSpy: jasmine.Spy;
  let routerStub: { navigate: jasmine.Spy; url: string };
  let authStub: { isAuthenticatedSync: jasmine.Spy; updateAuthenticationState: jasmine.Spy };

  async function setup(url: string, authed: boolean) {
    routerNavigate = jasmine.createSpy('navigate');
    routerStub = { navigate: routerNavigate, url };
    isAuthenticatedSyncSpy = jasmine.createSpy('isAuthenticatedSync').and.returnValue(authed);
    updateAuthenticationStateSpy = jasmine
      .createSpy('updateAuthenticationState')
      .and.returnValue(Promise.resolve());
    authStub = {
      isAuthenticatedSync: isAuthenticatedSyncSpy,
      updateAuthenticationState: updateAuthenticationStateSpy,
    };

    await TestBed.configureTestingModule({
      imports: [AppComponent],
      providers: [
        { provide: Router, useValue: routerStub },
        { provide: AuthService, useValue: authStub },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(AppComponent);
    fixture.componentInstance.ngOnInit();
    return fixture.componentInstance;
  }

  it('should create the app', async () => {
    await setup('/', false);
    const fixture = TestBed.createComponent(AppComponent);
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('does NOT redirect from / when authenticated (router resolves "" → DashboardComponent)', async () => {
    await setup('/', true);
    expect(routerNavigate).not.toHaveBeenCalled();
  });

  it('does NOT redirect from /login when authenticated', async () => {
    await setup('/login', true);
    expect(routerNavigate).not.toHaveBeenCalled();
  });

  it('does NOT redirect from /login when unauthenticated (login is a public route)', async () => {
    await setup('/login', false);
    expect(routerNavigate).not.toHaveBeenCalled();
  });

  it('does NOT redirect from /code/ABC123 when authenticated', async () => {
    await setup('/code/ABC123', true);
    expect(routerNavigate).not.toHaveBeenCalled();
  });

  it('does NOT redirect from /code/ABC123 when unauthenticated (public route)', async () => {
    await setup('/code/ABC123', false);
    expect(routerNavigate).not.toHaveBeenCalled();
  });

  it('redirects to /login from a private route when unauthenticated', async () => {
    await setup('/dashboard', false);
    expect(routerNavigate).toHaveBeenCalledWith(['/login']);
  });

  it('does NOT redirect from a private route when already authenticated', async () => {
    await setup('/dashboard', true);
    expect(routerNavigate).not.toHaveBeenCalled();
  });
});
