import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { BehaviorSubject } from 'rxjs';

import { CartButtonComponent } from './cart-button.component';
import { CartItem, CartService } from '../../services/cart.service';

class CartServiceStub {
  cartItems$ = new BehaviorSubject<CartItem[]>([]);
  cart$ = this.cartItems$.asObservable();
  toggleDrawer = jasmine.createSpy('toggleDrawer');
}

describe('CartButtonComponent', () => {
  let fixture: ComponentFixture<CartButtonComponent>;
  let cart: CartServiceStub;

  beforeEach(async () => {
    cart = new CartServiceStub();
    await TestBed.configureTestingModule({
      imports: [CartButtonComponent],
      providers: [{ provide: CartService, useValue: cart }]
    }).compileComponents();

    fixture = TestBed.createComponent(CartButtonComponent);
    fixture.detectChanges();
  });

  it('does not render the badge when the cart is empty', () => {
    expect(fixture.debugElement.query(By.css('[data-testid="cart-badge"]'))).toBeNull();
  });

  it('renders the badge with the item count when the cart has items', () => {
    cart.cartItems$.next([
      { photoId: 'p1', fileName: 'a.jpg', quality: 'High' },
      { photoId: 'p2', fileName: 'b.jpg', quality: 'Low' },
      { photoId: 'p3', fileName: 'c.jpg', quality: 'Medium' }
    ]);
    fixture.detectChanges();
    const badge = fixture.debugElement.query(By.css('[data-testid="cart-badge"]'));
    expect(badge).toBeTruthy();
    expect(badge.nativeElement.textContent.trim()).toBe('3');
  });

  it('clicking the button toggles the cart drawer', () => {
    const btn = fixture.debugElement.query(By.css('[data-testid="global-cart-button"]'));
    btn.triggerEventHandler('click', null);
    expect(cart.toggleDrawer).toHaveBeenCalled();
  });
});
