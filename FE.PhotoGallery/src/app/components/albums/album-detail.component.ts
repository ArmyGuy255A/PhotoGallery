import { Component, OnInit, OnDestroy } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../environments/environment';
import { PhotoUploadComponent } from './photo-upload.component';
import { Subject } from 'rxjs';
import { takeUntil } from 'rxjs/operators';

interface Photo {
  id: string;
  fileName: string;
  uploadDate: string;
  processingStatus?: string;
}

interface AccessCode {
  id: string;
  code: string;
  expirationDate: string | null;
  createdDate: string;
}

interface Album {
  id: string;
  title: string;
  description: string;
  createdDate: string;
  createdBy: string;
  ownerId: string;
}

@Component({
  selector: 'app-album-detail',
  standalone: true,
  imports: [CommonModule, RouterLink, PhotoUploadComponent],
  template: `
    <div class="album-detail-container">
      <header class="detail-header">
        <button class="back-btn" routerLink="/dashboard">← Back to Dashboard</button>
        <h1>{{ album?.title }}</h1>
      </header>

      <main class="detail-content">
        <div class="loading" *ngIf="isLoading">
          <p>Loading album details...</p>
        </div>

        <div *ngIf="!isLoading && album">
          <section class="album-info">
            <p class="description">{{ album.description || 'No description' }}</p>
            <p class="meta">Created on {{ (album.createdDate | date: 'short') }} by {{ album.createdBy }}</p>
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
              <h2>Photos ({{ photos.length }})</h2>
            </div>

            <div class="photos-grid" *ngIf="photos.length > 0">
              <div *ngFor="let photo of photos" class="photo-card">
                <div class="photo-status-badge" [ngClass]="getStatusClass(photo)">
                  <span *ngIf="photo.processingStatus === 'Complete'">✓</span>
                  <span *ngIf="photo.processingStatus === 'Processing'">⟳</span>
                  <span *ngIf="photo.processingStatus === 'Failed'">✗</span>
                </div>
                <div class="photo-placeholder">
                  <svg viewBox="0 0 24 24">
                    <path d="M21 19V5c0-1.1-.9-2-2-2H5c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h14c1.1 0 2-.9 2-2zM8.5 13.5l2.5 3.01L14.5 12l4.5 6H5l3.5-4.5z"/>
                  </svg>
                </div>
                <h3>{{ photo.fileName }}</h3>
                <p class="photo-meta">Uploaded {{ (photo.uploadDate | date: 'short') }}</p>
              </div>
            </div>

            <div class="empty-message" *ngIf="photos.length === 0">
              <p>No photos yet. Upload some photos to get started above.</p>
            </div>
          </section>

          <section class="access-codes-section">
            <div class="section-header">
              <h2>Access Codes ({{ accessCodes.length }})</h2>
              <button (click)="createAccessCode()" class="action-btn">+ Generate Code</button>
            </div>

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
                      <button class="copy-btn" (click)="copyToClipboard(code.code)" title="Copy code">📋</button>
                    </td>
                    <td>
                      <span class="status-badge" [ngClass]="getCodeStatus(code)">
                        {{ getCodeStatus(code) === 'active' ? 'Active' : 'Expired' }}
                      </span>
                    </td>
                    <td>{{ code.expirationDate ? (code.expirationDate | date: 'short') : 'Never' }}</td>
                    <td>{{ code.createdDate | date: 'short' }}</td>
                    <td>
                      <button class="delete-btn" (click)="deleteAccessCode(code.id)">Delete</button>
                    </td>
                  </tr>
                </tbody>
              </table>
            </div>

            <div class="empty-message" *ngIf="accessCodes.length === 0">
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
      grid-template-columns: repeat(auto-fill, minmax(150px, 1fr));
      gap: 15px;
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
      top: 5px;
      right: 5px;
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
      border: none;
      cursor: pointer;
      font-size: 16px;
      padding: 2px 5px;
    }

    .copy-btn:hover {
      transform: scale(1.2);
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
  `]
})
export class AlbumDetailComponent implements OnInit, OnDestroy {
  albumId: string = '';
  album: Album | null = null;
  photos: Photo[] = [];
  accessCodes: AccessCode[] = [];
  isLoading = true;
  errorMessage = '';
  isAdmin = false;

  private destroy$ = new Subject<void>();

  constructor(
    private route: ActivatedRoute,
    private http: HttpClient
  ) {}

  ngOnInit(): void {
    this.route.params.pipe(takeUntil(this.destroy$)).subscribe(params => {
      this.albumId = params['id'];
      this.loadAlbum();
      this.loadPhotos();
      this.loadAccessCodes();
    });
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
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

  loadPhotos(): void {
    const apiUrl = environment.apiUrl || '';
    this.http.get<Photo[]>(`${apiUrl}/api/albums/${this.albumId}/photos`).pipe(takeUntil(this.destroy$)).subscribe({
      next: (data) => {
        this.photos = data || [];
      },
      error: (error) => {
        console.error('Error loading photos:', error);
      }
    });
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

  createAccessCode(): void {
    const apiUrl = environment.apiUrl || '';
    const defaultExpiration = new Date();
    defaultExpiration.setDate(defaultExpiration.getDate() + 30);

    this.http.post(`${apiUrl}/api/albums/${this.albumId}/access-codes`, {
      expirationDate: defaultExpiration
    }).pipe(takeUntil(this.destroy$)).subscribe({
      next: () => {
        console.log('Access code created successfully');
        this.loadAccessCodes();
      },
      error: (error) => {
        console.error('Error creating access code:', error);
        this.errorMessage = 'Failed to create access code. Please try again.';
      }
    });
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

  getStatusClass(photo: Photo): string {
    if (!photo.processingStatus) return 'pending';
    
    const status = photo.processingStatus.toLowerCase();
    if (status === 'complete') return 'complete';
    if (status === 'processing') return 'processing';
    if (status === 'failed') return 'failed';
    return 'pending';
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
      console.log('Copied to clipboard:', text);
      alert('Access code copied to clipboard!');
    });
  }
}
