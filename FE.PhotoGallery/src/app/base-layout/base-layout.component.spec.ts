import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, RouterOutlet } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { By } from '@angular/platform-browser';
import { BehaviorSubject } from 'rxjs';

import { BaseLayoutComponent } from './base-layout.component';
import { AuthService, User } from '../services/auth.service';

class AuthServiceStub {
  currentUserSubject = new BehaviorSubject<User | null>(null);
  currentUser$ = this.currentUserSubject.asObservable();
  isAuthenticatedSync(): boolean {
    return false;
  }
  isAdmin(): boolean {
    return false;
  }
}

describe('BaseLayoutComponent', () => {
  let component: BaseLayoutComponent;
  let fixture: ComponentFixture<BaseLayoutComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [BaseLayoutComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AuthService, useClass: AuthServiceStub }
      ]
    })
    .compileComponents();

    fixture = TestBed.createComponent(BaseLayoutComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('renders the global navbar, sidenav, and footer', () => {
    expect(fixture.debugElement.query(By.css('app-navbar'))).toBeTruthy();
    expect(fixture.debugElement.query(By.css('app-sidenav'))).toBeTruthy();
    expect(fixture.debugElement.query(By.css('app-footer'))).toBeTruthy();
  });

  it('mounts the global cart drawer inside .main-container', () => {
    const container = fixture.debugElement.query(By.css('.main-container'));
    expect(container).toBeTruthy();
    expect(container.query(By.css('app-cart-drawer'))).toBeTruthy();
  });


  it('hosts a <router-outlet/> (provided by app-sidenav) for child routes', () => {
    // BaseLayoutComponent delegates the routed content slot to <app-sidenav>'s
    // <mat-sidenav-content><router-outlet/></mat-sidenav-content>. Confirm
    // exactly one outlet exists in the rendered shell.
    const outlets = fixture.debugElement.queryAll(By.directive(RouterOutlet));
    expect(outlets.length).toBe(1);
  });
});
