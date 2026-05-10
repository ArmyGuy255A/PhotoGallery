import { Component, OnDestroy, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Subject } from 'rxjs';
import { takeUntil } from 'rxjs/operators';
import { CartService } from '../../services/cart.service';

/**
 * Global cart button rendered in the navbar's right zone (next to the
 * user dropdown). Shows a count badge driven by CartService.cartItems$
 * and toggles the drawer via CartService.
 */
@Component({
  selector: 'app-cart-button',
  standalone: true,
  imports: [CommonModule],
  template: `
    <button
      type="button"
      class="cart-icon-button"
      [class.has-items]="count > 0"
      (click)="toggle()"
      aria-label="Open cart"
      data-testid="global-cart-button">
      <span class="cart-icon" aria-hidden="true">🛒</span>
      <span *ngIf="count > 0" class="badge" data-testid="cart-badge">{{ count }}</span>
    </button>
  `,
  styles: [`
    .cart-icon-button {
      position: relative;
      background: transparent;
      border: none;
      cursor: pointer;
      padding: 6px 10px;
      border-radius: 6px;
      color: inherit;
      font-size: 18px;
      line-height: 1;
    }
    .cart-icon-button:hover { background: rgba(0,0,0,0.06); }
    .cart-icon-button.has-items { color: #0066cc; }
    .badge {
      position: absolute;
      top: -2px;
      right: -2px;
      background: #c62828;
      color: white;
      border-radius: 999px;
      padding: 1px 6px;
      font-size: 11px;
      font-weight: 700;
      line-height: 1.4;
    }
  `]
})
export class CartButtonComponent implements OnInit, OnDestroy {
  private readonly cart = inject(CartService);
  private readonly destroy$ = new Subject<void>();

  count = 0;

  ngOnInit(): void {
    this.cart.cartItems$
      .pipe(takeUntil(this.destroy$))
      .subscribe(items => { this.count = items.length; });
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  toggle(): void {
    this.cart.toggleDrawer();
  }
}
