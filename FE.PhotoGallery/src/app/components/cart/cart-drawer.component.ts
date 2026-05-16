import { Component, OnDestroy, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Subject } from 'rxjs';
import { takeUntil } from 'rxjs/operators';
import { CartCapError, CartItem, CartQuality, CartService } from '../../services/cart.service';

interface CartGroup {
  albumId: string;
  albumTitle: string;
  items: CartItem[];
  open: boolean;
}

/**
 * Global, authenticated-mode cart drawer mounted once inside BaseLayoutComponent.
 *
 * Groups items by sourceAlbumTitle (collapsible <details>), shows skipped-after-
 * download warnings, surfaces 409 cap toasts, and triggers the bulk download
 * via CartService.download(). Anonymous (per-code) downloads continue to use
 * CartPanelComponent inside CodeGalleryComponent.
 */
@Component({
  selector: 'app-cart-drawer',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="cart-overlay" *ngIf="isOpen" (click)="close()" data-testid="cart-overlay"></div>

    <aside class="cart-drawer"
           [class.open]="isOpen"
           [attr.aria-hidden]="!isOpen"
           data-testid="cart-drawer">
      <header class="cart-header">
        <h3>Cart ({{ items.length }})</h3>
        <button type="button" class="close-btn" (click)="close()" aria-label="Close cart">×</button>
      </header>

      <div class="cart-toasts" *ngIf="toastMessage" role="status" aria-live="polite" data-testid="cart-toast">
        {{ toastMessage }}
      </div>

      <div class="cart-warning" *ngIf="skippedCount > 0" role="alert" data-testid="cart-skipped-warning">
        {{ skippedCount }} item(s) were skipped because your access has expired.
      </div>

      <div class="cart-body">
        <div *ngIf="items.length === 0" class="empty-state">
          <p>Your cart is empty.</p>
          <p class="hint">Add photos from any album to get started.</p>
        </div>

        <div *ngFor="let g of groups; trackBy: trackByGroup" class="album-group" data-testid="cart-album-group">
          <details [open]="g.open" (toggle)="g.open = $any($event.target).open">
            <summary>
              <span class="group-title">{{ g.albumTitle }}</span>
              <span class="group-count">({{ g.items.length }})</span>
            </summary>
            <ul class="cart-items">
              <li *ngFor="let item of g.items; trackBy: trackByItem" class="cart-item">
                <div class="thumb">
                  <img *ngIf="item.thumbnailUrl" [src]="item.thumbnailUrl" [alt]="item.fileName">
                  <div *ngIf="!item.thumbnailUrl" class="thumb-placeholder">📷</div>
                </div>
                <div class="item-info">
                  <div class="item-name" [title]="item.fileName">{{ item.fileName }}</div>
                  <div class="item-controls">
                    <!-- Issue #109: quality picker per item — was a static pill,
                         which left users no way to bump Low → High without
                         removing and re-adding the item. -->
                    <select
                      class="quality-select"
                      [ngModel]="item.quality"
                      (ngModelChange)="onQualityChange(item, $event)"
                      [attr.aria-label]="'Quality for ' + item.fileName"
                      data-testid="cart-item-quality-select">
                      <option value="Low">Low</option>
                      <option value="Medium">Medium</option>
                      <option value="High">High</option>
                      <option value="Original">Original</option>
                    </select>
                    <button type="button"
                            class="remove-btn"
                            (click)="onRemove(item)"
                            aria-label="Remove from cart">✕</button>
                  </div>
                </div>
              </li>
            </ul>
          </details>
        </div>
      </div>

      <footer class="cart-footer" *ngIf="items.length > 0">
        <div class="footer-actions">
          <button type="button"
                  class="clear-btn"
                  (click)="onClear()"
                  [disabled]="isDownloading">Clear</button>
          <button type="button"
                  class="download-btn"
                  (click)="onDownload()"
                  [disabled]="isDownloading"
                  data-testid="cart-download-btn">
            {{ isDownloading ? 'Preparing...' : 'Download All (' + items.length + ')' }}
          </button>
        </div>
      </footer>
    </aside>
  `,
  styles: [`
    .cart-overlay {
      position: fixed; inset: 0;
      background: rgba(0, 0, 0, 0.4);
      z-index: 999;
    }
    .cart-drawer {
      position: fixed; top: 0; right: 0;
      width: 380px; max-width: 100vw; height: 100vh;
      background: white;
      box-shadow: -4px 0 16px rgba(0, 0, 0, 0.15);
      z-index: 1000;
      transform: translateX(100%);
      transition: transform 0.25s ease-out;
      display: flex; flex-direction: column;
    }
    .cart-drawer.open { transform: translateX(0); }
    .cart-header {
      display: flex; justify-content: space-between; align-items: center;
      padding: 16px 20px; border-bottom: 1px solid #e0e0e0;
    }
    .cart-header h3 { margin: 0; font-size: 18px; color: #333; }
    .close-btn {
      background: none; border: none; font-size: 28px;
      line-height: 1; cursor: pointer; color: #666;
    }
    .cart-toasts {
      background: #ffebee; color: #c62828;
      padding: 10px 14px; margin: 12px 16px 0;
      border-radius: 4px; border: 1px solid #ef9a9a;
      font-size: 13px;
    }
    .cart-warning {
      background: #fff8e1; color: #6d4c00;
      padding: 10px 14px; margin: 12px 16px 0;
      border-radius: 4px; border: 1px solid #ffd54f;
      font-size: 13px;
    }
    .cart-body { flex: 1; overflow-y: auto; padding: 12px 0; }
    .empty-state { padding: 40px 20px; text-align: center; color: #666; }
    .empty-state .hint { font-size: 13px; color: #999; }

    .album-group { padding: 0 16px; margin-bottom: 8px; }
    .album-group summary {
      cursor: pointer; padding: 8px 4px;
      font-weight: 600; color: #333;
      display: flex; gap: 6px; align-items: center;
    }
    .group-count { color: #999; font-weight: 400; font-size: 13px; }

    .cart-items { list-style: none; margin: 0; padding: 0; }
    .cart-item {
      display: flex; gap: 12px; padding: 10px 4px;
      border-bottom: 1px solid #f0f0f0;
    }
    .thumb {
      width: 56px; height: 56px; flex-shrink: 0;
      border-radius: 4px; overflow: hidden; background: #f5f5f5;
      display: flex; align-items: center; justify-content: center;
    }
    .thumb img { width: 100%; height: 100%; object-fit: cover; }
    .thumb-placeholder { font-size: 22px; color: #ccc; }
    .item-info {
      flex: 1; min-width: 0;
      display: flex; flex-direction: column; justify-content: space-between;
    }
    .item-name {
      font-size: 13px; color: #333;
      white-space: nowrap; overflow: hidden; text-overflow: ellipsis;
    }
    .item-controls { display: flex; gap: 8px; align-items: center; }
    .quality-select {
      flex: 1;
      padding: 4px 6px;
      border: 1px solid #ccc;
      border-radius: 4px;
      font-size: 12px;
      background: white;
      color: #333;
    }
    .quality-pill {
      background: #e3f2fd; color: #0066cc;
      padding: 2px 8px; border-radius: 999px;
      font-size: 11px; font-weight: 600;
    }
    .remove-btn {
      background: none; border: 1px solid #e0e0e0;
      border-radius: 4px; width: 26px; height: 26px;
      cursor: pointer; color: #999; font-size: 13px;
    }
    .remove-btn:hover { background: #fee; border-color: #fcc; color: #c33; }
    .cart-footer {
      border-top: 1px solid #e0e0e0;
      padding: 16px 20px; background: #fafafa;
    }
    .footer-actions { display: flex; gap: 10px; }
    .clear-btn, .download-btn {
      padding: 10px; border-radius: 4px; cursor: pointer;
      font-size: 14px; font-weight: 500; border: none;
    }
    .clear-btn {
      background: white; color: #666;
      border: 1px solid #ccc; flex: 1;
    }
    .download-btn { background: #0066cc; color: white; flex: 2; }
    .clear-btn:disabled, .download-btn:disabled {
      opacity: 0.6; cursor: not-allowed;
    }
  `]
})
export class CartDrawerComponent implements OnInit, OnDestroy {
  private readonly cart = inject(CartService);
  private readonly destroy$ = new Subject<void>();

  isOpen = false;
  items: CartItem[] = [];
  groups: CartGroup[] = [];
  isDownloading = false;
  toastMessage = '';
  skippedCount = 0;

  private toastTimer: ReturnType<typeof setTimeout> | null = null;

  ngOnInit(): void {
    this.cart.cartDrawerOpen$
      .pipe(takeUntil(this.destroy$))
      .subscribe(o => { this.isOpen = o; });

    this.cart.cartItems$
      .pipe(takeUntil(this.destroy$))
      .subscribe(items => {
        this.items = items;
        this.groups = this.regroup(items);
      });

    this.cart.errors
      .pipe(takeUntil(this.destroy$))
      .subscribe(msg => this.showToast(msg));

    this.cart.skippedPhotoIds$
      .pipe(takeUntil(this.destroy$))
      .subscribe(skipped => { this.skippedCount = skipped.length; });
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
    if (this.toastTimer !== null) clearTimeout(this.toastTimer);
  }

  close(): void { this.cart.closeDrawer(); }

  trackByGroup(_idx: number, g: CartGroup): string { return g.albumId; }
  trackByItem(_idx: number, i: CartItem): string { return `${i.photoId}::${i.quality}`; }

  onRemove(item: CartItem): void {
    this.cart.removeItem(item.photoId, item.quality);
  }

  /**
   * Per-item quality picker handler (issue #109). Routes through
   * <c>CartService.updateQuality</c> so the authed/anonymous branching is
   * handled in one place.
   */
  onQualityChange(item: CartItem, newQuality: CartQuality): void {
    if (!newQuality || newQuality === item.quality) return;
    this.cart.updateQuality(item.photoId, item.quality, newQuality);
  }

  onClear(): void {
    if (this.items.length === 0) return;
    if (typeof confirm === 'function' && !confirm('Remove all items from cart?')) return;
    this.cart.clear();
  }

  async onDownload(): Promise<void> {
    if (this.items.length === 0 || this.isDownloading) return;
    this.isDownloading = true;
    this.skippedCount = 0;
    try {
      await this.cart.download();
    } catch (err: unknown) {
      if (err instanceof CartCapError) {
        this.showToast('Cart is full. Please download or remove items first.');
      } else {
        const msg = err instanceof Error ? err.message : 'Download failed. Please try again.';
        this.showToast(msg);
      }
    } finally {
      this.isDownloading = false;
    }
  }

  private regroup(items: CartItem[]): CartGroup[] {
    const map = new Map<string, CartGroup>();
    for (const item of items) {
      const id = item.sourceAlbumId ?? '__unknown__';
      const title = item.sourceAlbumTitle ?? 'Other';
      let g = map.get(id);
      if (!g) {
        g = { albumId: id, albumTitle: title, items: [], open: true };
        map.set(id, g);
      }
      g.items.push(item);
    }
    return Array.from(map.values());
  }

  private showToast(message: string): void {
    this.toastMessage = message;
    if (this.toastTimer !== null) clearTimeout(this.toastTimer);
    this.toastTimer = setTimeout(() => {
      this.toastMessage = '';
      this.toastTimer = null;
    }, 4000);
  }
}
