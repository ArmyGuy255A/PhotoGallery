import { Component, OnInit, OnDestroy } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { Subject } from 'rxjs';
import { takeUntil } from 'rxjs/operators';
import { CartService, CartQuality } from '../../services/cart.service';
import { AuthService } from '../../services/auth.service';
import { CartPanelComponent } from './cart-panel.component';
import { PhotoModalComponent, ModalPhoto } from '../photo-modal/photo-modal.component';
import { environment } from '../../../environments/environment';

interface PublicPhoto {
  photoId: string;
  fileName: string;
  uploadDate: string;
  thumbnailUrl?: string;
  mediumUrl?: string;
  availableQualities?: string[];
}

interface CodeValidation {
  albumId: string;
  albumTitle: string;
  albumDescription: string;
  isValid: boolean;
  expirationDate?: string;
}

/**
 * Public photo gallery for unauthenticated users with an access code.
 * Route: /code/:code
 *
 * Features:
 * - Validates the code, shows album metadata
 * - Loads photos with short-lived (15-min) thumbnail URLs from the backend
 * - Shows quality picker per photo to add items to a client-side cart
 * - Cart icon opens slide-out CartPanel for review and download
 */
@Component({
  selector: 'app-code-gallery',
  standalone: true,
  imports: [CommonModule, FormsModule, CartPanelComponent, PhotoModalComponent],
  template: `
    <div class="code-gallery">
      <header class="gallery-header">
        <h1>{{ album?.albumTitle || 'Photo Gallery' }}</h1>
        <div class="header-actions">
          <button
            *ngIf="isAuthenticated"
            class="save-button"
            (click)="onSaveToAccount()"
            [disabled]="saving || saved"
            [title]="saved ? 'Saved to your account' : 'Save this album to your account'">
            <span *ngIf="!saved && !saving">⭐ Save to my account</span>
            <span *ngIf="saving">Saving...</span>
            <span *ngIf="saved" class="saved-feedback">✓ Saved</span>
          </button>
          <button class="cart-button" (click)="cartOpen = true" [class.has-items]="cartCount > 0">
            🛒 Cart
            <span *ngIf="cartCount > 0" class="badge">{{ cartCount }}</span>
          </button>
        </div>
      </header>

      <div *ngIf="loading" class="loading">Loading album...</div>

      <div *ngIf="errorMessage" class="error">
        <h2>Unable to access this album</h2>
        <p>{{ errorMessage }}</p>
      </div>

      <main *ngIf="!loading && !errorMessage && album">
        <p class="description" *ngIf="album.albumDescription">{{ album.albumDescription }}</p>
        <p class="meta" *ngIf="album.expirationDate">
          Access expires {{ album.expirationDate | date: 'mediumDate' }}
        </p>

        <div *ngIf="photos.length === 0" class="empty-message">
          <p>No photos in this album yet.</p>
        </div>

        <div *ngIf="photos.length > 0" class="photo-grid">
          <article *ngFor="let photo of photos; let i = index" class="photo-card">
            <div class="photo-thumb" (click)="openModal(i)" role="button" tabindex="0"
                 (keydown.enter)="openModal(i)" (keydown.space)="openModal(i)"
                 [attr.aria-label]="'View ' + photo.fileName">
              <img *ngIf="photo.thumbnailUrl" [src]="photo.thumbnailUrl" [alt]="photo.fileName">
              <div *ngIf="!photo.thumbnailUrl" class="thumb-placeholder">📷</div>
              <div class="thumb-hover-overlay">🔍 View</div>
            </div>
            <div class="photo-meta">
              <div class="filename" [title]="photo.fileName">{{ photo.fileName }}</div>
              <div class="actions">
                <select [(ngModel)]="selectedQuality[photo.photoId]" class="quality-select">
                  <option value="Low">Low</option>
                  <option value="Medium">Medium</option>
                  <option value="High">High</option>
                </select>
                <button
                  class="add-btn"
                  (click)="onAddToCart(photo)"
                  [disabled]="isInCart(photo)">
                  {{ isInCart(photo) ? '✓ Added' : '+ Add' }}
                </button>
              </div>
            </div>
          </article>
        </div>
      </main>

      <app-photo-modal
        [photos]="modalPhotos"
        [(currentIndex)]="modalIndex"
        [isOpen]="modalOpen"
        [showCartButton]="true"
        (closed)="modalOpen = false"
        (cartAction)="onModalAddToCart($event)">
      </app-photo-modal>

      <app-cart-panel
        [isOpen]="cartOpen"
        [code]="code"
        (closed)="cartOpen = false">
      </app-cart-panel>
    </div>
  `,
  styles: [`
    .code-gallery {
      max-width: 1200px;
      margin: 0 auto;
      padding: 20px;
    }

    .gallery-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      margin-bottom: 20px;
      padding-bottom: 16px;
      border-bottom: 2px solid #e0e0e0;
    }

    .gallery-header h1 {
      margin: 0;
      font-size: 26px;
      color: #333;
    }

    .cart-button {
      position: relative;
      background: white;
      border: 2px solid #0066cc;
      color: #0066cc;
      padding: 8px 16px;
      border-radius: 6px;
      cursor: pointer;
      font-size: 14px;
      font-weight: 500;
    }

    .header-actions {
      display: flex;
      gap: 10px;
      align-items: center;
    }

    .save-button {
      background: white;
      border: 2px solid #2e7d32;
      color: #2e7d32;
      padding: 8px 16px;
      border-radius: 6px;
      cursor: pointer;
      font-size: 14px;
      font-weight: 500;
    }

    .save-button:hover:not(:disabled) {
      background: #e8f5e9;
    }

    .save-button:disabled {
      cursor: default;
    }

    .save-button .saved-feedback {
      color: #2e7d32;
      font-weight: 600;
    }

    .cart-button:hover {
      background: #e3f2fd;
    }

    .cart-button.has-items {
      background: #0066cc;
      color: white;
    }

    .cart-button .badge {
      display: inline-block;
      background: #c62828;
      color: white;
      border-radius: 999px;
      padding: 2px 8px;
      font-size: 12px;
      font-weight: 700;
      margin-left: 6px;
    }

    .loading, .error, .empty-message {
      text-align: center;
      padding: 60px 20px;
      color: #666;
    }

    .error h2 {
      color: #c62828;
      margin-bottom: 8px;
    }

    .description {
      color: #555;
      font-size: 15px;
      margin: 0 0 8px 0;
    }

    .meta {
      color: #999;
      font-size: 13px;
      margin: 0 0 24px 0;
    }

    .photo-grid {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(180px, 1fr));
      gap: 16px;
    }

    .photo-card {
      background: white;
      border: 1px solid #e0e0e0;
      border-radius: 8px;
      overflow: hidden;
      transition: transform 0.15s, box-shadow 0.15s;
    }

    .photo-card:hover {
      transform: translateY(-2px);
      box-shadow: 0 4px 12px rgba(0, 0, 0, 0.1);
    }

    .photo-thumb {
      position: relative;
      width: 100%;
      aspect-ratio: 1;
      background: #f5f5f5;
      display: flex;
      align-items: center;
      justify-content: center;
      overflow: hidden;
      cursor: pointer;
    }

    .photo-thumb:focus {
      outline: 2px solid #0066cc;
      outline-offset: -2px;
    }

    .photo-thumb img {
      width: 100%;
      height: 100%;
      object-fit: cover;
    }

    .thumb-hover-overlay {
      position: absolute;
      inset: 0;
      background: rgba(0, 0, 0, 0.5);
      color: white;
      display: flex;
      align-items: center;
      justify-content: center;
      font-size: 14px;
      font-weight: 500;
      opacity: 0;
      transition: opacity 0.15s;
    }

    .photo-thumb:hover .thumb-hover-overlay,
    .photo-thumb:focus .thumb-hover-overlay {
      opacity: 1;
    }

    .thumb-placeholder {
      font-size: 36px;
      color: #ccc;
    }

    .photo-meta {
      padding: 10px;
    }

    .filename {
      font-size: 13px;
      color: #333;
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
      margin-bottom: 8px;
    }

    .actions {
      display: flex;
      gap: 6px;
    }

    .quality-select {
      flex: 1;
      padding: 5px;
      border: 1px solid #ccc;
      border-radius: 4px;
      font-size: 12px;
    }

    .add-btn {
      background: #0066cc;
      color: white;
      border: none;
      padding: 5px 12px;
      border-radius: 4px;
      cursor: pointer;
      font-size: 12px;
      font-weight: 500;
      white-space: nowrap;
    }

    .add-btn:hover:not(:disabled) {
      background: #0052a3;
    }

    .add-btn:disabled {
      background: #c8e6c9;
      color: #2e7d32;
      cursor: default;
    }
  `]
})
export class CodeGalleryComponent implements OnInit, OnDestroy {
  code = '';
  album: CodeValidation | null = null;
  photos: PublicPhoto[] = [];
  selectedQuality: Record<string, CartQuality> = {};
  loading = true;
  errorMessage = '';
  cartOpen = false;
  cartCount = 0;
  isAuthenticated = false;
  saving = false;
  saved = false;

  // Modal state
  modalOpen = false;
  modalIndex = 0;

  private destroy$ = new Subject<void>();

  constructor(
    private route: ActivatedRoute,
    private http: HttpClient,
    private cart: CartService,
    private authService: AuthService
  ) {
    this.isAuthenticated = this.authService.isAuthenticated();
  }

  ngOnInit(): void {
    this.route.params.pipe(takeUntil(this.destroy$)).subscribe(params => {
      this.code = params['code'];
      if (this.code) {
        this.cart.loadForCode(this.code);
        this.loadAlbum();
        this.loadPhotos();
      }
    });

    this.cart.cart$.pipe(takeUntil(this.destroy$)).subscribe(items => {
      this.cartCount = items.length;
    });
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  /** Photos transformed for the PhotoModalComponent — uses mediumUrl as displayUrl. */
  get modalPhotos(): ModalPhoto[] {
    return this.photos.map(p => ({
      photoId: p.photoId,
      fileName: p.fileName,
      thumbnailUrl: p.thumbnailUrl,
      displayUrl: p.mediumUrl ?? p.thumbnailUrl
    }));
  }

  openModal(index: number): void {
    this.modalIndex = index;
    this.modalOpen = true;
  }

  onModalAddToCart(modalPhoto: ModalPhoto): void {
    const photo = this.photos.find(p => p.photoId === modalPhoto.photoId);
    if (photo) {
      this.onAddToCart(photo);
    }
  }

  isInCart(photo: PublicPhoto): boolean {
    const quality = this.selectedQuality[photo.photoId] || 'Medium';
    return this.cart.contains(photo.photoId, quality);
  }

  onAddToCart(photo: PublicPhoto): void {
    const quality = this.selectedQuality[photo.photoId] || 'Medium';
    const added = this.cart.addItem({
      photoId: photo.photoId,
      fileName: photo.fileName,
      thumbnailUrl: photo.thumbnailUrl,
      quality
    });
    if (!added) {
      if (this.cart.count >= 100) {
        alert('Cart is full (100 items max). Please download or remove items first.');
      }
    }
  }

  /**
   * EPIC-02 Slice B — save the current access code to the authenticated
   * user's account. Idempotent on the server side. Shows ✓ Saved for 2s.
   */
  onSaveToAccount(): void {
    if (this.saving || this.saved || !this.code) return;
    this.saving = true;
    const apiUrl = environment.apiUrl || '';
    this.http.post(`${apiUrl}/api/account/access-codes`, { code: this.code })
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: () => {
          this.saving = false;
          this.saved = true;
          setTimeout(() => { this.saved = false; }, 2000);
        },
        error: (err) => {
          this.saving = false;
          console.error('Failed to save access code:', err);
          if (err?.status === 401) {
            alert('Please sign in to save this album.');
          } else if (err?.status === 400) {
            alert(err?.error ?? 'This access code cannot be saved.');
          } else {
            alert('Failed to save. Please try again.');
          }
        }
      });
  }

  private loadAlbum(): void {
    const apiUrl = environment.apiUrl || '';
    this.http.get<CodeValidation>(`${apiUrl}/api/code/${this.code}/validate`)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (data) => {
          this.album = data;
          this.loading = false;
        },
        error: (err) => {
          this.loading = false;
          if (err.status === 404) {
            this.errorMessage = 'Access code not found.';
          } else if (err.status === 403) {
            this.errorMessage = 'This access code has expired.';
          } else {
            this.errorMessage = 'Failed to load album. Please try again later.';
          }
        }
      });
  }

  private loadPhotos(): void {
    const apiUrl = environment.apiUrl || '';
    this.http.get<{ photos: PublicPhoto[]; totalCount: number; page: number; pageSize: number; hasMore: boolean }>(
      `${apiUrl}/api/code/${this.code}/photos`
    )
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (data) => {
          this.photos = data?.photos ?? [];
          for (const p of this.photos) {
            if (!this.selectedQuality[p.photoId]) {
              this.selectedQuality[p.photoId] = 'Medium';
            }
          }
        },
        error: (err) => {
          console.error('Error loading photos:', err);
        }
      });
  }
}
