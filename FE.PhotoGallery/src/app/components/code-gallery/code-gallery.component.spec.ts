import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute } from '@angular/router';
import { HttpClientTestingModule } from '@angular/common/http/testing';
import { By } from '@angular/platform-browser';
import { BehaviorSubject, of } from 'rxjs';

import { CodeGalleryComponent } from './code-gallery.component';
import { AuthService, User } from '../../services/auth.service';
import { CartService } from '../../services/cart.service';

class AuthServiceStub {
  authenticated = false;
  currentUserSubject = new BehaviorSubject<User | null>(null);
  currentUser$ = this.currentUserSubject.asObservable();
  isAuthenticatedSync(): boolean {
    return this.authenticated;
  }
}

class CartServiceStub {
  cart$ = new BehaviorSubject<any[]>([]);
  count = 0;
  loadForCode(_code: string): void {}
  contains(_id: string, _q: string): boolean { return false; }
  addItem(_item: any): boolean { return true; }
}

describe('CodeGalleryComponent', () => {
  let authStub: AuthServiceStub;

  async function createComponent(authenticated: boolean): Promise<ComponentFixture<CodeGalleryComponent>> {
    authStub = new AuthServiceStub();
    authStub.authenticated = authenticated;

    await TestBed.configureTestingModule({
      imports: [CodeGalleryComponent, HttpClientTestingModule],
      providers: [
        { provide: AuthService, useValue: authStub },
        { provide: CartService, useClass: CartServiceStub },
        { provide: ActivatedRoute, useValue: { params: of({}) } }
      ]
    }).compileComponents();

    const fixture = TestBed.createComponent(CodeGalleryComponent);
    fixture.detectChanges();
    return fixture;
  }

  it('renders <app-user-dropdown> when authenticated', async () => {
    const fixture = await createComponent(true);
    const dropdown = fixture.debugElement.query(By.css('app-user-dropdown'));
    expect(dropdown).toBeTruthy();
  });

  it('does not render <app-user-dropdown> when unauthenticated', async () => {
    const fixture = await createComponent(false);
    const dropdown = fixture.debugElement.query(By.css('app-user-dropdown'));
    expect(dropdown).toBeNull();
  });
});
