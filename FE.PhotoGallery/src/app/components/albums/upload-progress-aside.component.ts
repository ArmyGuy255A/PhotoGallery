import { Component, Input, OnDestroy, OnInit, ChangeDetectionStrategy, signal } from '@angular/core';
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
      *ngIf="visible() && !dismissed()"
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
          (click)="dismissed.set(true)"
          aria-label="Hide progress panel"
          data-testid="upload-aside-dismiss">×</button>
      </header>
      <div class="aside-body" *ngIf="summary() as s">
        <div class="row" *ngIf="s.photoStatus.uploading > 0">
          <span class="dot dot-uploading"></span>
          <span class="label">Uploading</span>
          <span class="count" data-testid="upload-aside-uploading">{{ s.photoStatus.uploading }}</span>
        </div>
        <div class="row" *ngIf="s.photoStatus.pending > 0">
          <span class="dot dot-pending"></span>
          <span class="label">Queued</span>
          <span class="count" data-testid="upload-aside-pending">{{ s.photoStatus.pending }}</span>
        </div>
        <div class="row" *ngIf="s.photoStatus.processing > 0">
          <span class="dot dot-processing"></span>
          <span class="label">Processing</span>
          <span class="count" data-testid="upload-aside-processing">{{ s.photoStatus.processing }}</span>
        </div>
        <div class="row row-summary" *ngIf="s.photoStatus.complete > 0">
          <span class="dot dot-complete"></span>
          <span class="label">Complete</span>
          <span class="count" data-testid="upload-aside-complete">{{ s.photoStatus.complete }} / {{ s.totalPhotos }}</span>
        </div>
        <div class="row row-error" *ngIf="s.photoStatus.failed > 0">
          <span class="dot dot-failed"></span>
          <span class="label">Failed</span>
          <span class="count" data-testid="upload-aside-failed">{{ s.photoStatus.failed }}</span>
        </div>

        <div class="quality-section" *ngIf="hasQualityWork(s)">
          <small class="section-title">Variants pending</small>
          <div class="quality-row" *ngIf="anyQualityWork(s.byQuality.thumbnail)">
            <span class="quality-label">Thumbnail</span>
            <span class="quality-bar">
              <span class="quality-fill" [style.width.%]="pctComplete(s.byQuality.thumbnail)"></span>
            </span>
            <span class="quality-count">{{ s.byQuality.thumbnail.complete }} / {{ qualityTotal(s.byQuality.thumbnail) }}</span>
          </div>
          <div class="quality-row" *ngIf="anyQualityWork(s.byQuality.low)">
            <span class="quality-label">Low</span>
            <span class="quality-bar">
              <span class="quality-fill" [style.width.%]="pctComplete(s.byQuality.low)"></span>
            </span>
            <span class="quality-count">{{ s.byQuality.low.complete }} / {{ qualityTotal(s.byQuality.low) }}</span>
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
        </div>
      </div>
    </aside>
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
    .aside-body { padding: 12px 14px; display: flex; flex-direction: column; gap: 6px; }
    .row {
      display: grid;
      grid-template-columns: 14px 1fr auto;
      gap: 8px;
      align-items: center;
    }
    .row .label { color: #374151; }
    .row .count { color: #111827; font-weight: 600; font-variant-numeric: tabular-nums; }
    .row-summary .label, .row-summary .count { color: #047857; }
    .row-error .label, .row-error .count { color: #b91c1c; }
    .dot {
      width: 10px;
      height: 10px;
      border-radius: 50%;
      display: inline-block;
    }
    .dot-uploading { background: #0d6efd; }
    .dot-pending { background: #9ca3af; }
    .dot-processing { background: #f39c12; }
    .dot-complete { background: #10b981; }
    .dot-failed { background: #ef4444; }

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

  readonly summary = signal<AlbumProcessingSummary | null>(null);
  readonly visible = signal<boolean>(false);
  readonly dismissed = signal<boolean>(false);

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
        this.summary.set(s);
        const inflight =
          s.photoStatus.uploading + s.photoStatus.pending + s.photoStatus.processing;
        const failedRecent = s.photoStatus.failed > 0;
        this.visible.set(inflight > 0 || failedRecent);
        if (!this.visible()) this.dismissed.set(false); // re-arm for next batch
      });
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
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
