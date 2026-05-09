import { Component, Input, Output, EventEmitter, OnDestroy, OnInit, HostListener } from '@angular/core';
import { CommonModule } from '@angular/common';

export interface ModalPhoto {
  photoId: string;
  fileName: string;
  /** URL to display in the modal — typically the medium-quality URL. */
  displayUrl?: string;
  thumbnailUrl?: string;
}

/**
 * Fullscreen photo lightbox/modal with prev/next navigation.
 *
 * Reusable across album-detail (admin view) and code-gallery (client view).
 * The host component supplies a list of photos and the index to start at.
 *
 * Keyboard:
 *   Esc — close
 *   ← → — navigate
 *
 * Add-to-cart slot is rendered via [showCartButton] toggle and (cartAction) output.
 */
@Component({
  selector: 'app-photo-modal',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="modal-backdrop" *ngIf="isOpen" (click)="onBackdropClick($event)">
      <button class="close-btn" (click)="close()" aria-label="Close">×</button>

      <button
        class="nav-btn nav-prev"
        *ngIf="canNavigatePrev()"
        (click)="prev(); $event.stopPropagation()"
        aria-label="Previous photo">
        ‹
      </button>

      <button
        class="nav-btn nav-next"
        *ngIf="canNavigateNext()"
        (click)="next(); $event.stopPropagation()"
        aria-label="Next photo">
        ›
      </button>

      <div class="modal-content" (click)="$event.stopPropagation()">
        <div class="image-container">
          <img *ngIf="currentPhoto?.displayUrl"
               [src]="currentPhoto?.displayUrl"
               [alt]="currentPhoto?.fileName"
               class="modal-image">
          <div *ngIf="!currentPhoto?.displayUrl" class="image-placeholder">
            Image not available
          </div>
        </div>

        <footer class="modal-footer">
          <div class="photo-info">
            <div class="filename">{{ currentPhoto?.fileName }}</div>
            <div class="counter" *ngIf="photos.length > 1">
              {{ currentIndex + 1 }} / {{ photos.length }}
            </div>
          </div>

          <button
            *ngIf="showCartButton && currentPhoto"
            class="cart-btn"
            (click)="onAddToCart(); $event.stopPropagation()">
            + Add to Cart
          </button>
        </footer>
      </div>
    </div>
  `,
  styles: [`
    .modal-backdrop {
      position: fixed;
      inset: 0;
      background: rgba(0, 0, 0, 0.92);
      z-index: 1100;
      display: flex;
      align-items: center;
      justify-content: center;
      padding: 20px;
      animation: fadeIn 0.15s ease-out;
    }

    @keyframes fadeIn {
      from { opacity: 0; }
      to { opacity: 1; }
    }

    .close-btn {
      position: absolute;
      top: 16px;
      right: 24px;
      background: rgba(0, 0, 0, 0.5);
      border: 2px solid rgba(255, 255, 255, 0.4);
      color: white;
      width: 44px;
      height: 44px;
      border-radius: 50%;
      font-size: 24px;
      cursor: pointer;
      transition: background 0.15s, border-color 0.15s;
      /* Flex centering ensures the × glyph sits exactly in the middle of the circle
         regardless of font metrics (line-height alone is unreliable across fonts). */
      display: inline-flex;
      align-items: center;
      justify-content: center;
      line-height: 1;
      padding: 0;
      font-family: Arial, sans-serif;
    }

    .close-btn:hover {
      background: rgba(255, 255, 255, 0.2);
      border-color: white;
    }

    .nav-btn {
      position: absolute;
      top: 50%;
      transform: translateY(-50%);
      background: rgba(0, 0, 0, 0.5);
      border: 2px solid rgba(255, 255, 255, 0.4);
      color: white;
      width: 56px;
      height: 56px;
      border-radius: 50%;
      font-size: 32px;
      cursor: pointer;
      transition: background 0.15s, border-color 0.15s;
      display: inline-flex;
      align-items: center;
      justify-content: center;
      line-height: 1;
      padding: 0;
      font-family: Arial, sans-serif;
    }

    .nav-btn:hover {
      background: rgba(255, 255, 255, 0.2);
      border-color: white;
    }

    .nav-prev { left: 24px; }
    .nav-next { right: 24px; }

    .modal-content {
      max-width: 90vw;
      max-height: 90vh;
      display: flex;
      flex-direction: column;
    }

    .image-container {
      flex: 1;
      display: flex;
      align-items: center;
      justify-content: center;
      max-height: 80vh;
      overflow: hidden;
    }

    .modal-image {
      max-width: 100%;
      max-height: 80vh;
      object-fit: contain;
      box-shadow: 0 4px 32px rgba(0, 0, 0, 0.5);
    }

    .image-placeholder {
      color: white;
      font-size: 18px;
      padding: 80px 40px;
      text-align: center;
    }

    .modal-footer {
      display: flex;
      justify-content: space-between;
      align-items: center;
      padding: 16px 4px;
      gap: 16px;
      color: white;
    }

    .photo-info {
      flex: 1;
      min-width: 0;
    }

    .filename {
      font-size: 14px;
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }

    .counter {
      font-size: 12px;
      color: #aaa;
      margin-top: 2px;
    }

    .cart-btn {
      background: #0066cc;
      color: white;
      border: none;
      padding: 10px 18px;
      border-radius: 4px;
      cursor: pointer;
      font-size: 14px;
      font-weight: 500;
      flex-shrink: 0;
    }

    .cart-btn:hover {
      background: #0052a3;
    }
  `]
})
export class PhotoModalComponent implements OnInit, OnDestroy {
  @Input() photos: ModalPhoto[] = [];
  @Input() currentIndex = 0;
  @Input() isOpen = false;
  @Input() showCartButton = false;

  @Output() closed = new EventEmitter<void>();
  @Output() currentIndexChange = new EventEmitter<number>();
  @Output() cartAction = new EventEmitter<ModalPhoto>();

  ngOnInit(): void {
    if (this.isOpen) {
      this.lockBodyScroll();
    }
  }

  ngOnDestroy(): void {
    this.unlockBodyScroll();
  }

  get currentPhoto(): ModalPhoto | null {
    if (this.currentIndex < 0 || this.currentIndex >= this.photos.length) return null;
    return this.photos[this.currentIndex];
  }

  canNavigatePrev(): boolean {
    return this.currentIndex > 0;
  }

  canNavigateNext(): boolean {
    return this.currentIndex < this.photos.length - 1;
  }

  prev(): void {
    if (this.canNavigatePrev()) {
      this.currentIndex--;
      this.currentIndexChange.emit(this.currentIndex);
    }
  }

  next(): void {
    if (this.canNavigateNext()) {
      this.currentIndex++;
      this.currentIndexChange.emit(this.currentIndex);
    }
  }

  close(): void {
    this.unlockBodyScroll();
    this.closed.emit();
  }

  onBackdropClick(_event: MouseEvent): void {
    this.close();
  }

  onAddToCart(): void {
    const photo = this.currentPhoto;
    if (photo) {
      this.cartAction.emit(photo);
    }
  }

  @HostListener('document:keydown', ['$event'])
  onKeydown(event: KeyboardEvent): void {
    if (!this.isOpen) return;
    switch (event.key) {
      case 'Escape':
        this.close();
        break;
      case 'ArrowLeft':
        this.prev();
        break;
      case 'ArrowRight':
        this.next();
        break;
    }
  }

  private lockBodyScroll(): void {
    if (typeof document !== 'undefined') {
      document.body.style.overflow = 'hidden';
    }
  }

  private unlockBodyScroll(): void {
    if (typeof document !== 'undefined') {
      document.body.style.overflow = '';
    }
  }
}
