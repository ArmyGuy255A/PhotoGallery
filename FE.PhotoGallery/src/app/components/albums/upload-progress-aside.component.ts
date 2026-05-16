import { Component, Input, OnDestroy, OnInit, ChangeDetectionStrategy, signal, Output, EventEmitter } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Subject, interval, takeUntil, switchMap, catchError, of, startWith } from 'rxjs';
import { PhotoService, AlbumProcessingSummary } from '../../services/photo.service';

/**
 * Floating bottom-right widget that shows aggregate album-wide
 * upload / processing counts. Replaces the per-file polling primitive
 * for albums with hundreds of photos in flight — one HTTP call per tick
 * regardless of batch size.
 *
 * Hides itself automatically when nothing is in flight (uploading +
 * pending + processing all zero, and per-quality work all settled).
 *
 * Polls <c>GET /api/photos/albums/{albumId}/processing-summary</c> every
 * <c>refreshMs</c> (default 5s) while visible.
 */
@Component({
  selector: 'app-upload-progress-aside',
  standalone: true,
  imports: [CommonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <aside
      *ngIf="expanded()"
      class="upload-aside"
      data-testid="upload-progress-aside"
      role="status"
      aria-live="polite"
      aria-label="Album processing progress">
      <header class="aside-head">
        <span class="aside-title">📥 Album activity</span>
        <button
          type="button"
          class="aside-dismiss"
          (click)="expanded.set(false)"
          aria-label="Hide progress panel"
          data-testid="upload-aside-dismiss">×</button>
      </header>
      <div class="aside-body" *ngIf="summary() as s">
        <!-- 3 headline totals: Uploaded (finished hitting storage),
             Processing (still being worked on), Complete (all variants done). -->
        <div class="totals-grid">
          <div class="total-item">
            <span class="total-label">Uploaded</span>
            <span class="total-value" data-testid="upload-aside-uploaded">{{ uploadedCount(s) }}</span>
          </div>
          <div class="total-item">
            <span class="total-label">Processing</span>
            <span class="total-value" data-testid="upload-aside-processing">{{ processingTotal(s) }}</span>
          </div>
          <div class="total-item">
            <span class="total-label">Complete</span>
            <span class="total-value" data-testid="upload-aside-complete">{{ s.photoStatus.complete }}</span>
          </div>
        </div>

        <div class="row row-uploading" *ngIf="s.photoStatus.uploading > 0">
          <span class="label">In-flight uploads</span>
          <span class="count" data-testid="upload-aside-uploading">{{ s.photoStatus.uploading }}</span>
        </div>
        <div class="row row-error" *ngIf="s.photoStatus.failed > 0">
          <span class="label">Failed</span>
          <span class="count" data-testid="upload-aside-failed">{{ s.photoStatus.failed }}</span>
        </div>

        <div class="quality-section" *ngIf="hasQualityWork(s)">
          <small class="section-title">Variants (priority order)</small>
          <div class="quality-row" *ngIf="anyQualityWork(s.byQuality.thumbnail)">
            <span class="quality-label">Thumbnail</span>
            <span class="quality-bar">
              <span class="quality-fill" [style.width.%]="pctComplete(s.byQuality.thumbnail)"></span>
            </span>
            <span class="quality-count">{{ s.byQuality.thumbnail.complete }} / {{ qualityTotal(s.byQuality.thumbnail) }}</span>
          </div>
          <div class="quality-row" *ngIf="anyQualityWork(s.byQuality.medium)">
            <span class="quality-label">Medium</span>
            <span class="quality-bar">
              <span class="quality-fill" [style.width.%]="pctComplete(s.byQuality.medium)"></span>
            </span>
            <span class="quality-count">{{ s.byQuality.medium.complete }} / {{ qualityTotal(s.byQuality.medium) }}</span>
          </div>
          <div class="quality-row" *ngIf="anyQualityWork(s.byQuality.high)">
            <span class="quality-label">High</span>
            <span class="quality-bar">
              <span class="quality-fill" [style.width.%]="pctComplete(s.byQuality.high)"></span>
            </span>
            <span class="quality-count">{{ s.byQuality.high.complete }} / {{ qualityTotal(s.byQuality.high) }}</span>
          </div>
          <div class="quality-row" *ngIf="anyQualityWork(s.byQuality.low)">
            <span class="quality-label">Low</span>
            <span class="quality-bar">
              <span class="quality-fill" [style.width.%]="pctComplete(s.byQuality.low)"></span>
            </span>
            <span class="quality-count">{{ s.byQuality.low.complete }} / {{ qualityTotal(s.byQuality.low) }}</span>
          </div>
        </div>
      </div>
    </aside>

    <!-- Always-present pill: opens the full panel. Sits at the same
         bottom-right anchor as the expanded aside; one is shown at a
         time via expanded(). The pill carries an active-work badge so
         users can see at a glance whether something needs attention
         without having to open the panel. -->
    <button
      *ngIf="!expanded() && albumId"
      type="button"
      class="upload-reshow-pill"
      [class.pill-active]="activeCount(summary()) > 0"
      data-testid="upload-progress-reshow"
      (click)="expanded.set(true)"
      [attr.aria-label]="activeCount(summary()) > 0
        ? 'Show album activity (' + activeCount(summary()) + ' in progress)'
        : 'Show album activity'">
      📥 Album activity<ng-container *ngIf="activeCount(summary()) > 0"> ({{ activeCount(summary()) }})</ng-container>
    </button>
  `,
  styles: [`
    .upload-aside {
      position: fixed;
      bottom: 20px;
      right: 20px;
      width: 280px;
      max-width: calc(100vw - 40px);
      background: #ffffff;
      border: 1px solid #e0e6ed;
      border-radius: 12px;
      box-shadow: 0 8px 28px rgba(0, 0, 0, 0.18);
      z-index: 1500;
      font-size: 13px;
      overflow: hidden;
    }
    .aside-head {
      display: flex;
      justify-content: space-between;
      align-items: center;
      padding: 10px 14px;
      background: #f4f7fb;
      border-bottom: 1px solid #e0e6ed;
    }
    .aside-title { font-weight: 600; color: #1f2937; }
    .aside-dismiss {
      background: transparent;
      border: none;
      font-size: 18px;
      line-height: 1;
      color: #6b7280;
      cursor: pointer;
      padding: 0 4px;
    }
    .aside-dismiss:hover { color: #111827; }
    .aside-body { padding: 12px 14px; display: flex; flex-direction: column; gap: 8px; }

    .totals-grid {
      display: grid;
      grid-template-columns: repeat(3, 1fr);
      gap: 8px;
      padding-bottom: 8px;
      border-bottom: 1px solid #f0f1f3;
    }
    .total-item {
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: 2px;
    }
    .total-label {
      color: #6b7280;
      font-size: 10px;
      text-transform: uppercase;
      letter-spacing: 0.05em;
    }
    .total-value {
      color: #111827;
      font-size: 22px;
      font-weight: 700;
      font-variant-numeric: tabular-nums;
    }

    .upload-reshow-pill {
      position: fixed;
      bottom: 20px;
      right: 20px;
      z-index: 1500;
      padding: 10px 16px;
      background: #1f2937;
      color: #ffffff;
      border: none;
      border-radius: 999px;
      font-size: 13px;
      font-weight: 600;
      cursor: pointer;
      box-shadow: 0 6px 20px rgba(0, 0, 0, 0.18);
    }
    .upload-reshow-pill:hover { background: #111827; }
    .upload-reshow-pill.pill-active {
      background: #0d6efd;
      animation: pill-pulse 2s ease-in-out infinite;
    }
    .upload-reshow-pill.pill-active:hover { background: #0b5ed7; }
    @keyframes pill-pulse {
      0%, 100% { box-shadow: 0 6px 20px rgba(13, 110, 253, 0.35); }
      50%      { box-shadow: 0 6px 28px rgba(13, 110, 253, 0.6); }
    }
    .row {
      display: grid;
      grid-template-columns: 1fr auto;
      gap: 8px;
      align-items: center;
    }
    .row .label { color: #374151; }
    .row .count { color: #111827; font-weight: 600; font-variant-numeric: tabular-nums; }
    .row-uploading .label, .row-uploading .count { color: #1d4ed8; }
    .row-error .label, .row-error .count { color: #b91c1c; }

    .quality-section {
      margin-top: 8px;
      padding-top: 8px;
      border-top: 1px dashed #e0e6ed;
      display: flex;
      flex-direction: column;
      gap: 6px;
    }
    .section-title { color: #6b7280; text-transform: uppercase; font-size: 10px; letter-spacing: 0.05em; }
    .quality-row {
      display: grid;
      grid-template-columns: 70px 1fr auto;
      gap: 8px;
      align-items: center;
      font-size: 12px;
    }
    .quality-label { color: #374151; }
    .quality-bar {
      height: 6px;
      background: #e5e7eb;
      border-radius: 999px;
      overflow: hidden;
    }
    .quality-fill {
      display: block;
      height: 100%;
      background: linear-gradient(90deg, #f39c12, #10b981);
      transition: width 0.4s ease;
    }
    .quality-count { color: #6b7280; font-variant-numeric: tabular-nums; }
  `]
})
export class UploadProgressAsideComponent implements OnInit, OnDestroy {
  /** Album the widget is summarising. Required. */
  @Input({ required: true }) albumId!: string;

  /** Polling interval (ms). 5s by default. */
  @Input() refreshMs: number = 5000;

  /**
   * Fires when the summary indicates new photo work has progressed —
   * specifically when totalPhotos grows (new uploads landed) or Complete
   * grows (server finished a photo's variants). Album-detail listens to
   * trigger a refresh of the on-screen photo grid so freshly-completed
   * thumbnails appear without the user reloading.
   */
  @Output() summaryChanged = new EventEmitter<AlbumProcessingSummary>();

  readonly summary = signal<AlbumProcessingSummary | null>(null);
  /**
   * Single source of truth for whether the full panel is rendered.
   *   - Starts false → user sees the pill only.
   *   - Auto-flips true when activity transitions 0 → >0 (new uploads
   *     land or the failed count spikes).
   *   - User × button or pill click flips it manually.
   *   - Does NOT auto-flip false on activity ending — the pill stays
   *     visible so the user can always reopen the panel.
   */
  readonly expanded = signal<boolean>(false);

  private readonly destroy$ = new Subject<void>();

  constructor(private photoService: PhotoService) {}

  ngOnInit(): void {
    interval(this.refreshMs)
      .pipe(
        startWith(0),
        takeUntil(this.destroy$),
        switchMap(() => this.photoService.getAlbumProcessingSummary(this.albumId).pipe(
          catchError(err => {
            console.warn('[UploadProgressAside] poll failed', err);
            return of(null);
          })
        ))
      )
      .subscribe(s => {
        if (!s) return;
        const prev = this.summary();
        this.summary.set(s);

        // Emit on edges that matter for the album-detail refresh:
        //  - a fresh photo has landed (totalPhotos grew)
        //  - the server finished another photo's variants (Complete grew)
        const prevTotal = prev?.totalPhotos ?? 0;
        const prevComplete = prev?.photoStatus.complete ?? 0;
        if (s.totalPhotos > prevTotal || s.photoStatus.complete > prevComplete) {
          this.summaryChanged.emit(s);
        }

        // Auto-expand on the no-activity -> activity edge so a fresh upload
        // batch surfaces the panel without the user having to click. Never
        // auto-collapse — the pill stays visible so the user can always
        // reopen the panel after the batch settles.
        const prevActive = this.activeCount(prev);
        const nowActive = this.activeCount(s);
        if (prevActive === 0 && nowActive > 0) {
          this.expanded.set(true);
        }
      });
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  /** Photos that finished hitting storage (total minus still-uploading). */
  uploadedCount(s: AlbumProcessingSummary): number {
    return Math.max(0, s.totalPhotos - s.photoStatus.uploading);
  }

  /** Photos still being worked on by the server (pending + processing). */
  processingTotal(s: AlbumProcessingSummary): number {
    return s.photoStatus.pending + s.photoStatus.processing;
  }

  /** Aggregate active count for the re-show pill. */
  activeCount(s: AlbumProcessingSummary | null): number {
    if (!s) return 0;
    return s.photoStatus.uploading + s.photoStatus.pending + s.photoStatus.processing + s.photoStatus.failed;
  }

  anyQualityWork(q: { pending: number; processing: number; complete: number; failed: number }): boolean {
    return q.pending + q.processing + q.complete + q.failed > 0;
  }

  hasQualityWork(s: AlbumProcessingSummary): boolean {
    return this.anyQualityWork(s.byQuality.thumbnail)
      || this.anyQualityWork(s.byQuality.low)
      || this.anyQualityWork(s.byQuality.medium)
      || this.anyQualityWork(s.byQuality.high);
  }

  qualityTotal(q: { pending: number; processing: number; complete: number; failed: number }): number {
    return q.pending + q.processing + q.complete + q.failed;
  }

  pctComplete(q: { pending: number; processing: number; complete: number; failed: number }): number {
    const total = this.qualityTotal(q);
    if (total === 0) return 0;
    return Math.round((q.complete / total) * 100);
  }
}
