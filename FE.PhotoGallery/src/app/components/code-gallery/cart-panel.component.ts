import { Component, Input, Output, EventEmitter } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { CartService, CartItem, CartQuality } from '../../services/cart.service';
import { environment } from '../../../environments/environment';

/**
 * Slide-out cart drawer. Shows current cart contents, allows quality changes,
 * removal, clear, and triggers the bulk ZIP download.
 */
@Component({
  selector: 'app-cart-panel',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="cart-overlay" *ngIf="isOpen" (click)="close()"></div>

    <aside class="cart-drawer" [class.open]="isOpen">
      <header class="cart-header">
        <h3>Cart ({{ items.length }})</h3>
        <button class="close-btn" (click)="close()" aria-label="Close cart">×</button>
      </header>

      <div class="cart-body">
        <div *ngIf="items.length === 0" class="empty-state">
          <p>Your cart is empty.</p>
          <p class="hint">Browse photos and click "Add" to get started.</p>
        </div>

        <ul class="cart-items" *ngIf="items.length > 0">
          <li *ngFor="let item of items; trackBy: trackByItem" class="cart-item">
            <div class="thumb">
              <img *ngIf="item.thumbnailUrl" [src]="item.thumbnailUrl" [alt]="item.fileName">
              <div *ngIf="!item.thumbnailUrl" class="thumb-placeholder">📷</div>
            </div>
            <div class="item-info">
              <div class="item-name" [title]="item.fileName">{{ item.fileName }}</div>
              <div class="item-controls">
                <select
                  [ngModel]="item.quality"
                  (ngModelChange)="onQualityChange(item, $event)"
                  class="quality-select">
                  <option value="Low">Low</option>
                  <option value="Medium">Medium</option>
                  <option value="High">High</option>
                </select>
                <button class="remove-btn" (click)="onRemove(item)" title="Remove">✕</button>
              </div>
            </div>
          </li>
        </ul>
      </div>

      <footer class="cart-footer" *ngIf="items.length > 0">
        <div class="error-banner" *ngIf="errorMessage">{{ errorMessage }}</div>
        <div class="footer-actions">
          <button class="clear-btn" (click)="onClear()" [disabled]="isDownloading">Clear</button>
          <button class="download-btn" (click)="onDownload()" [disabled]="isDownloading">
            {{ isDownloading ? 'Preparing...' : 'Download (' + items.length + ')' }}
          </button>
        </div>
      </footer>
    </aside>
  `,
  styles: [`
    .cart-overlay {
      position: fixed;
      top: 0;
      left: 0;
      right: 0;
      bottom: 0;
      background: rgba(0, 0, 0, 0.4);
      z-index: 999;
    }

    .cart-drawer {
      position: fixed;
      top: 0;
      right: 0;
      width: 380px;
      max-width: 100vw;
      height: 100vh;
      background: white;
      box-shadow: -4px 0 16px rgba(0, 0, 0, 0.15);
      z-index: 1000;
      transform: translateX(100%);
      transition: transform 0.25s ease-out;
      display: flex;
      flex-direction: column;
    }

    .cart-drawer.open {
      transform: translateX(0);
    }

    .cart-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      padding: 16px 20px;
      border-bottom: 1px solid #e0e0e0;
    }

    .cart-header h3 {
      margin: 0;
      font-size: 18px;
      color: #333;
    }

    .close-btn {
      background: none;
      border: none;
      font-size: 28px;
      line-height: 1;
      cursor: pointer;
      color: #666;
      padding: 0 8px;
    }

    .close-btn:hover { color: #333; }

    .cart-body {
      flex: 1;
      overflow-y: auto;
      padding: 12px 0;
    }

    .empty-state {
      padding: 40px 20px;
      text-align: center;
      color: #666;
    }

    .empty-state p { margin: 0 0 8px 0; }
    .empty-state .hint { font-size: 13px; color: #999; }

    .cart-items {
      list-style: none;
      margin: 0;
      padding: 0;
    }

    .cart-item {
      display: flex;
      gap: 12px;
      padding: 12px 20px;
      border-bottom: 1px solid #f0f0f0;
    }

    .thumb {
      width: 60px;
      height: 60px;
      flex-shrink: 0;
      border-radius: 4px;
      overflow: hidden;
      background: #f5f5f5;
      display: flex;
      align-items: center;
      justify-content: center;
    }

    .thumb img {
      width: 100%;
      height: 100%;
      object-fit: cover;
    }

    .thumb-placeholder {
      font-size: 24px;
      color: #ccc;
    }

    .item-info {
      flex: 1;
      min-width: 0;
      display: flex;
      flex-direction: column;
      justify-content: space-between;
    }

    .item-name {
      font-size: 13px;
      color: #333;
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }

    .item-controls {
      display: flex;
      gap: 8px;
      align-items: center;
    }

    .quality-select {
      flex: 1;
      padding: 4px 6px;
      border: 1px solid #ccc;
      border-radius: 4px;
      font-size: 12px;
    }

    .remove-btn {
      background: none;
      border: 1px solid #e0e0e0;
      border-radius: 4px;
      width: 28px;
      height: 28px;
      cursor: pointer;
      color: #999;
      font-size: 14px;
    }

    .remove-btn:hover {
      background: #fee;
      border-color: #fcc;
      color: #c33;
    }

    .cart-footer {
      border-top: 1px solid #e0e0e0;
      padding: 16px 20px;
      background: #fafafa;
    }

    .error-banner {
      background: #fee;
      color: #c33;
      padding: 8px 10px;
      border-radius: 4px;
      font-size: 13px;
      margin-bottom: 12px;
    }

    .footer-actions {
      display: flex;
      gap: 10px;
    }

    .clear-btn,
    .download-btn {
      padding: 10px;
      border-radius: 4px;
      cursor: pointer;
      font-size: 14px;
      font-weight: 500;
      border: none;
    }

    .clear-btn {
      background: white;
      color: #666;
      border: 1px solid #ccc;
      flex: 1;
    }

    .clear-btn:hover:not(:disabled) {
      background: #f5f5f5;
    }

    .download-btn {
      background: #0066cc;
      color: white;
      flex: 2;
    }

    .download-btn:hover:not(:disabled) {
      background: #0052a3;
    }

    .clear-btn:disabled,
    .download-btn:disabled {
      opacity: 0.6;
      cursor: not-allowed;
    }
  `]
})
export class CartPanelComponent {
  @Input() isOpen = false;
  @Input() code = '';
  @Output() closed = new EventEmitter<void>();

  items: CartItem[] = [];
  isDownloading = false;
  errorMessage = '';

  constructor(private cart: CartService, private http: HttpClient) {
    this.cart.cart$.subscribe(items => {
      this.items = items;
    });
  }

  close(): void {
    this.closed.emit();
  }

  trackByItem(_idx: number, item: CartItem): string {
    return `${item.photoId}-${item.quality}`;
  }

  onQualityChange(item: CartItem, newQuality: CartQuality): void {
    this.cart.updateQuality(item.photoId, item.quality, newQuality);
  }

  onRemove(item: CartItem): void {
    this.cart.removeItem(item.photoId, item.quality);
  }

  onClear(): void {
    if (confirm('Remove all items from cart?')) {
      this.cart.clear();
    }
  }

  async onDownload(): Promise<void> {
    if (this.items.length === 0 || this.isDownloading) return;
    this.isDownloading = true;
    this.errorMessage = '';

    const apiUrl = environment.apiUrl || '';
    const url = `${apiUrl}/api/code/${this.code}/cart/download`;
    const body = {
      items: this.items.map(i => ({ photoId: i.photoId, quality: i.quality }))
    };

    try {
      const response = await fetch(url, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body),
        credentials: 'include'
      });

      if (!response.ok) {
        const errorText = await response.text().catch(() => '');
        throw new Error(errorText || `Download failed (HTTP ${response.status})`);
      }

      const blob = await response.blob();
      const filename = this.extractFilename(response) ?? `photos-${Date.now()}.zip`;

      const objectUrl = URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = objectUrl;
      link.download = filename;
      document.body.appendChild(link);
      link.click();
      document.body.removeChild(link);
      URL.revokeObjectURL(objectUrl);
    } catch (err: any) {
      this.errorMessage = err?.message || 'Download failed. Please try again.';
      console.error('Cart download error:', err);
    } finally {
      this.isDownloading = false;
    }
  }

  private extractFilename(response: Response): string | null {
    const dispo = response.headers.get('Content-Disposition');
    if (!dispo) return null;
    const match = /filename="?([^"]+)"?/i.exec(dispo);
    return match ? match[1] : null;
  }
}
