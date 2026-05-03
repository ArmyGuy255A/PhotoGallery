import { Component, Input, Output, EventEmitter, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { PhotoService } from '../../services/photo.service';
import { interval, Subject, takeUntil, switchMap, filter, take } from 'rxjs';

interface UploadFile {
  file: File;
  uploadProgress: number;
  processingProgress: number;
  status: 'pending' | 'uploading' | 'complete' | 'error' | 'processing';
  errorMessage?: string;
  photoId?: string;
  processingStatus?: string;
  thumbnailUrl?: string;
  completeTime?: Date;
}

@Component({
  selector: 'app-photo-upload',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="upload-card">
      <div class="card-header">
        <strong>Upload Photos</strong>
      </div>
      <div class="card-body">
        <!-- Drag and drop zone -->
        <div
          class="upload-zone"
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
            (change)="onFileSelected($event)"
            style="display: none"
          />
          <button
            class="btn btn-primary"
            (click)="fileInput.click()"
            [disabled]="isUploading"
          >
            Choose Files
          </button>
        </div>

        <!-- Upload progress with thumbnails -->
        <div *ngIf="uploadFiles.length > 0" class="mt-4">
          <h6>Upload Progress</h6>
          <div *ngFor="let item of uploadFiles; let i = index" class="upload-item mb-3" [class.completed]="item.status === 'complete'">
            <div class="item-row d-flex align-items-center gap-3">
              <!-- Thumbnail -->
              <div class="thumbnail-container">
                <img 
                  *ngIf="item.thumbnailUrl && item.status === 'complete'" 
                  [src]="item.thumbnailUrl" 
                  alt="thumbnail"
                  class="thumbnail"
                  [title]="item.file.name"
                />
                <div *ngIf="!item.thumbnailUrl || item.status !== 'complete'" class="thumbnail-placeholder">
                  📷
                </div>
              </div>

              <!-- File info and progress -->
              <div class="flex-grow-1">
                <small class="d-block mb-1 filename">{{ item.file.name }}</small>
                
                <!-- Dual Progress Bar -->
                <div class="progress-container">
                  <!-- Background: Upload progress (blue) -->
                  <div class="progress progress-upload">
                    <div 
                      class="progress-bar progress-bar-upload" 
                      [style.width.%]="item.uploadProgress"
                      role="progressbar">
                    </div>
                  </div>
                  <!-- Overlay: Processing progress (green) -->
                  <div class="progress progress-processing">
                    <div 
                      class="progress-bar progress-bar-processing" 
                      [style.width.%]="item.processingProgress"
                      role="progressbar">
                    </div>
                  </div>
                </div>
                
                <!-- Status text -->
                <small class="status-text">
                  <span *ngIf="item.status === 'uploading'">📤 Uploading...</span>
                  <span *ngIf="item.status === 'processing'">🔄 Processing: {{ item.processingProgress }}%</span>
                  <span *ngIf="item.status === 'complete'">✅ Complete</span>
                  <span *ngIf="item.status === 'error'">❌ Error</span>
                </small>
                <small *ngIf="item.errorMessage" class="error-message">{{ item.errorMessage }}</small>
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
        <div *ngIf="uploadCompleteFlag" class="alert alert-info mt-4">
          <strong>Upload Summary:</strong>
          <p class="mb-0">
            {{ successCount }} photo(s) uploaded successfully,
            {{ processingCount }} processing,
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

    /* Progress bars */
    .progress-container {
      position: relative;
      margin: 6px 0;
    }

    .progress {
      height: 24px;
      background: #e9ecef;
      border-radius: 4px;
      overflow: hidden;
      position: absolute;
      width: 100%;
      top: 0;
    }

    .progress-upload {
      z-index: 1;
    }

    .progress-processing {
      z-index: 2;
    }

    .progress-bar {
      transition: width 0.3s ease;
      display: flex;
      align-items: center;
      justify-content: center;
      font-size: 11px;
      font-weight: 600;
      color: white;
      text-shadow: 0 1px 2px rgba(0, 0, 0, 0.2);
    }

    .progress-bar-upload {
      background: linear-gradient(90deg, #0d6efd, #0d6efd);
    }

    .progress-bar-processing {
      background: linear-gradient(90deg, rgba(40, 167, 69, 0.8), rgba(40, 167, 69, 0.8));
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
  `]
})
export class PhotoUploadComponent implements OnInit, OnDestroy {
  @Input() albumId: string = '';
  @Output() uploadComplete = new EventEmitter<any>();

  uploadFiles: UploadFile[] = [];
  isDraggingOver = false;
  isUploading = false;
  uploadCompleteFlag = false;
  successCount = 0;
  processingCount = 0;
  errorCount = 0;
  private destroy$ = new Subject<void>();
  private pollStop$ = new Subject<void>();

  constructor(private photoService: PhotoService) {}

  ngOnInit() {}

  ngOnDestroy() {
    this.destroy$.next();
    this.destroy$.complete();
    this.pollStop$.next();
    this.pollStop$.complete();
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

    // Upload files in parallel
    this.uploadFiles.forEach((item, index) => {
      this.uploadFile(item, index);
    });
  }

  private uploadFile(item: UploadFile, index: number) {
    item.status = 'uploading';
    console.log(`[PhotoUpload] Starting upload for file ${index}: ${item.file.name}, albumId: ${this.albumId}`);

    this.photoService.uploadPhoto(this.albumId, item.file).subscribe({
      next: (response: any) => {
        console.log(`[PhotoUpload] Upload success for ${item.file.name}:`, response);
        item.photoId = response.successfulUploads?.[0]?.photoId;
        item.uploadProgress = 100;
        item.status = 'processing';
        item.processingProgress = 0;
        this.processingCount++;
        
        // Start polling for processing status
        if (item.photoId) {
          this.startProcessingStatusPoll(item);
        }
        this.checkUploadComplete();
      },
      error: (error: any) => {
        console.log(`[PhotoUpload] Upload error for ${item.file.name}:`, error);
        item.status = 'error';
        item.errorMessage = error?.error?.message || error?.message || 'Upload failed';
        this.errorCount++;
        this.checkUploadComplete();
      }
    });
  }

  private startProcessingStatusPoll(item: UploadFile) {
    if (!item.photoId) return;

    interval(2000) // Poll every 2 seconds
      .pipe(
        takeUntil(this.destroy$),
        filter(() => item.status === 'processing'),
        switchMap(() => this.photoService.getPhotoProcessingStatus(item.photoId!))
      )
      .subscribe({
        next: (status: any) => {
          item.processingProgress = status.percentComplete || 0;
          item.processingStatus = this.getProcessingStatusText(status);
          
          if (status.percentComplete === 100 && status.hasThumbnail) {
            item.status = 'complete';
            item.thumbnailUrl = this.photoService.getThumbnailUrl(item.photoId!);
            item.completeTime = new Date();
            this.processingCount--;
            this.successCount++;
            console.log(`[PhotoUpload] Processing complete for ${item.file.name}`);
            this.checkUploadComplete();
            
            // Auto-remove after 5 seconds
            setTimeout(() => {
              this.removeCompletedItem(item);
            }, 5000);
          }
        },
        error: (error: any) => {
          console.log(`[PhotoUpload] Error polling status:`, error);
          // Don't fail on polling errors - just keep trying
        }
      });
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
      f => f.status === 'complete' || f.status === 'error'
    );
    if (allDone && this.uploadFiles.length > 0) {
      this.isUploading = false;
      this.uploadCompleteFlag = true;
      this.uploadComplete.emit({
        successCount: this.successCount,
        processingCount: this.processingCount,
        errorCount: this.errorCount
      });
    }
  }
}
