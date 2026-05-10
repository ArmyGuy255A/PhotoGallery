import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { BehaviorSubject, Subject } from 'rxjs';

import { CartDrawerComponent } from './cart-drawer.component';
import { CartCapError, CartItem, CartService } from '../../services/cart.service';

class CartServiceStub {
  cartItems$ = new BehaviorSubject<CartItem[]>([]);
  cart$ = this.cartItems$.asObservable();
  cartDrawerOpen$ = new BehaviorSubject<boolean>(true);
  errors = new Subject<string>();
  skippedPhotoIds$ = new Subject<string[]>();
  closeDrawer = jasmine.createSpy('closeDrawer');
  removeItem = jasmine.createSpy('removeItem');
  clear = jasmine.createSpy('clear');
  download = jasmine.createSpy('download').and.returnValue(Promise.resolve());
}

const item = (id: string, albumId: string, albumTitle: string): CartItem => ({
  photoId: id,
  fileName: `${id}.jpg`,
  quality: 'High',
  sourceAlbumId: albumId,
  sourceAlbumTitle: albumTitle
});

describe('CartDrawerComponent', () => {
  let fixture: ComponentFixture<CartDrawerComponent>;
  let cart: CartServiceStub;

  beforeEach(async () => {
    cart = new CartServiceStub();
    await TestBed.configureTestingModule({
      imports: [CartDrawerComponent],
      providers: [{ provide: CartService, useValue: cart }]
    }).compileComponents();

    fixture = TestBed.createComponent(CartDrawerComponent);
    fixture.detectChanges();
  });

  it('renders one group per source album', () => {
    cart.cartItems$.next([
      item('p1', 'A1', 'Alpha'),
      item('p2', 'A1', 'Alpha'),
      item('p3', 'A2', 'Beta')
    ]);
    fixture.detectChanges();

    const groups = fixture.debugElement.queryAll(By.css('[data-testid="cart-album-group"]'));
    expect(groups.length).toBe(2);
    const titles = groups.map(g => g.nativeElement.textContent);
    expect(titles.some((t: string) => t.includes('Alpha'))).toBeTrue();
    expect(titles.some((t: string) => t.includes('Beta'))).toBeTrue();
  });

  it('renders the empty state when there are no items', () => {
    cart.cartItems$.next([]);
    fixture.detectChanges();
    const empty = fixture.nativeElement.textContent;
    expect(empty).toContain('Your cart is empty');
  });

  it('clicking download triggers cart.download()', async () => {
    cart.cartItems$.next([item('p1', 'A1', 'Alpha')]);
    fixture.detectChanges();
    const btn = fixture.debugElement.query(By.css('[data-testid="cart-download-btn"]'));
    btn.triggerEventHandler('click', null);
    await fixture.whenStable();
    expect(cart.download).toHaveBeenCalled();
  });

  it('shows the cap toast when download throws CartCapError', async () => {
    cart.download = jasmine.createSpy('download').and.returnValue(Promise.reject(new CartCapError(100)));
    cart.cartItems$.next([item('p1', 'A1', 'Alpha')]);
    fixture.detectChanges();

    await fixture.componentInstance.onDownload();
    fixture.detectChanges();

    const toast = fixture.debugElement.query(By.css('[data-testid="cart-toast"]'));
    expect(toast).toBeTruthy();
    expect(toast.nativeElement.textContent).toContain('Cart is full');
  });

  it('renders the skipped warning when skippedPhotoIds$ emits a non-empty list', () => {
    cart.skippedPhotoIds$.next(['p1', 'p2']);
    fixture.detectChanges();
    const warn = fixture.debugElement.query(By.css('[data-testid="cart-skipped-warning"]'));
    expect(warn).toBeTruthy();
    expect(warn.nativeElement.textContent).toContain('2 item');
    expect(warn.nativeElement.textContent).toContain('skipped');
  });

  it('does not render skipped warning when no items were skipped', () => {
    expect(fixture.debugElement.query(By.css('[data-testid="cart-skipped-warning"]'))).toBeNull();
  });

  describe('per-item quality picker (issue #109)', () => {
    beforeEach(() => {
      // Add updateQuality to the stub for this block.
      (cart as unknown as { updateQuality: jasmine.Spy }).updateQuality =
        jasmine.createSpy('updateQuality');
      cart.cartItems$.next([item('p1', 'A1', 'Alpha')]);
      fixture.detectChanges();
    });

    it('renders a quality <select> with all four options per item', () => {
      const select = fixture.debugElement
        .query(By.css('[data-testid="cart-item-quality-select"]'))
        .nativeElement as HTMLSelectElement;
      const values = Array.from(select.options).map(o => o.value);
      expect(values).toEqual(['Low', 'Medium', 'High', 'Original']);
    });

    it('changing the picker calls cart.updateQuality with old + new', () => {
      const select = fixture.debugElement
        .query(By.css('[data-testid="cart-item-quality-select"]'))
        .nativeElement as HTMLSelectElement;
      // Select 'Original' (last option).
      select.value = 'Original';
      select.dispatchEvent(new Event('change'));
      fixture.detectChanges();

      const spy = (cart as unknown as { updateQuality: jasmine.Spy }).updateQuality;
      expect(spy).toHaveBeenCalledWith('p1', 'High', 'Original');
    });
  });
});
