import { Component, OnInit, OnDestroy, ViewChild, ElementRef, AfterViewInit } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../environments/environment';
import { PhotoUploadComponent } from './photo-upload.component';
import { UploadProgressAsideComponent } from './upload-progress-aside.component';
import { AccessCodeFormComponent } from './access-code-form.component';
import { PhotoModalComponent, ModalPhoto } from '../photo-modal/photo-modal.component';
import { Subject, interval, Observable } from 'rxjs';
import { takeUntil, switchMap } from 'rxjs/operators';
import { CartService, CartQuality } from '../../services/cart.service';
import { AuthService } from '../../services/auth.service';
import { BackToDashboardComponent } from '../back-to-dashboard/back-to-dashboard.component';
import { PhotoPage, PhotoPageLoader } from '../../services/photo-page-loader';

interface Photo {
  id: string;
  fileName: string;
  uploadDate: string;
  uploadedBy?: string;
  processingStatus?: string;
  thumbnailUrl?: string;
  mediumUrl?: string;
}

interface AccessCode {
  id: string;
  code: string;
  expirationDate: string | null;
  createdDate: string;
  createdBy?: string;
  createdByDisplayName?: string;
}

interface Album {
  id: string;
  title: string;
  description: string;
  createdDate: string;
  createdBy: string;
  createdByDisplayName?: string;
  ownerId: string;
}

@Component({
  selector: 'app-album-detail',
  standalone: true,
  imports: [CommonModule, FormsModule, PhotoUploadComponent, UploadProgressAsideComponent, AccessCodeFormComponent, PhotoModalComponent, BackToDashboardComponent],
  template: `
    <div class="album-detail-container" data-testid="album-detail">
      <header class="detail-header">
        <app-back-to-dashboard></app-back-to-dashboard>
        <h1 data-testid="album-title">{{ album?.title }}</h1>
      </header>

      <main class="detail-content">
        <div class="loading" *ngIf="isLoading">
          <p>Loading album details...</p>
        </div>

        <div *ngIf="!isLoading && album">
          <section class="album-info">
            <p class="description">{{ album.description || 'No description' }}</p>
            <p class="meta">Created on {{ (album.createdDate | date: 'short') }} by {{ album.createdByDisplayName || album.createdBy }}</p>
          </section>

          <!-- Photo Upload Section -->
          <section class="upload-section">
            <app-photo-upload 
              [albumId]="albumId"
              (uploadComplete)="onUploadComplete($event)">
            </app-photo-upload>
          </section>

          <section class="photos-section">
            <div class="section-header">
              <h2 data-testid="photos-count">Photos ({{ photos.length }})</h2>
              <button type="button"
                class="action-btn"
                [disabled]="reconcileState === 'running'"
                (click)="runReconcile()"
                data-testid="album-reconcile-button"
                title="Re-scan storage for missing thumbnails / variants and re-queue any that need to be regenerated.">
                {{ reconcileState === 'running' ? 'Reconciling…' : 'Reconcile' }}
              </button>
            </div>
            <div *ngIf="reconcileMessage" class="reconcile-summary" data-testid="album-reconcile-summary">
              {{ reconcileMessage }}
            </div>

            <!--
              Loading-state region (Phase 7).
              - Initial spinner: loader is fetching the first page; no envelope
                has come back yet. Centred CoreUI-style spinner + copy so the
                user knows photos are pending.
              - Pagination banner: at least one page has landed, but more
                photos are still being fetched or aren't yet on screen. Shows
                "Loaded X of Y photos…" with a tiny inline spinner.
              Both states are suppressed in the empty-state path (handled
              below by loader.isEmpty()) so the "No photos yet" copy still wins.
            -->
            <!--
              Initial-load spinner only. The previous "Loaded X of Y…" banner
              was removed in favour of auto-trickle pagination: every page
              loads in the background until all photos are present, so the
              banner had nothing useful to convey. Empty-state copy still
              wins via loader.isEmpty().
            -->
            <div class="photos-loading-initial"
                 *ngIf="loader.isLoading() && !loader.hasLoadedFirstPage()"
                 data-testid="album-photos-loading-initial"
                 role="status"
                 aria-live="polite">
              <div class="photos-spinner" aria-hidden="true"></div>
              <p class="photos-loading-copy">Loading photos…</p>
            </div>

            <div class="photos-grid" *ngIf="photos.length > 0" data-testid="photos-grid">
              <div *ngFor="let photo of photos; let i = index; trackBy: trackByPhotoId" class="photo-card" data-testid="photo-card"
                   [attr.data-photo-id]="photo.id"
                   role="button" tabindex="0">
                <!-- Issue #113: per-photo delete (✕) in the top-right, gated to
                     album owners + admins. Confirmation handled in
                     onDeletePhoto so the click handler is a one-liner. -->
                <button
                  *ngIf="canDeletePhotos"
                  type="button"
                  class="photo-delete-btn"
                  (click)="onDeletePhoto(photo); $event.stopPropagation()"
                  [attr.aria-label]="'Delete ' + photo.fileName"
                  data-testid="photo-delete-btn">✕</button>
                <div class="photo-thumb-clickable"
                     (click)="openModal(i)"
                     (keydown.enter)="openModal(i)" (keydown.space)="openModal(i)">
                <div *ngIf="getShouldShowBadge(photo)" class="photo-status-badge" [ngClass]="getStatusClass(photo)" data-testid="photo-status-badge">
                  <span *ngIf="photo.processingStatus === 'Processing'">⟳</span>
                  <span *ngIf="photo.processingStatus === 'Failed'">✗</span>
                </div>
                <div class="photo-placeholder">
                  <img *ngIf="photo.thumbnailUrl"
                       [src]="photo.thumbnailUrl"
                       alt="{{ photo.fileName }}"
                       class="photo-image"
                       data-testid="photo-card-image"
                       (error)="onThumbnailError(photo)">
                  <svg *ngIf="!photo.thumbnailUrl" viewBox="0 0 24 24" data-testid="photo-card-placeholder">
                    <path d="M21 19V5c0-1.1-.9-2-2-2H5c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h14c1.1 0 2-.9 2-2zM8.5 13.5l2.5 3.01L14.5 12l4.5 6H5l3.5-4.5z"/>
                  </svg>
                </div>
                </div>
                <h3 data-testid="photo-card-filename">{{ photo.fileName }}</h3>
                <p class="photo-meta">Uploaded {{ (photo.uploadDate | date: 'short') }}</p>
                <div class="photo-cart-actions" (click)="$event.stopPropagation()">
                  <select
                    [ngModel]="selectedQuality[photo.id] || 'Medium'"
                    (ngModelChange)="onQualityChange(photo.id, $event)"
                    class="quality-select"
                    data-testid="album-photo-quality-select"
                    aria-label="Quality">
                    <option value="Low">Low</option>
                    <option value="Medium">Medium</option>
                    <option value="High">High</option>
                    <option value="Original">Original</option>
                  </select>
                  <button
                    type="button"
                    class="add-cart-btn"
                    [class.in-cart]="isInCart(photo)"
                    (click)="onCartButtonClick(photo)"
                    data-testid="album-photo-add-to-cart">
                    {{ isInCart(photo) ? '✕ Remove' : '+ Add' }}
                  </button>
                </div>
              </div>
            </div>

            <!--
              Phase 6 skeleton grid: shown while the loader is fetching. Replaces
              the previous "no photos yet" flash that fired for any slow initial
              load. The 8 placeholder tiles are pure CSS so they paint within the
              first frame after route navigation.
            -->
            <div class="photos-grid skeleton-grid" *ngIf="loader.isLoading()" data-testid="album-photos-skeleton">
              <div *ngFor="let _ of skeletonSlots" class="photo-card skeleton-card" aria-hidden="true">
                <div class="skeleton-thumb"></div>
                <div class="skeleton-line"></div>
                <div class="skeleton-line short"></div>
              </div>
            </div>

            <!--
              Phase 6: the truly-empty state is only valid when nothing is loading
              AND the server confirmed there are no more pages. Conflating
              "photos.length === 0" with "empty" was the /code/8SSPRIUAMO5A bug.
            -->
            <div class="empty-message" *ngIf="loader.isEmpty()" data-testid="album-empty-photos">
              <p>No photos yet. Upload some photos to get started above.</p>
            </div>

            <!--
              Phase 6 sentinel: an empty div the IntersectionObserver watches.
              When it scrolls into view the component asks the loader for the
              next page. Rendered only while the server says there's more to
              fetch — keeps the observer from re-firing forever at the bottom.
            -->
            <div #sentinel class="photo-grid-sentinel" *ngIf="loader.hasMore()" aria-hidden="true" data-testid="album-photos-sentinel"></div>
          </section>

          <section class="access-codes-section">
            <div class="section-header">
              <h2>Access Codes ({{ accessCodes.length }})</h2>
              <button (click)="toggleCodeForm()" class="action-btn" *ngIf="!showCodeForm">+ Generate Code</button>
            </div>

            <app-access-code-form
              *ngIf="showCodeForm"
              [albumId]="albumId"
              (codeCreated)="onCodeCreated()"
              (cancelled)="showCodeForm = false">
            </app-access-code-form>

            <div class="codes-table" *ngIf="accessCodes.length > 0">
              <table>
                <thead>
                  <tr>
                    <th>Access Code</th>
                    <th>Status</th>
                    <th>Expiration</th>
                    <th>Created</th>
                    <th>Actions</th>
                  </tr>
                </thead>
                <tbody>
                  <tr *ngFor="let code of accessCodes">
                    <td class="code-cell">
                      <code>{{ code.code }}</code>
                      <button class="copy-btn" (click)="copyToClipboard(code.code)" title="Copy code">
                        <span *ngIf="copiedCode !== code.code">Copy</span>
                        <span *ngIf="copiedCode === code.code" class="copied-feedback">✓ Copied!</span>
                      </button>
                    </td>
                    <td>
                      <span class="status-badge" [ngClass]="getCodeStatus(code)">
                        {{ getCodeStatus(code) === 'active' ? 'Active' : 'Expired' }}
                      </span>
                    </td>
                    <td>{{ code.expirationDate ? (code.expirationDate | date: 'short') : 'Never' }}</td>
                    <td>{{ code.createdDate | date: 'short' }}</td>
                    <td>
                      <button class="copy-link-btn" (click)="copyShareLink(code.code)" title="Copy share link">
                        <span *ngIf="copiedLink !== code.code">🔗 Copy Link</span>
                        <span *ngIf="copiedLink === code.code" class="copied-feedback">✓ Copied!</span>
                      </button>
                      <button class="delete-btn" (click)="deleteAccessCode(code.id)">Delete</button>
                    </td>
                  </tr>
                </tbody>
              </table>
            </div>

            <div class="empty-message" *ngIf="accessCodes.length === 0 && !showCodeForm">
              <p>No access codes yet. Create one to share this album with clients.</p>
            </div>
          </section>

          <section class="admin-stats" *ngIf="isAdmin">
            <div class="stat-item">
              <label>Total Photos</label>
              <span class="stat-value">{{ photos.length }}</span>
            </div>
            <div class="stat-item">
              <label>Active Codes</label>
              <span class="stat-value">{{ getActiveCodes() }}</span>
            </div>
          </section>
        </div>

        <div class="error-message" *ngIf="errorMessage">
          {{ errorMessage }}
        </div>
      </main>

      <app-photo-modal
        [photos]="modalPhotos"
        [(currentIndex)]="modalIndex"
        [isOpen]="modalOpen"
        [showCartButton]="true"
        [isInCart]="isModalPhotoInCart"
        (closed)="modalOpen = false"
        (cartAction)="onModalCartAction($event)">
      </app-photo-modal>

      <app-upload-progress-aside
        *ngIf="album?.id"
        [albumId]="album!.id"
        (summaryChanged)="onAlbumActivityChanged($event)">
      </app-upload-progress-aside>
    </div>
  `,
  styles: [`
    .album-detail-container {
      max-width: 1200px;
      margin: 0 auto;
      padding: 20px;
    }

    .detail-header {
      display: flex;
      align-items: center;
      gap: 20px;
      margin-bottom: 30px;
      border-bottom: 2px solid #e0e0e0;
      padding-bottom: 15px;
    }

    .back-btn {
      background: none;
      border: none;
      color: #0066cc;
      cursor: pointer;
      font-size: 16px;
      padding: 5px 10px;
    }

    .back-btn:hover {
      background: #f0f0f0;
      border-radius: 4px;
    }

    .detail-header h1 {
      margin: 0;
      font-size: 28px;
      color: #333;
    }

    .loading {
      text-align: center;
      padding: 40px;
      color: #666;
    }

    /* Phase 7 loading states (initial spinner + pagination banner) */
    .photos-loading-initial {
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      gap: 14px;
      padding: 48px 20px;
      color: #555;
    }

    .photos-loading-copy {
      margin: 0;
      font-size: 16px;
      font-weight: 500;
    }

    .photos-spinner {
      width: 36px;
      height: 36px;
      border: 4px solid #d0d0d0;
      border-top-color: #0066cc;
      border-radius: 50%;
      animation: photos-spinner-rotate 0.9s linear infinite;
    }

    .photos-spinner-inline {
      width: 14px;
      height: 14px;
      border-width: 2px;
      border-top-color: #1d4ed8;
      display: inline-block;
    }

    @keyframes photos-spinner-rotate {
      to { transform: rotate(360deg); }
    }

    @media (prefers-reduced-motion: reduce) {
      .photos-spinner { animation-duration: 3s; }
    }

    .album-info {
      background: #f9f9f9;
      border-radius: 8px;
      padding: 20px;
      margin-bottom: 30px;
    }

    .description {
      font-size: 16px;
      color: #333;
      margin: 0 0 10px 0;
    }

    .meta {
      font-size: 14px;
      color: #999;
      margin: 0;
    }

    .upload-section {
      margin-bottom: 40px;
      padding: 20px;
      background: #f0f8ff;
      border-radius: 8px;
      border: 1px dashed #0066cc;
    }

    .photos-section,
    .access-codes-section,
    .admin-stats {
      margin-bottom: 40px;
    }

    .section-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      margin-bottom: 20px;
    }

    .section-header h2 {
      margin: 0;
      font-size: 20px;
      color: #333;
    }

    .action-btn {
      background: #0066cc;
      color: white;
      border: none;
      padding: 10px 20px;
      border-radius: 4px;
      cursor: pointer;
      font-size: 14px;
    }

    .action-btn:hover {
      background: #0052a3;
    }

    .photos-grid {
      display: grid;
      /* Issue #113: bump from 150px → 220px so cards aren't cramped. */
      grid-template-columns: repeat(auto-fill, minmax(220px, 1fr));
      gap: 16px;
    }

    .photo-card {
      background: white;
      border: 1px solid #e0e0e0;
      border-radius: 8px;
      overflow: hidden;
      cursor: pointer;
      transition: transform 0.2s, box-shadow 0.2s;
      position: relative;
    }

    .photo-card:hover {
      transform: translateY(-5px);
      box-shadow: 0 4px 12px rgba(0, 0, 0, 0.1);
    }

    .photo-status-badge {
      position: absolute;
      /* Issue #113: moved to top-left so it doesn't collide with the new
         delete (✕) button at top-right. */
      top: 5px;
      left: 5px;
      width: 24px;
      height: 24px;
      border-radius: 50%;
      display: flex;
      align-items: center;
      justify-content: center;
      font-size: 12px;
      color: white;
      z-index: 10;
    }

    .photo-status-badge.complete {
      background: #4caf50;
    }

    .photo-status-badge.processing {
      background: #ff9800;
    }

    .photo-status-badge.failed {
      background: #f44336;
    }

    .photo-status-badge.pending {
      background: #9e9e9e;
    }

    .photo-placeholder {
      width: 100%;
      height: 120px;
      background: #f5f5f5;
      display: flex;
      align-items: center;
      justify-content: center;
      overflow: hidden;
    }

    .photo-image {
      width: 100%;
      height: 100%;
      object-fit: cover;
    }

    .photo-placeholder svg {
      width: 60px;
      height: 60px;
      fill: #ccc;
    }

    .photo-card h3 {
      margin: 10px;
      font-size: 14px;
      color: #333;
      word-break: break-word;
    }

    .photo-meta {
      margin: 0 10px 10px;
      font-size: 12px;
      color: #999;
    }

    .empty-message {
      text-align: center;
      padding: 40px;
      color: #999;
      background: #f9f9f9;
      border-radius: 8px;
      border: 1px dashed #e0e0e0;
    }

    .codes-table {
      width: 100%;
      border-collapse: collapse;
    }

    .codes-table table {
      width: 100%;
      border-collapse: collapse;
      background: white;
      border-radius: 8px;
      overflow: hidden;
      box-shadow: 0 1px 3px rgba(0, 0, 0, 0.1);
    }

    .codes-table thead {
      background: #f5f5f5;
      border-bottom: 2px solid #e0e0e0;
    }

    .codes-table th {
      padding: 15px;
      text-align: left;
      font-weight: 600;
      color: #333;
      font-size: 14px;
    }

    .codes-table td {
      padding: 15px;
      border-bottom: 1px solid #e0e0e0;
      font-size: 14px;
    }

    .code-cell {
      font-family: monospace;
      display: flex;
      align-items: center;
      gap: 10px;
    }

    .code-cell code {
      background: #f5f5f5;
      padding: 5px 10px;
      border-radius: 4px;
      color: #0066cc;
    }

    .copy-btn {
      background: none;
      border: 1px solid transparent;
      cursor: pointer;
      font-size: 14px;
      padding: 4px 8px;
      border-radius: 4px;
      transition: background 0.15s, border-color 0.15s;
    }

    .copy-btn:hover {
      background: #f0f0f0;
      border-color: #ddd;
    }

    .copy-link-btn {
      background: #e3f2fd;
      color: #0066cc;
      border: 1px solid #bbdefb;
      padding: 6px 12px;
      border-radius: 4px;
      cursor: pointer;
      font-size: 12px;
      margin-right: 6px;
      transition: background 0.15s;
    }

    .copy-link-btn:hover {
      background: #bbdefb;
    }

    .copied-feedback {
      color: #2e7d32;
      font-weight: 600;
      font-size: 12px;
    }

    .status-badge {
      display: inline-block;
      padding: 5px 12px;
      border-radius: 20px;
      font-size: 12px;
      font-weight: 600;
    }

    .status-badge.active {
      background: #c8e6c9;
      color: #2e7d32;
    }

    .status-badge.expired {
      background: #ffcccc;
      color: #c62828;
    }

    .delete-btn {
      background: #f44336;
      color: white;
      border: none;
      padding: 6px 12px;
      border-radius: 4px;
      cursor: pointer;
      font-size: 12px;
    }

    .delete-btn:hover {
      background: #d32f2f;
    }

    .admin-stats {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
      gap: 20px;
    }

    .stat-item {
      background: white;
      border: 1px solid #e0e0e0;
      border-radius: 8px;
      padding: 20px;
      text-align: center;
    }

    .stat-item label {
      display: block;
      font-size: 14px;
      color: #999;
      margin-bottom: 10px;
    }

    .stat-value {
      display: block;
      font-size: 32px;
      font-weight: bold;
      color: #333;
    }

    .error-message {
      background: #ffebee;
      color: #c62828;
      padding: 15px;
      border-radius: 8px;
      margin-top: 20px;
      border-left: 4px solid #c62828;
    }

    .photo-cart-actions {
      display: flex;
      gap: 8px;
      align-items: stretch;
      padding: 8px 10px 12px;
    }
    .photo-cart-actions .quality-select {
      flex: 0 0 88px;
      min-width: 0;
      padding: 6px 8px;
      border: 1px solid #ccc;
      border-radius: 6px;
      font-size: 12px;
      background: white;
      color: #333;
    }
    /* Issue #113: deliberate button shape — fills the rest of the row,
       consistent rounded rectangle, fixed min-height so the action looks
       intentional next to the quality picker. */
    .photo-cart-actions .add-cart-btn {
      flex: 1 1 auto;
      min-height: 30px;
      background: #0066cc;
      border: 1px solid #0066cc;
      color: white;
      padding: 6px 12px;
      border-radius: 6px;
      cursor: pointer;
      font-size: 13px;
      font-weight: 600;
      line-height: 1;
      display: inline-flex;
      align-items: center;
      justify-content: center;
      transition: background 0.15s, border-color 0.15s, color 0.15s;
    }
    .photo-cart-actions .add-cart-btn:hover {
      background: #0052a3;
      border-color: #0052a3;
    }
    /* Issue #108: in-cart state shows a Remove affordance (red) so the user
       can undo from the card itself instead of being stuck with a disabled
       button. */
    .photo-cart-actions .add-cart-btn.in-cart {
      background: #fff;
      border-color: #c62828;
      color: #c62828;
    }
    .photo-cart-actions .add-cart-btn.in-cart:hover {
      background: #fdecea;
    }

    /* Issue #113: per-photo delete (✕) in the top-right of every card. */
    .photo-delete-btn {
      position: absolute;
      top: 6px;
      right: 6px;
      width: 26px;
      height: 26px;
      border-radius: 50%;
      border: none;
      background: rgba(0, 0, 0, 0.55);
      color: white;
      font-size: 14px;
      line-height: 1;
      cursor: pointer;
      display: inline-flex;
      align-items: center;
      justify-content: center;
      opacity: 0;
      transition: opacity 0.15s, background 0.15s;
      z-index: 20;
    }
    .photo-card:hover .photo-delete-btn,
    .photo-delete-btn:focus-visible {
      opacity: 1;
      outline: none;
    }
    .photo-delete-btn:hover {
      background: #c62828;
    }

    /* Phase 6: progressive grid skeleton + sentinel. */
    .skeleton-grid {
      opacity: 0.7;
    }
    .skeleton-card {
      cursor: default;
      pointer-events: none;
    }
    .skeleton-thumb {
      width: 100%;
      height: 120px;
      background: linear-gradient(90deg, #eee 25%, #f5f5f5 50%, #eee 75%);
      background-size: 200% 100%;
      animation: skeleton-pulse 1.4s ease-in-out infinite;
    }
    .skeleton-line {
      height: 12px;
      margin: 10px;
      border-radius: 4px;
      background: linear-gradient(90deg, #eee 25%, #f5f5f5 50%, #eee 75%);
      background-size: 200% 100%;
      animation: skeleton-pulse 1.4s ease-in-out infinite;
    }
    .skeleton-line.short {
      width: 60%;
    }
    @keyframes skeleton-pulse {
      0%   { background-position: 200% 0; }
      100% { background-position: -200% 0; }
    }
    .photo-grid-sentinel {
      width: 100%;
      height: 1px;
      margin-top: 24px;
    }
  `]
})
export class AlbumDetailComponent implements OnInit, OnDestroy, AfterViewInit {
  albumId: string = '';
  album: Album | null = null;
  /**
   * Phase 6: progressive photo loader. The component reads {@link photos} via
   * the {@link loader} signal so the grid grows as the IntersectionObserver
   * sentinel scrolls into view.
   */
  loader = new PhotoPageLoader<Photo>(
    (page, pageSize) => this.fetchPhotoPage(page, pageSize)
  );
  /** Skeleton-grid placeholder count (8 cards while the loader is fetching). */
  readonly skeletonSlots: ReadonlyArray<unknown> = new Array(8);
  accessCodes: AccessCode[] = [];
  isLoading = true;
  errorMessage = '';
  isAdmin = false;
  showCodeForm = false;
  copiedCode: string | null = null;
  copiedLink: string | null = null;

  // Photo modal state
  modalOpen = false;
  modalIndex = 0;

  /** Per-photo quality selection (defaults to Medium when unset). */
  selectedQuality: Record<string, CartQuality> = {};

  @ViewChild('sentinel', { read: ElementRef }) sentinelRef?: ElementRef<HTMLElement>;
  private intersectionObserver: IntersectionObserver | null = null;

  /**
   * Photos accumulated by the loader. Exposed as a plain getter (not a signal)
   * so the existing template + spec bindings (`photos.length`, `*ngFor`,
   * direct array mutation in tests) keep working without churn.
   */
  get photos(): Photo[] {
    return this.loader.photos();
  }
  set photos(value: Photo[]) {
    this.loader.photos.set(value);
  }

  private destroy$ = new Subject<void>();

  constructor(
    private route: ActivatedRoute,
    private http: HttpClient,
    private cart: CartService,
    private auth: AuthService
  ) {}

  ngOnInit(): void {
    this.route.params.pipe(takeUntil(this.destroy$)).subscribe(params => {
      this.albumId = params['id'];
      // Reset the loader so navigating between albums doesn't append the new
      // album's photos to the previous one's cache.
      this.loader.reset();
      this.loadAlbum();
      // Auto-trickle every page so the carousel + grid never get stuck
      // waiting for the sentinel — all photos eventually appear without
      // user scrolling. Sentinel + IntersectionObserver still attached
      // as a redundant trigger; auto-load is additive.
      this.loader.enableAutoLoad();
      this.loadAccessCodes();
      this.startPhotoStatusPolling();
    });

    // Deep-link: /albums/:id?photoId=:guid opens the carousel directly to
    // the requested photo. The admin "top downloaded photos" table and the
    // per-user downloads modal both pass this query param. We re-evaluate
    // on every page-load tick so the modal opens as soon as the loader
    // pulls in the page that contains the target photoId. Some test
    // doubles for ActivatedRoute omit queryParams — guard against it.
    if (this.route.queryParams) {
      this.route.queryParams.pipe(takeUntil(this.destroy$)).subscribe(qp => {
        this.pendingPhotoId = qp['photoId'] || null;
        this.tryOpenPendingPhoto();
      });
    }
    this.loader.onLoadCompleted = () => {
      // Re-attached on each ngOnInit because reset() doesn't clear it;
      // safe to overwrite — the sentinel observer setup also assigns it.
      this.tryOpenPendingPhoto();
    };
  }

  /** Set by ?photoId=… query param; cleared after we've opened the modal. */
  private pendingPhotoId: string | null = null;

  private tryOpenPendingPhoto(): void {
    if (!this.pendingPhotoId) return;
    const idx = this.photos.findIndex(p => p.id === this.pendingPhotoId);
    if (idx >= 0) {
      this.openModal(idx);
      this.pendingPhotoId = null;
    }
  }

  ngAfterViewInit(): void {
    this.setupSentinelObserver();
  }

  ngOnDestroy(): void {
    this.intersectionObserver?.disconnect();
    this.intersectionObserver = null;
    this.loader.destroy();
    this.destroy$.next();
    this.destroy$.complete();
  }

  /**
   * Wires the IntersectionObserver to the bottom-of-grid sentinel. The
   * sentinel is added/removed reactively via *ngIf="loader.hasMore()", so we
   * re-attach whenever the ViewChild ref changes. Browsers without
   * IntersectionObserver fall back to a no-op — the user can still see the
   * loaded page; they just don't auto-load the next one.
   */
  private setupSentinelObserver(): void {
    if (typeof IntersectionObserver === 'undefined') return;
    this.intersectionObserver = new IntersectionObserver(entries => {
      for (const e of entries) {
        // Guard against the sentinel firing before route params landed: without
        // an albumId the fetcher would build an invalid URL. The ViewChild ref
        // re-attaches once *ngIf re-renders the sentinel after the first page.
        if (e.isIntersecting && this.albumId) {
          this.loader.loadNext();
        }
      }
    }, { rootMargin: '200px' });
    this.observeSentinelIfPresent();
    // Phase 6 progressive-load bug fix: re-evaluate the sentinel after each
    // successful page load. IntersectionObserver only fires on intersection-
    // state *transitions* — when the appended page doesn't push the sentinel
    // out of the viewport it stays "intersecting" silently, and the user's
    // scroll never reloads. Re-observing the same element forces a fresh
    // evaluation that fires loadNext again if the sentinel is still visible.
    this.loader.onLoadCompleted = () => {
      this.reobserveSentinel();
      this.tryOpenPendingPhoto();
    };
  }

  private reobserveSentinel(): void {
    const el = this.sentinelRef?.nativeElement;
    if (!this.intersectionObserver || !el) return;
    this.intersectionObserver.unobserve(el);
    this.intersectionObserver.observe(el);
  }

  private observeSentinelIfPresent(): void {
    if (this.intersectionObserver && this.sentinelRef?.nativeElement) {
      this.intersectionObserver.observe(this.sentinelRef.nativeElement);
    }
  }

  /**
   * Issue #113: per-photo delete is available to album owners and admins.
   * The current FE doesn't fetch the owner id alongside the album payload
   * for every viewer, but the backend re-authorises the delete request, so
   * surfacing the affordance to all signed-in viewers of /albums/:id is
   * safe: a non-owner click would surface a friendly error toast (see
   * onDeletePhoto's error branch). For now we gate it on Admin OR album
   * ownership (when the album payload includes ownerId).
   */
  get canDeletePhotos(): boolean {
    if (this.auth.isAdmin()) return true;
    const userId = this.auth.getUser()?.id;
    return !!userId && !!this.album && this.album.ownerId === userId;
  }

  /**
   * Issue #113: delete a single photo. Prompts the user before issuing
   * <c>DELETE /api/photos/{id}</c>. On success splices the photo out of
   * the local list (no full reload). On failure surfaces a friendly error
   * via window.alert — same pattern as onSaveToAccount.
   */
  onDeletePhoto(photo: Photo): void {
    if (!photo?.id) return;
    if (typeof confirm === 'function' &&
        !confirm(`Delete "${photo.fileName}"? This cannot be undone.`)) {
      return;
    }
    const apiUrl = environment.apiUrl || '';
    this.http.delete(`${apiUrl}/api/photos/${photo.id}`)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: () => {
          this.loader.removeWhere(p => p.id === photo.id);
        },
        error: (err) => {
          console.error('Failed to delete photo', err);
          const msg = err?.status === 403
            ? 'You do not have permission to delete this photo.'
            : err?.status === 404
              ? 'Photo not found. It may have already been deleted.'
              : 'Failed to delete photo. Please try again.';
          if (typeof alert === 'function') alert(msg);
        }
      });
  }

  private startPhotoStatusPolling(): void {
    // Poll every 2 seconds for photos that are still processing
    interval(2000)
      .pipe(
        takeUntil(this.destroy$),
        switchMap(() => this.getProcessingPhotos())
      )
      .subscribe({
        next: (processingPhotos) => {
          if (processingPhotos.length === 0) {
            // All photos done processing
            return;
          }
          // Update each photo's status
          processingPhotos.forEach(photoId => {
            this.updatePhotoStatus(photoId);
          });
        },
        error: (error) => {
          console.error('Error polling photo statuses:', error);
        }
      });
  }

  private getProcessingPhotos(): Observable<string[]> {
    // Get list of photos currently processing
    return new Observable(observer => {
      const processingIds = this.photos
        .filter(p => p.processingStatus === 'Processing')
        .map(p => p.id);
      observer.next(processingIds);
      observer.complete();
    });
  }

  private updatePhotoStatus(photoId: string): void {
    const apiUrl = environment.apiUrl || '';
    this.http.get<any>(`${apiUrl}/api/photos/${photoId}/status`)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (status) => {
          const photo = this.photos.find(p => p.id === photoId);
          if (photo) {
            // Update processingStatus based on percentComplete
            if (status.percentComplete === 100) {
              photo.processingStatus = 'Complete';
            } else {
              photo.processingStatus = 'Processing';
            }
          }
        },
        error: (error) => {
          console.error('Error fetching photo status:', error);
        }
      });
  }

  loadAlbum(): void {
    const apiUrl = environment.apiUrl || '';
    this.http.get<Album>(`${apiUrl}/api/albums/${this.albumId}`).pipe(takeUntil(this.destroy$)).subscribe({
      next: (data) => {
        this.album = data;
        this.isLoading = false;
      },
      error: (error) => {
        console.error('Error loading album:', error);
        this.errorMessage = 'Failed to load album details.';
        this.isLoading = false;
      }
    });
  }

  /**
   * Phase 6: per-page HTTP fetcher used by the {@link PhotoPageLoader}. Replaces
   * the previous "fetch all photos at once" call. The pre-signed URL cost is
   * paid only for items on the current page; older clients that don't supply
   * page/pageSize still get a safe default (page=1, size=20) per the server.
   */
  private fetchPhotoPage(page: number, pageSize: number): Observable<PhotoPage<Photo>> {
    const apiUrl = environment.apiUrl || '';
    const url = `${apiUrl}/api/albums/${this.albumId}/photos?page=${page}&pageSize=${pageSize}`;
    return this.http.get<PhotoPage<Photo>>(url);
  }

  /**
   * @deprecated Phase 6 wired the grid through {@link PhotoPageLoader}; the
   * original full-list fetch is preserved here for tests and the upload-complete
   * refresh path, which intentionally resets the loader and pulls page 1 again.
   */
  loadPhotos(): void {
    this.loader.reset();
    this.loader.loadNext();
    // Re-observe in case the sentinel was removed (hasMore was false) and the
    // *ngIf re-rendered it on the next page-1 fetch.
    queueMicrotask(() => this.observeSentinelIfPresent());
  }

  loadAccessCodes(): void {
    const apiUrl = environment.apiUrl || '';
    this.http.get<AccessCode[]>(`${apiUrl}/api/albums/${this.albumId}/access-codes`).pipe(takeUntil(this.destroy$)).subscribe({
      next: (data) => {
        this.accessCodes = data || [];
      },
      error: (error) => {
        console.error('Error loading access codes:', error);
      }
    });
  }

  toggleCodeForm(): void {
    this.showCodeForm = !this.showCodeForm;
  }

  onCodeCreated(): void {
    this.showCodeForm = false;
    this.loadAccessCodes();
  }

  deleteAccessCode(codeId: string): void {
    if (!confirm('Are you sure you want to delete this access code?')) {
      return;
    }

    const apiUrl = environment.apiUrl || '';
    this.http.delete(`${apiUrl}/api/albums/${this.albumId}/access-codes/${codeId}`).pipe(takeUntil(this.destroy$)).subscribe({
      next: () => {
        console.log('Access code deleted successfully');
        this.loadAccessCodes();
      },
      error: (error) => {
        console.error('Error deleting access code:', error);
        this.errorMessage = 'Failed to delete access code. Please try again.';
      }
    });
  }

  onUploadComplete(event: any): void {
    console.log('Upload completed:', event);
    this.loadPhotos();
  }

  // ---------------------------------------------------------------------------
  // Cart integration (issue #58)
  // ---------------------------------------------------------------------------

  /** Photos transformed for the PhotoModalComponent. */
  get modalPhotos(): ModalPhoto[] {
    return this.photos.map(p => ({
      photoId: p.id,
      fileName: p.fileName,
      thumbnailUrl: p.thumbnailUrl,
      displayUrl: p.mediumUrl ?? p.thumbnailUrl
    }));
  }

  get isModalPhotoInCart(): boolean {
    const modalPhoto = this.modalPhotos[this.modalIndex];
    if (!modalPhoto) return false;
    const quality = this.selectedQuality[modalPhoto.photoId] || 'Medium';
    return this.cart.contains(modalPhoto.photoId, quality);
  }

  openModal(index: number): void {
    this.modalIndex = index;
    this.modalOpen = true;
  }

  onQualityChange(photoId: string, q: CartQuality): void {
    this.selectedQuality[photoId] = q;
  }

  isInCart(photo: Photo): boolean {
    const quality = this.selectedQuality[photo.id] || 'Medium';
    return this.cart.contains(photo.id, quality);
  }

  /**
   * Click handler for the per-photo cart button. Toggles add ↔ remove based on
   * the current cart state (issue #108). Replaces the previous "disabled when
   * added" UX which gave users no way to undo from the card.
   */
  onCartButtonClick(photo: Photo): void {
    const quality: CartQuality = this.selectedQuality[photo.id] || 'Medium';
    if (this.cart.contains(photo.id, quality)) {
      this.cart.removeItem(photo.id, quality);
    } else {
      this.onAddToCart(photo);
    }
  }

  onAddToCart(photo: Photo): void {
    const quality: CartQuality = this.selectedQuality[photo.id] || 'Medium';
    this.cart.addItem({
      photoId: photo.id,
      fileName: photo.fileName,
      thumbnailUrl: photo.thumbnailUrl,
      quality,
      sourceAlbumId: this.albumId,
      sourceAlbumTitle: this.album?.title
    });
  }

  onModalCartAction(modalPhoto: ModalPhoto): void {
    const photo = this.photos.find(p => p.id === modalPhoto.photoId);
    if (!photo) return;
    const quality = this.selectedQuality[photo.id] || 'Medium';
    if (this.cart.contains(photo.id, quality)) {
      this.cart.removeItem(photo.id, quality);
    } else {
      this.onAddToCart(photo);
    }
  }

  /**
   * The floating Album Activity aside fires this whenever the server reports
   * either a new photo landing in the album or a freshly-completed photo's
   * variants finishing — both cases require the on-screen grid to refresh
   * so the new thumbnails appear without the user reloading. We re-arm the
   * paged loader from scratch and let auto-trickle re-paginate the album;
   * for an album of N photos at 20 per page that's N/20 fast HTTP calls
   * spaced 200ms apart, comfortably less work than the server-side processing
   * cycle that triggered it.
   */
  onAlbumActivityChanged(_summary: unknown): void {
    // Phase: smooth refresh during active uploads. We used to call
    // loader.reset() + loader.enableAutoLoad() here which nuked the photos
    // array and re-rendered from page 1 — that's what caused the visible
    // glitch on every poll while uploading. Now we do an in-place merge:
    // re-fetch the pages already loaded, keep object references for items
    // that haven't changed, and only append new items if totalCount grew.
    this.loader.refreshLoaded(p => p.id);
  }

  /** trackBy keyfn for the *ngFor on .photo-card — keeps DOM nodes stable. */
  trackByPhotoId(_index: number, photo: Photo): string { return photo.id; }

  getStatusClass(photo: Photo): string {
    if (!photo.processingStatus) return 'pending';
    
    const status = photo.processingStatus.toLowerCase();
    if (status === 'complete') return 'complete';
    if (status === 'processing') return 'processing';
    if (status === 'failed') return 'failed';
    return 'pending';
  }

  getShouldShowBadge(photo: Photo): boolean {
    return photo?.processingStatus === 'Processing' || photo?.processingStatus === 'Failed';
  }

  getCodeStatus(code: AccessCode): string {
    if (!code.expirationDate) return 'active';
    const expiration = new Date(code.expirationDate);
    return expiration > new Date() ? 'active' : 'expired';
  }

  getActiveCodes(): number {
    return this.accessCodes.filter(code => this.getCodeStatus(code) === 'active').length;
  }

  copyToClipboard(text: string): void {
    navigator.clipboard.writeText(text).then(() => {
      this.copiedCode = text;
      setTimeout(() => {
        if (this.copiedCode === text) {
          this.copiedCode = null;
        }
      }, 2000);
    }).catch(err => {
      console.error('Failed to copy:', err);
      this.errorMessage = 'Failed to copy to clipboard.';
    });
  }

  copyShareLink(code: string): void {
    // Build share link for unauthenticated client access (per requirement #4: /code/{albumcode})
    const shareUrl = `${window.location.origin}/code/${code}`;
    navigator.clipboard.writeText(shareUrl).then(() => {
      this.copiedLink = code;
      setTimeout(() => {
        if (this.copiedLink === code) {
          this.copiedLink = null;
        }
      }, 2000);
    }).catch(err => {
      console.error('Failed to copy share link:', err);
      this.errorMessage = 'Failed to copy share link.';
    });
  }

  /**
   * Defensive fallback when a thumbnail URL fails to load (HTTP 404, network error,
   * stale pre-signed URL, etc.). Clearing the URL on the photo lets the existing
   * `*ngIf="!photo.thumbnailUrl"` SVG placeholder take over so the user no longer
   * sees the browser's broken-image icon.
   *
   * The underlying root cause (cached pre-signed URL pointing at a missing storage
   * object) is fixed server-side by D008; this handler is the client-side safety net.
   */
  onThumbnailError(photo: Photo): void {
    console.warn('Thumbnail failed to load for photo', photo.id, photo.fileName);
    photo.thumbnailUrl = undefined;
  }

  // ---------------------------------------------------------------------------
  // Per-album reconciliation. POSTs to /api/photos/albums/{id}/reconcile-storage,
  // which re-scans every photo in the album for missing variants and re-queues
  // the worker to regenerate them. Owner OR admin can trigger this; the BE
  // authorizes the same way GetAlbumProcessingSummary does. The actual image
  // regeneration runs on the worker app on its next 5s tick, so the report
  // counters (requeued, backFilled) are the immediate user-visible signal.
  // ---------------------------------------------------------------------------

  reconcileState: 'idle' | 'running' | 'success' | 'error' = 'idle';
  reconcileMessage: string | null = null;

  runReconcile(): void {
    if (this.reconcileState === 'running') return;
    this.reconcileState = 'running';
    this.reconcileMessage = 'Queued for worker — picking up shortly…';
    const apiUrl = environment.apiUrl || '';
    // The endpoint is now async: returns 202 Accepted with a jobId, the
    // worker drains it within ~5s, and we poll the status endpoint until
    // it transitions to complete/error. The API replica never runs the
    // job itself — keeps it responsive to other client traffic during a
    // big reconcile pass.
    this.http.post<AdminJobEnqueued>(`${apiUrl}/api/photos/albums/${this.albumId}/reconcile-storage`, {})
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (enqueued) => {
          if (!enqueued?.jobId) {
            this.reconcileState = 'error';
            this.reconcileMessage = 'Reconcile request failed: no jobId returned.';
            return;
          }
          this.pollAdminJob(enqueued.jobId);
        },
        error: (err) => {
          this.reconcileState = 'error';
          this.reconcileMessage = err?.error?.message
            ? `Reconcile failed: ${err.error.message}`
            : 'Reconcile failed.';
        }
      });
  }

  /**
   * Poll an enqueued admin job every 2 seconds until it terminates. Stops
   * after 5 minutes — by then either the worker is dead (operator should
   * investigate via Service Health) or the job is genuinely huge and the
   * operator can re-poll later.
   */
  private pollAdminJob(jobId: string, attempt: number = 0): void {
    if (attempt > 150) {  // 150 * 2s = 5 minutes
      this.reconcileState = 'error';
      this.reconcileMessage = 'Reconcile timed out after 5 minutes. Check the admin Service Health page to see worker status.';
      return;
    }
    const apiUrl = environment.apiUrl || '';
    this.http.get<AdminJobStatus>(`${apiUrl}/api/photos/admin/jobs/${jobId}`)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (job) => {
          if (job.status === 'complete') {
            this.reconcileState = 'success';
            const r: ReconcileReport = job.result || {};
            const parts: string[] = [];
            if (r.photosScanned != null)          parts.push(`${r.photosScanned} scanned`);
            if (r.itemsRequeued)                  parts.push(`${r.itemsRequeued} requeued`);
            if (r.itemsBackFilledComplete)        parts.push(`${r.itemsBackFilledComplete} back-filled`);
            if (r.itemsCreatedPending)            parts.push(`${r.itemsCreatedPending} new pending`);
            if (r.uploadingPromotedToPending)     parts.push(`${r.uploadingPromotedToPending} uploads promoted`);
            if (r.uploadingMarkedFailed)          parts.push(`${r.uploadingMarkedFailed} stale uploads failed`);
            if (r.originalsMissing)               parts.push(`${r.originalsMissing} originals missing`);
            if (r.urlsInvalidated)                parts.push(`${r.urlsInvalidated} URLs invalidated`);
            const replica = job.completedByInstanceId ? ` (handled by ${job.completedByInstanceId})` : '';
            this.reconcileMessage = parts.length
              ? `Reconcile complete${replica}: ${parts.join(', ')}. Image-processing worker will regenerate any pending items shortly.`
              : `Reconcile complete${replica}: nothing to fix.`;
            this.loader.refreshLoaded(p => p.id);
            return;
          }
          if (job.status === 'error') {
            this.reconcileState = 'error';
            this.reconcileMessage = job.error
              ? `Reconcile failed on worker: ${job.error}`
              : 'Reconcile failed on worker.';
            return;
          }
          // pending / running — keep polling
          this.reconcileMessage = job.status === 'running'
            ? `Worker is reconciling${job.completedByInstanceId ? ` on ${job.completedByInstanceId}` : ''}…`
            : 'Queued for worker — picking up shortly…';
          setTimeout(() => this.pollAdminJob(jobId, attempt + 1), 2000);
        },
        error: () => {
          // Transient — back off and retry. After 5 consecutive failures, bail.
          if (attempt > 5) {
            this.reconcileState = 'error';
            this.reconcileMessage = 'Lost contact with the API while waiting for the worker. Try again in a moment.';
            return;
          }
          setTimeout(() => this.pollAdminJob(jobId, attempt + 1), 3000);
        }
      });
  }

  /** Photos transformed for the PhotoModalComponent. (single definition — see Cart integration block) */
}

interface AdminJobEnqueued {
  jobId: string;
  jobType: string;
  status: string;
  requestedAt: string;
  statusUrl?: string;
}

interface AdminJobStatus {
  jobId: string;
  jobType: string;
  status: 'pending' | 'running' | 'complete' | 'error';
  requestedAt: string;
  requestedBy?: string | null;
  startedAt?: string | null;
  completedAt?: string | null;
  completedByInstanceId?: string | null;
  result?: ReconcileReport | null;
  error?: string | null;
}

interface ReconcileReport {
  photosScanned?: number;
  itemsCreatedPending?: number;
  itemsBackFilledComplete?: number;
  itemsRequeued?: number;
  originalsMissing?: number;
  queuesCreated?: number;
  urlsInvalidated?: number;
  uploadingPromotedToPending?: number;
  uploadingMarkedFailed?: number;
}
