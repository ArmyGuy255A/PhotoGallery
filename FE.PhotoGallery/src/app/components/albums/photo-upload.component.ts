import { Component, Input, Output, EventEmitter, OnInit, OnDestroy, effect, inject, Injector, runInInjectionContext, EffectRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { PhotoService, UploadProgress } from '../../services/photo.service';
import { PhotoProgressService, QUALITIES, Quality } from '../../services/photo-progress.service';
import { Subject, interval, takeUntil } from 'rxjs';

interface UploadFile {
  file: File;
  uploadProgress: number;
  processingProgress: number;
  perQuality: Partial<Record<Quality, number>>;
  status: 'pending' | 'uploading' | 'complete' | 'error' | 'processing' | 'duplicate';
  errorMessage?: string;
  photoId?: string;
  processingStatus?: string;
  thumbnailUrl?: string;
  completeTime?: Date;
  /** Cleanup hook for the effect bound to this row's photoId. */
  effectRef?: EffectRef;
}

@Component({
  selector: 'app-photo-upload',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="upload-card" data-testid="photo-upload-component">
      <div class="card-header">
        <strong>Upload Photos</strong>
      </div>
      <div class="card-body">
        <!-- Drag and drop zone -->
        <div
          class="upload-zone"
          data-testid="photo-upload-zone"
          (dragover)="onDragOver($event)"
          (dragleave)="onDragLeave($event)"
          (drop)="onDrop($event)"
          [class.dragover]="isDraggingOver"
        >
          <p class="text-muted mb-3">
            💡 Drag and drop photos here or click to select
          </p>
          <input
            #fileInput
            type="file"
            multiple
            accept="image/*,.raw,.cr2,.nef"
            data-testid="photo-upload-input"
            (change)="onFileSelected($event)"
            style="display: none"
          />
          <button
            class="btn btn-primary"
            data-testid="photo-upload-choose-files"
            (click)="fileInput.click()"
            [disabled]="isUploading"
          >
            Choose Files
          </button>
        </div>

        <!-- Upload progress with thumbnails -->
        <div *ngIf="uploadFiles.length > 0" class="mt-4" data-testid="upload-progress-list">
          <h6>Upload Progress</h6>
          <div *ngFor="let item of uploadFiles; let i = index" class="upload-item mb-3" [class.completed]="item.status === 'complete'" data-testid="upload-item" [attr.data-upload-status]="item.status">
            <div class="item-row d-flex align-items-center gap-3">
              <!-- Thumbnail -->
              <div class="thumbnail-container">
                <img
                  *ngIf="item.thumbnailUrl && item.status === 'complete'"
                  [src]="item.thumbnailUrl"
                  alt="thumbnail"
                  class="thumbnail"
                  data-testid="upload-item-thumbnail"
                  [title]="item.file.name"
                  (error)="onThumbnailError(item)"
                />
                <div *ngIf="!item.thumbnailUrl || item.status !== 'complete'" class="thumbnail-placeholder" data-testid="upload-item-thumbnail-placeholder">
                  📷
                </div>
              </div>

              <!-- File info and progress -->
              <div class="flex-grow-1">
                <small class="d-block mb-1 filename" data-testid="upload-item-filename">{{ item.file.name }}</small>

                <!-- Single progress bar: blue while uploading, yellow while
                     processing, orange "skipped" stripe for already-in-album
                     duplicates. Width drives off uploadProgress during the
                     upload phase, processingProgress once the server is
                     working on it, and 100 for terminal states. -->
                <div class="progress single-progress">
                  <div
                    class="progress-bar"
                    [class.progress-bar-upload]="item.status === 'uploading' || item.status === 'pending'"
                    [class.progress-bar-processing]="item.status === 'processing'"
                    [class.progress-bar-complete]="item.status === 'complete'"
                    [class.progress-bar-duplicate]="item.status === 'duplicate'"
                    [class.progress-bar-error]="item.status === 'error'"
                    [style.width.%]="getDisplayProgress(item)"
                    role="progressbar">
                  </div>
                </div>

                <!-- Status text -->
                <small class="status-text" data-testid="upload-item-status">
                  <span *ngIf="item.status === 'uploading'">📤 Uploading {{ item.uploadProgress }}%</span>
                  <span *ngIf="item.status === 'processing'">🔄 Processing {{ item.processingProgress }}%</span>
                  <span *ngIf="item.status === 'complete'">✅ Complete</span>
                  <span *ngIf="item.status === 'duplicate'">⚠️ Already in album — skipped</span>
                  <span *ngIf="item.status === 'error'">❌ Error</span>
                </small>
                <small *ngIf="item.errorMessage" class="error-message">{{ item.errorMessage }}</small>
                <button
                  *ngIf="item.status === 'error'"
                  type="button"
                  class="btn btn-sm btn-outline-danger retry-btn"
                  data-testid="upload-item-retry"
                  (click)="retryUpload(item)">
                  Retry
                </button>
              </div>

              <!-- Status badge -->
              <div class="status-badge">
                <span
                  *ngIf="item.status === 'complete'"
                  class="badge bg-success"
                >
                  ✓
                </span>
                <span
                  *ngIf="item.status === 'duplicate'"
                  class="badge bg-warning"
                  title="Already in album"
                >
                  ⚠
                </span>
                <span
                  *ngIf="item.status === 'error'"
                  class="badge bg-danger"
                  [title]="item.errorMessage"
                >
                  ✗
                </span>
                <span
                  *ngIf="item.status === 'uploading'"
                  class="spinner-border spinner-border-sm text-primary"
                ></span>
                <span
                  *ngIf="item.status === 'processing'"
                  class="spinner-border spinner-border-sm text-warning"
                ></span>
              </div>
            </div>
          </div>
        </div>

        <!-- Summary -->
        <div *ngIf="uploadCompleteFlag" class="alert alert-info mt-4" data-testid="upload-summary">
          <strong>Upload Summary:</strong>
          <p class="mb-0">
            {{ successCount }} photo(s) uploaded successfully,
            {{ processingCount }} processing,
            {{ duplicateCount }} already in album,
            {{ errorCount }} failed
          </p>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .upload-card {
      border: 1px solid #e0e6ed;
      border-radius: 8px;
      background: white;
      box-shadow: 0 2px 8px rgba(0, 0, 0, 0.1);
    }

    .card-header {
      padding: 16px;
      border-bottom: 1px solid #e0e6ed;
      background: #f9f9f9;
      border-radius: 8px 8px 0 0;
    }

    .card-body {
      padding: 16px;
    }

    .upload-zone {
      border: 2px dashed #ccc;
      border-radius: 8px;
      padding: 40px;
      text-align: center;
      cursor: pointer;
      transition: all 0.3s ease;
      background-color: #f9f9f9;
    }

    .upload-zone.dragover {
      border-color: #0d6efd;
      background-color: #e7f1ff;
    }

    .btn {
      padding: 10px 20px;
      background: #27ae60;
      color: white;
      border: none;
      border-radius: 4px;
      cursor: pointer;
      font-size: 14px;
      transition: background 0.3s;
    }

    .btn:hover {
      background: #229954;
    }

    .btn:disabled {
      background: #bdc3c7;
      cursor: not-allowed;
    }

    /* Upload Item */
    .upload-item {
      padding: 12px;
      border: 1px solid #e0e6ed;
      border-radius: 6px;
      background: white;
      transition: all 0.3s ease;
    }

    .upload-item.completed {
      opacity: 0.7;
      animation: fadeOut 5s ease-in-out 3s forwards;
    }

    @keyframes fadeOut {
      0% {
        opacity: 0.7;
        transform: translateY(0);
      }
      100% {
        opacity: 0;
        transform: translateY(-10px);
        max-height: 0;
        padding: 0;
        border: none;
        margin: 0;
      }
    }

    .item-row {
      display: flex;
      align-items: center;
      gap: 12px;
    }

    /* Thumbnail */
    .thumbnail-container {
      flex-shrink: 0;
      width: 60px;
      height: 60px;
    }

    .thumbnail {
      width: 100%;
      height: 100%;
      object-fit: cover;
      border-radius: 4px;
      border: 1px solid #ddd;
    }

    .thumbnail-placeholder {
      width: 100%;
      height: 100%;
      background: #f0f0f0;
      border: 1px solid #ddd;
      border-radius: 4px;
      display: flex;
      align-items: center;
      justify-content: center;
      font-size: 24px;
      color: #999;
    }

    /* Single progress bar (replaces the old dual-overlay scheme). */
    .single-progress {
      height: 18px;
      background: #e9ecef;
      border-radius: 4px;
      overflow: hidden;
      margin: 6px 0;
    }

    .progress-bar {
      height: 100%;
      transition: width 0.3s ease, background-color 0.3s ease;
      display: flex;
      align-items: center;
      justify-content: center;
      font-size: 11px;
      font-weight: 600;
      color: white;
      text-shadow: 0 1px 2px rgba(0, 0, 0, 0.2);
    }

    .progress-bar-upload {
      background: #0d6efd; /* blue */
    }

    .progress-bar-processing {
      background: #f39c12; /* yellow / amber */
    }

    .progress-bar-complete {
      background: #27ae60; /* green */
    }

    .progress-bar-error {
      background: #e74c3c; /* red */
    }

    .progress-bar-duplicate {
      background: repeating-linear-gradient(
        45deg,
        #f39c12,
        #f39c12 8px,
        #e67e22 8px,
        #e67e22 16px
      );
    }

    .filename {
      font-weight: 500;
      color: #333;
      word-break: break-word;
    }

    .status-text {
      display: block;
      margin-top: 4px;
      color: #666;
      font-size: 0.8rem;
    }

    .error-message {
      display: block;
      color: #e74c3c;
      margin-top: 2px;
      font-size: 0.8rem;
    }

    .status-badge {
      flex-shrink: 0;
      min-width: 32px;
      text-align: center;
    }

    .badge {
      padding: 4px 8px;
      border-radius: 4px;
      font-size: 12px;
      font-weight: 600;
      color: white;
      display: inline-block;
    }

    .bg-success {
      background: #27ae60;
    }

    .bg-danger {
      background: #e74c3c;
    }

    .bg-warning {
      background: #f39c12;
    }

    .spinner-border {
      display: inline-block;
      width: 1.2rem;
      height: 1.2rem;
      vertical-align: -0.25em;
      border: 0.25em solid currentColor;
      border-right-color: transparent;
      border-radius: 50%;
      animation: spinner-border 0.75s linear infinite;
    }

    .spinner-border-sm {
      width: 1rem;
      height: 1rem;
      border-width: 0.2em;
    }

    .text-primary {
      color: #0d6efd;
    }

    .text-warning {
      color: #f39c12;
    }

    @keyframes spinner-border {
      to {
        transform: rotate(360deg);
      }
    }

    .gap-3 {
      gap: 12px;
    }

    .mt-4 {
      margin-top: 1.5rem;
    }

    .mb-3 {
      margin-bottom: 1rem;
    }

    .mb-1 {
      margin-bottom: 0.25rem;
    }

    .mt-1 {
      margin-top: 0.25rem;
    }

    .mb-0 {
      margin-bottom: 0;
    }

    .d-block {
      display: block;
    }

    .d-flex {
      display: flex;
    }

    .align-items-center {
      align-items: center;
    }

    .flex-grow-1 {
      flex-grow: 1;
      min-width: 0;
    }

    .text-muted {
      color: #6c757d;
    }

    .alert {
      padding: 12px 16px;
      border-radius: 4px;
      border: 1px solid;
    }

    .alert-info {
      background: #d1ecf1;
      border-color: #bee5eb;
      color: #0c5460;
    }

    /* Quality-bars block removed — single-progress UI consumes the
       SignalR per-quality data via getDisplayProgress(). */
    .retry-btn {
      margin-top: 4px;
      padding: 2px 10px;
      background: transparent;
      color: #e74c3c;
      border: 1px solid #e74c3c;
      border-radius: 4px;
      font-size: 0.8rem;
      cursor: pointer;
    }
    .retry-btn:hover {
      background: #e74c3c;
      color: white;
    }
  `]
})
export class PhotoUploadComponent implements OnInit, OnDestroy {
  @Input() albumId: string = '';
  @Output() uploadComplete = new EventEmitter<any>();

  /** Per-quality bar labels rendered in template order. */
  readonly qualities = QUALITIES;

  uploadFiles: UploadFile[] = [];
  isDraggingOver = false;
  isUploading = false;
  uploadCompleteFlag = false;
  successCount = 0;
  processingCount = 0;
  errorCount = 0;
  duplicateCount = 0;
  private destroy$ = new Subject<void>();

  private readonly progressService = inject(PhotoProgressService);
  private readonly injector = inject(Injector);

  constructor(private photoService: PhotoService) {}

  ngOnInit() {
    // Open the SignalR hub once for the lifetime of the upload view. Errors
    // are non-fatal: the batch-status fallback below will catch up the UI
    // even if events never arrive. The per-file polling fallback was DoS'ing
    // the browser at scale, so we explicitly do not restore it.
    this.progressService.start().catch(err =>
      console.error('[PhotoUpload] PhotoProgressService failed to start', err)
    );

    // Defense-in-depth: every 10s, if any file is still in 'processing'
    // status, batch-poll the BE for the current state of all in-flight
    // photos. ONE HTTP call regardless of batch size, so this scales
    // cleanly to hundreds of files. SignalR remains the primary push
    // channel; this only catches dropped events / silent connection loss.
    interval(10000).pipe(takeUntil(this.destroy$)).subscribe(() => {
      this.runBatchStatusFallback();
    });
  }

  private runBatchStatusFallback(): void {
    const inflight = this.uploadFiles.filter(
      f => f.status === 'processing' && !!f.photoId
    );
    if (inflight.length === 0) return;
    const ids = inflight.map(f => f.photoId!) as string[];
    this.photoService.getBatchProcessingStatus(ids).subscribe({
      next: snapshots => {
        for (const snap of snapshots) {
          const item = this.uploadFiles.find(f => f.photoId === snap.photoId);
          if (!item || item.status !== 'processing') continue;
          // Map server's 4-version progress (25/50/75/100) onto the
          // single bar. Only goes up — never regresses.
          const pct = snap.percentComplete ?? 0;
          if (pct > (item.processingProgress ?? 0)) {
            item.processingProgress = pct;
          }
          if (snap.status === 'Complete' && item.status === 'processing') {
            item.status = 'complete';
            item.processingProgress = 100;
            item.thumbnailUrl = this.photoService.getThumbnailUrl(snap.photoId);
            item.completeTime = new Date();
            this.processingCount = Math.max(0, this.processingCount - 1);
            this.successCount++;
            item.effectRef?.destroy();
            item.effectRef = undefined;
            this.checkUploadComplete();
            setTimeout(() => this.removeCompletedItem(item), 5000);
          }
        }
      },
      error: err => console.warn('[PhotoUpload] batch-status poll failed', err)
    });
  }

  ngOnDestroy() {
    this.destroy$.next();
    this.destroy$.complete();
    for (const item of this.uploadFiles) {
      item.effectRef?.destroy();
    }
  }

  /**
   * Defensive fallback when the in-component upload thumbnail fails to render
   * (server returned a stale pre-signed URL, network blip, etc.). Clearing the
   * URL on the upload item swaps in the existing 📷 placeholder so the user
   * never sees the browser's broken-image icon.
   */
  /**
   * Drives the width of the single progress bar. Maps the row's lifecycle
   * onto one 0-100 number:
   *   - pending/uploading → upload byte progress
   *   - processing        → aggregated SignalR per-quality progress
   *   - complete          → 100
   *   - error             → 100 (so the red bar fills the track)
   */
  getDisplayProgress(item: UploadFile): number {
    switch (item.status) {
      case 'complete':
      case 'duplicate':
      case 'error':
        return 100;
      case 'processing':
        return item.processingProgress || 0;
      case 'uploading':
      case 'pending':
      default:
        return item.uploadProgress || 0;
    }
  }

  onThumbnailError(item: UploadFile): void {
    console.warn('Upload thumbnail failed to load for', item.file.name, item.photoId);
    item.thumbnailUrl = undefined;
  }

  onDragOver(event: DragEvent) {
    event.preventDefault();
    this.isDraggingOver = true;
  }

  onDragLeave(event: DragEvent) {
    event.preventDefault();
    this.isDraggingOver = false;
  }

  onDrop(event: DragEvent) {
    event.preventDefault();
    this.isDraggingOver = false;

    const files = event.dataTransfer?.files;
    if (files) {
      this.handleFiles(files);
    }
  }

  onFileSelected(event: Event) {
    const input = event.target as HTMLInputElement;
    if (input.files) {
      this.handleFiles(input.files);
    }
  }

  private handleFiles(files: FileList) {
    const fileArray = Array.from(files);
    fileArray.forEach(file => {
      this.uploadFiles.push({
        file,
        uploadProgress: 0,
        processingProgress: 0,
        perQuality: {},
        status: 'pending'
      });
    });

    this.uploadAllFiles();
  }

  private uploadAllFiles() {
    if (!this.albumId) {
      alert('Album ID is required');
      return;
    }

    this.isUploading = true;
    this.uploadCompleteFlag = false;
    this.successCount = 0;
    this.processingCount = 0;
    this.errorCount = 0;
    this.duplicateCount = 0;

    // Upload files in parallel
    this.uploadFiles.forEach((item, index) => {
      this.uploadFile(item, index);
    });
  }

  private uploadFile(item: UploadFile, index: number) {
    item.status = 'uploading';
    console.log(`[PhotoUpload] Starting upload for file ${index}: ${item.file.name}, albumId: ${this.albumId}`);

    // Phase 2 direct-to-blob flow: PhotoService emits a stream of
    // UploadProgress events ending in either { phase: 'queued' } (server
    // accepted, scheduled processing) or { phase: 'error' }. The status
    // polling path below remains the source of truth for per-quality
    // progress — we just need to flip status to 'processing' once 'queued'
    // arrives so the poll picks it up.
    this.photoService.uploadPhoto(this.albumId, item.file).subscribe({
      next: (progress: UploadProgress) => {
        switch (progress.phase) {
          case 'ticket':
            // Sent the upload-ticket request; still showing 0% progress.
            break;
          case 'uploading':
            item.photoId = progress.photoId;
            item.uploadProgress = progress.bytesTotal > 0
              ? Math.round((progress.bytesSent / progress.bytesTotal) * 100)
              : 0;
            break;
          case 'completing':
            item.photoId = progress.photoId;
            item.uploadProgress = 100;
            break;
          case 'queued':
            item.photoId = progress.photoId;
            item.uploadProgress = 100;
            item.status = 'processing';
            item.processingProgress = 0;
            this.processingCount++;
            if (item.photoId) {
              this.subscribeToProcessing(item);
              // Nudge the SignalR hub to push the current snapshot for this
              // photoId. If processing finished in the brief window between
              // /upload-complete and our effect() subscription, the snapshot
              // tells us so without us needing to wait for an event that
              // already fired. One round-trip per upload — no polling DoS.
              this.progressService.requestStatus(item.photoId).catch(err =>
                console.warn('[PhotoUpload] RequestStatus failed', item.photoId, err)
              );
            }
            this.checkUploadComplete();
            break;
          case 'alreadyComplete':
            // Duplicate of an existing non-Uploading photo. Don't run the
            // processing subscription — there's nothing in flight for this
            // file in this session. Mark the row with a distinct status so
            // the user sees "skipped, already in album" instead of mistaking
            // it for a fresh upload.
            item.photoId = progress.photoId;
            item.uploadProgress = 100;
            item.processingProgress = 100;
            item.status = 'duplicate';
            this.duplicateCount++;
            // Surface the existing thumbnail since the photo really is there.
            item.thumbnailUrl = this.photoService.getThumbnailUrl(progress.photoId);
            item.completeTime = new Date();
            this.checkUploadComplete();
            // Auto-clear like the success path.
            setTimeout(() => this.removeCompletedItem(item), 5000);
            break;
          case 'error':
            console.log(`[PhotoUpload] Upload error for ${item.file.name}:`, progress.message);
            if (progress.photoId) item.photoId = progress.photoId;
            item.status = 'error';
            item.errorMessage = progress.message || 'Upload failed';
            this.errorCount++;
            this.checkUploadComplete();
            break;
        }
      },
      error: (error: any) => {
        // Defensive: PhotoService catches its own errors and emits an 'error'
        // phase, so this path is rare (transport-layer surprise). Treat it
        // the same as a phase: 'error' event.
        console.log(`[PhotoUpload] Upload error for ${item.file.name}:`, error);
        item.status = 'error';
        item.errorMessage = error?.error?.message || error?.message || 'Upload failed';
        this.errorCount++;
        this.checkUploadComplete();
      }
    });
  }

  /**
   * Wire this row to <c>PhotoProgressService</c> via an Angular <c>effect</c>.
   * The effect re-fires whenever the service's progress / completed / errors
   * signals change. The effect's lifetime is bound to the component via the
   * <c>Injector</c> we capture in ngOnInit; we additionally keep an explicit
   * <c>EffectRef</c> on the row so once the photo is fully complete (or has
   * been removed from the list) we can destroy the effect immediately and
   * stop reacting to unrelated photoId updates.
   *
   * This replaces the previous per-photo <c>interval(2000)</c> polling loop,
   * which at ~500 concurrent uploads exhausted the browser's HTTP connection
   * pool with <c>ERR_INSUFFICIENT_RESOURCES</c>.
   */
  private subscribeToProcessing(item: UploadFile) {
    if (!item.photoId) return;
    const photoId = item.photoId;

    item.effectRef = runInInjectionContext(this.injector, () =>
      effect(() => {
        const map = this.progressService.progress();
        const done = this.progressService.completed();
        const errs = this.progressService.errors();

        const perQuality = map.get(photoId);
        if (perQuality) {
          item.perQuality = { ...perQuality };
          // Aggregate percent for the legacy "processing" bar = average across
          // the five qualities. Bars not yet started count as 0.
          const total = QUALITIES.reduce((acc, q) => acc + (perQuality[q] ?? 0), 0);
          item.processingProgress = Math.round(total / QUALITIES.length);
        }

        if (errs.has(photoId) && item.status !== 'error') {
          item.status = 'error';
          item.errorMessage = errs.get(photoId);
          this.processingCount = Math.max(0, this.processingCount - 1);
          this.errorCount++;
          item.effectRef?.destroy();
          item.effectRef = undefined;
          this.checkUploadComplete();
          return;
        }

        if (done.has(photoId) && item.status !== 'complete') {
          item.status = 'complete';
          item.processingProgress = 100;
          item.thumbnailUrl = this.photoService.getThumbnailUrl(photoId);
          item.completeTime = new Date();
          this.processingCount = Math.max(0, this.processingCount - 1);
          this.successCount++;
          this.checkUploadComplete();

          // Tear down the effect — no more updates will come for this row.
          item.effectRef?.destroy();
          item.effectRef = undefined;

          // Auto-remove after 5s (visual fade-out handled by CSS .completed)
          setTimeout(() => this.removeCompletedItem(item), 5000);
        }
      })
    );
  }

  /**
   * Re-upload the same File from scratch. The previous photoId is abandoned
   * (server-side it will time out and be reaped by the orphaned-blob sweep
   * if no bytes ever landed). Simpler than wiring a dedicated /retry
   * endpoint, and the SAS-upload flow makes a fresh upload identical to the
   * first attempt.
   */
  retryUpload(item: UploadFile) {
    item.effectRef?.destroy();
    item.effectRef = undefined;
    item.status = 'pending';
    item.errorMessage = undefined;
    item.uploadProgress = 0;
    item.processingProgress = 0;
    item.perQuality = {};
    item.photoId = undefined;
    this.errorCount = Math.max(0, this.errorCount - 1);
    this.uploadCompleteFlag = false;
    this.isUploading = true;
    const index = this.uploadFiles.indexOf(item);
    this.uploadFile(item, index);
  }

  private removeCompletedItem(item: UploadFile) {
    const index = this.uploadFiles.indexOf(item);
    if (index > -1) {
      this.uploadFiles.splice(index, 1);
    }
  }

  private getProcessingStatusText(status: any): string {
    const completed = status.completedVersions || 0;
    const total = status.totalVersions || 4;
    return `${completed}/${total} versions`;
  }

  private checkUploadComplete() {
    const allDone = this.uploadFiles.every(
      f => f.status === 'complete' || f.status === 'error' || f.status === 'duplicate'
    );
    if (allDone && this.uploadFiles.length > 0) {
      this.isUploading = false;
      this.uploadCompleteFlag = true;
      this.uploadComplete.emit({
        successCount: this.successCount,
        processingCount: this.processingCount,
        errorCount: this.errorCount,
        duplicateCount: this.duplicateCount
      });
    }
  }
}
