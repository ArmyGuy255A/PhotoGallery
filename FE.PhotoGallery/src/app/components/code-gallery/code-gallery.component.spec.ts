import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { By } from '@angular/platform-browser';
import { BehaviorSubject, of } from 'rxjs';

import { CodeGalleryComponent } from './code-gallery.component';
import { CartPanelComponent } from './cart-panel.component';
import { AuthService, User } from '../../services/auth.service';
import { CartService, CartItem, CartQuality } from '../../services/cart.service';
import { environment } from '../../../environments/environment';

class AuthServiceStub {
  authenticated = false;
  currentUserSubject = new BehaviorSubject<User | null>(null);
  currentUser$ = this.currentUserSubject.asObservable();
  isAuthenticatedSync(): boolean {
    return this.authenticated;
  }
}

class CartServiceStub {
  cart$ = new BehaviorSubject<CartItem[]>([]);
  count = 0;
  items: CartItem[] = [];
  loadForCode(_code: string): void { /* no-op */ }
  contains(_id: string, _q: CartQuality): boolean { return false; }
  addItem(_item: CartItem): boolean { return true; }
  addItems(items: CartItem[]): number { return items.length; }
  removeItem(_id: string, _q: CartQuality): void { /* no-op */ }
  updateQuality(_id: string, _o: CartQuality, _n: CartQuality): void { /* no-op */ }
  clear = jasmine.createSpy('clear');
}

describe('CodeGalleryComponent', () => {
  let authStub: AuthServiceStub;

  async function createComponent(authenticated: boolean): Promise<ComponentFixture<CodeGalleryComponent>> {
    authStub = new AuthServiceStub();
    authStub.authenticated = authenticated;

    await TestBed.configureTestingModule({
      imports: [CodeGalleryComponent, HttpClientTestingModule],
      providers: [
        provideRouter([]),
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

  it('shows the Back to Dashboard link when authenticated', async () => {
    const fixture = await createComponent(true);
    const link = fixture.debugElement.query(By.css('[data-testid="back-to-dashboard"]'));
    expect(link).toBeTruthy();
    expect(link.attributes['ng-reflect-router-link']).toBe('/dashboard');
  });

  it('hides the Back to Dashboard link when unauthenticated', async () => {
    const fixture = await createComponent(false);
    const link = fixture.debugElement.query(By.css('[data-testid="back-to-dashboard"]'));
    expect(link).toBeNull();
  });

  describe('toolbar / per-photo quality interaction', () => {
    let fixture: ComponentFixture<CodeGalleryComponent>;
    let component: CodeGalleryComponent;
    let httpMock: HttpTestingController;

    beforeEach(async () => {
      authStub = new AuthServiceStub();
      authStub.authenticated = false;

      await TestBed.configureTestingModule({
        imports: [CodeGalleryComponent, HttpClientTestingModule],
        providers: [
          { provide: AuthService, useValue: authStub },
          { provide: CartService, useClass: CartServiceStub },
          { provide: ActivatedRoute, useValue: { params: of({ code: 'TESTCODE' }) } }
        ]
      }).compileComponents();

      fixture = TestBed.createComponent(CodeGalleryComponent);
      component = fixture.componentInstance;
      httpMock = TestBed.inject(HttpTestingController);
      fixture.detectChanges();

      const apiBase = environment.apiUrl || '';
      // Album metadata
      httpMock.expectOne(`${apiBase}/api/code/TESTCODE/validate`).flush({
        albumId: 'a1',
        albumTitle: 'Test',
        albumDescription: '',
        isValid: true
      });
      // Photos
      httpMock.expectOne(`${apiBase}/api/code/TESTCODE/photos`).flush({
        photos: [
          { photoId: 'p1', fileName: 'a.jpg', uploadDate: '2025-01-01' },
          { photoId: 'p2', fileName: 'b.jpg', uploadDate: '2025-01-01' }
        ],
        totalCount: 2,
        page: 1,
        pageSize: 50,
        hasMore: false
      });
      fixture.detectChanges();
    });

    afterEach(() => {
      httpMock.verify();
    });

    it('toolbar default-quality select includes Original option', () => {
      const select = fixture.debugElement.query(
        By.css('.default-quality select')
      ).nativeElement as HTMLSelectElement;
      const options = Array.from(select.options).map(o => o.value);
      expect(options).toContain('Original');
      expect(options).toEqual(['Low', 'Medium', 'High', 'Original']);
    });

    it('seeds per-photo selectedQuality from defaultQuality on photo load', () => {
      expect(component.selectedQuality['p1']).toBe(component.defaultQuality);
      expect(component.selectedQuality['p2']).toBe(component.defaultQuality);
    });

    it('changing toolbar default updates non-overridden photos but preserves user overrides', () => {
      // User overrides p1 explicitly to High
      component.onPhotoQualityChange('p1', 'High');
      // Toolbar default changes to Low
      component.onDefaultQualityChange('Low');

      expect(component.selectedQuality['p1']).toBe('High'); // override preserved
      expect(component.selectedQuality['p2']).toBe('Low');  // followed default
    });

    it('per-photo dropdown bound value matches what onAddToCart will use', () => {
      const cart = TestBed.inject(CartService) as unknown as CartServiceStub;
      const addSpy = spyOn(cart, 'addItem').and.returnValue(true);

      component.onAddToCart({ photoId: 'p1', fileName: 'a.jpg', uploadDate: '' });

      expect(addSpy).toHaveBeenCalled();
      const arg = addSpy.calls.mostRecent().args[0] as CartItem;
      expect(arg.quality).toBe(component.selectedQuality['p1']);
      expect(arg.quality).toBe(component.defaultQuality);
    });

    it('renders new button labels: Add All to Cart / Remove All from Cart', () => {
      const btn = fixture.debugElement.query(By.css('.select-all-btn')).nativeElement as HTMLButtonElement;
      expect(btn.textContent?.trim()).toBe('Add All to Cart');

      // Force allVisibleInCart = true by spying on the getter
      Object.defineProperty(component, 'allVisibleInCart', { get: () => true, configurable: true });
      fixture.detectChanges();
      const btn2 = fixture.debugElement.query(By.css('.select-all-btn')).nativeElement as HTMLButtonElement;
      expect(btn2.textContent?.trim()).toBe('Remove All from Cart');
    });
  });
});

describe('CartPanelComponent.onDownload', () => {
  let cart: CartService;
  let fixture: ComponentFixture<CartPanelComponent>;
  let component: CartPanelComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [CartPanelComponent, HttpClientTestingModule],
      providers: [CartService]
    }).compileComponents();

    cart = TestBed.inject(CartService);
    fixture = TestBed.createComponent(CartPanelComponent);
    component = fixture.componentInstance;
    component.code = 'CODE';
    fixture.detectChanges();

    // Seed cart
    cart.loadForCode('CODE');
    cart.clear();
    cart.addItem({ photoId: 'p1', fileName: 'a.jpg', quality: 'Medium' });
    cart.addItem({ photoId: 'p2', fileName: 'b.jpg', quality: 'High' });
    fixture.detectChanges();
  });

  afterEach(() => {
    cart.clear();
  });

  it('clears cart after successful download (HTTP 200 Blob)', async () => {
    const blob = new Blob(['zip-bytes'], { type: 'application/zip' });
    const response = new Response(blob, {
      status: 200,
      headers: { 'Content-Disposition': 'attachment; filename="photos.zip"' }
    });
    spyOn(window, 'fetch').and.returnValue(Promise.resolve(response));
    spyOn(URL, 'createObjectURL').and.returnValue('blob:mock');
    spyOn(URL, 'revokeObjectURL');
    const clearSpy = spyOn(cart, 'clear').and.callThrough();

    expect(component.items.length).toBe(2);

    await component.onDownload();

    expect(clearSpy).toHaveBeenCalled();
    expect(component.items.length).toBe(0);
    expect(component.errorMessage).toBe('');
  });

  it('does NOT clear cart on failed download (non-2xx)', async () => {
    const response = new Response('boom', { status: 500 });
    spyOn(window, 'fetch').and.returnValue(Promise.resolve(response));
    const clearSpy = spyOn(cart, 'clear').and.callThrough();

    await component.onDownload();

    expect(clearSpy).not.toHaveBeenCalled();
    expect(component.items.length).toBe(2);
    expect(component.errorMessage).toBeTruthy();
  });
});
