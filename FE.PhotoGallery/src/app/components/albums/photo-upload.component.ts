import { Component, Input, Output, EventEmitter, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { PhotoService } from '../../services/photo.service';

interface UploadFile {
  file: File;
  progress: number;
  status: 'pending' | 'uploading' | 'complete' | 'error';
  errorMessage?: string;
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

        <!-- Upload progress -->
        <div *ngIf="uploadFiles.length > 0" class="mt-4">
          <h6>Upload Progress</h6>
          <div *ngFor="let item of uploadFiles" class="mb-3">
            <small class="d-block mb-1">{{ item.file.name }}</small>
            <div class="d-flex align-items-center gap-2">
              <div class="progress flex-grow-1">
                <div 
                  class="progress-bar" 
                  [style.width.%]="item.progress"
                  role="progressbar">
                </div>
              </div>
              <span class="text-sm">{{ item.progress }}%</span>
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
            </div>
            <small *ngIf="item.errorMessage" class="text-danger d-block mt-1">
              {{ item.errorMessage }}
            </small>
          </div>
        </div>

        <!-- Summary -->
        <div *ngIf="uploadCompleteFlag" class="alert alert-info mt-4">
          <strong>Upload Summary:</strong>
          <p class="mb-0">
            {{ successCount }} photo(s) uploaded successfully,
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

    .text-sm {
      font-size: 0.875rem;
    }

    .gap-2 {
      gap: 0.5rem;
    }

    .progress {
      height: 20px;
      background: #e9ecef;
      border-radius: 4px;
      overflow: hidden;
    }

    .progress-bar {
      background: #27ae60;
      transition: width 0.3s;
    }

    .badge {
      padding: 4px 8px;
      border-radius: 4px;
      font-size: 12px;
      font-weight: 600;
      color: white;
    }

    .bg-success {
      background: #27ae60;
    }

    .bg-danger {
      background: #e74c3c;
    }

    .bg-info {
      background: #3498db;
    }

    .spinner-border {
      display: inline-block;
      width: 1rem;
      height: 1rem;
      vertical-align: -0.125em;
      border: 0.25em solid currentColor;
      border-right-color: transparent;
      border-radius: 50%;
      animation: spinner-border 0.75s linear infinite;
    }

    .spinner-border-sm {
      width: 0.875rem;
      height: 0.875rem;
      border-width: 0.2em;
    }

    .text-primary {
      color: #0d6efd;
    }

    @keyframes spinner-border {
      to {
        transform: rotate(360deg);
      }
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
    }

    .text-muted {
      color: #6c757d;
    }

    .text-danger {
      color: #e74c3c;
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
export class PhotoUploadComponent implements OnInit {
  @Input() albumId: string = '';
  @Output() uploadComplete = new EventEmitter<any>();

  uploadFiles: UploadFile[] = [];
  isDraggingOver = false;
  isUploading = false;
  uploadCompleteFlag = false;
  successCount = 0;
  errorCount = 0;

  constructor(private photoService: PhotoService) {}

  ngOnInit() {}

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
        progress: 0,
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
        item.status = 'complete';
        item.progress = 100;
        this.successCount++;
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

  private checkUploadComplete() {
    const allDone = this.uploadFiles.every(
      f => f.status === 'complete' || f.status === 'error'
    );
    if (allDone) {
      this.isUploading = false;
      this.uploadCompleteFlag = true;
      this.uploadComplete.emit({
        successCount: this.successCount,
        errorCount: this.errorCount
      });
    }
  }
}
