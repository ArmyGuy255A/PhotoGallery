import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { By } from '@angular/platform-browser';

import { SidenavComponent } from './sidenav.component';
import { AuthService } from '../../services/auth.service';

class AuthServiceStub {
  admin = false;
  isAdmin(): boolean { return this.admin; }
}

describe('SidenavComponent', () => {
  let component: SidenavComponent;
  let fixture: ComponentFixture<SidenavComponent>;
  let authStub: AuthServiceStub;

  async function setup(isAdmin: boolean): Promise<void> {
    authStub = new AuthServiceStub();
    authStub.admin = isAdmin;
    await TestBed.configureTestingModule({
      imports: [SidenavComponent],
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: authStub },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(SidenavComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  it('should create', async () => {
    await setup(false);
    expect(component).toBeTruthy();
  });

  it('renders Title-Case nav items in order (issue #105)', async () => {
    await setup(false);
    const labels = fixture.debugElement
      .queryAll(By.css('mat-nav-list a[mat-list-item]'))
      .map(el => (el.nativeElement.textContent ?? '').trim());
    // Admin entry hidden for non-admins.
    expect(labels).toEqual(['Dashboard', 'Shared Albums', 'Account Settings']);
  });

  it('shows Admin Settings only when the user is admin (issue #102)', async () => {
    await setup(true);
    const labels = fixture.debugElement
      .queryAll(By.css('mat-nav-list a[mat-list-item]'))
      .map(el => (el.nativeElement.textContent ?? '').trim());
    expect(labels).toEqual(['Dashboard', 'Shared Albums', 'Account Settings', 'Admin Settings']);
  });

  it('binds routerLink on every nav entry (issue #102)', async () => {
    await setup(true);
    const links = fixture.debugElement
      .queryAll(By.css('mat-nav-list a[mat-list-item]'))
      .map(el => el.nativeElement.getAttribute('href'));
    // Every entry has a real href (Angular RouterLink renders one).
    expect(links.every(h => typeof h === 'string' && h.length > 0)).toBeTrue();
  });
});
