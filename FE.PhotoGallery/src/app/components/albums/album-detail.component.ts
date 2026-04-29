import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../environments/environment';

interface Photo {
  id: string;
  fileName: string;
  uploadDate: string;
  uploadedBy: string;
}

interface AccessCode {
  id: string;
  code: string;
  expirationDate: string | null;
  createdDate: string;
  createdBy: string;
  isExpired: boolean;
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
  imports: [CommonModule, RouterLink],
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

          <section class="photos-section">
            <div class="section-header">
              <h2>Photos ({{ photos.length }})</h2>
              <button routerLink="/albums/{{ album.id }}/upload" class="action-btn">+ Upload Photos</button>
            </div>

            <div class="photos-grid" *ngIf="photos.length > 0">
              <div *ngFor="let photo of photos" class="photo-card">
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
              <p>No photos yet. Upload some photos to get started.</p>
              <button routerLink="/albums/{{ album.id }}/upload" class="action-btn">Upload Photos</button>
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
                    <th>Code</th>
                    <th>Expiration Date</th>
                    <th>Created</th>
                    <th>Status</th>
                    <th>Action</th>
                  </tr>
                </thead>
                <tbody>
                  <tr *ngFor="let code of accessCodes" [class.expired]="code.isExpired">
                    <td class="code">{{ code.code }}</td>
                    <td>{{ (code.expirationDate | date: 'short') || 'Never' }}</td>
                    <td>{{ (code.createdDate | date: 'short') }}</td>
                    <td>
                      <span class="badge" [class.active]="!code.isExpired" [class.expired]="code.isExpired">
                        {{ code.isExpired ? 'Expired' : 'Active' }}
                      </span>
                    </td>
                    <td>
                      <button class="delete-btn" (click)="deleteAccessCode(code.id)">Delete</button>
                    </td>
                  </tr>
                </tbody>
              </table>
            </div>

            <div class="empty-message" *ngIf="accessCodes.length === 0">
              <p>No access codes yet. Generate one to share this album with others.</p>
              <button (click)="createAccessCode()" class="action-btn">Generate Code</button>
            </div>
          </section>
        </div>

        <div class="error" *ngIf="errorMessage">
          <p>{{ errorMessage }}</p>
        </div>
      </main>
    </div>
  `,
  styles: [`
    .album-detail-container {
      min-height: 100vh;
      background: #f5f7fa;
    }

    .detail-header {
      background: white;
      padding: 24px;
      border-bottom: 1px solid #e0e6ed;
    }

    .back-btn {
      padding: 8px 16px;
      background: #ecf0f1;
      border: none;
      border-radius: 4px;
      cursor: pointer;
      margin-bottom: 12px;
      font-size: 14px;
      transition: background 0.3s;
    }

    .back-btn:hover {
      background: #d5dbdb;
    }

    .detail-header h1 {
      margin: 0;
      font-size: 28px;
      color: #333;
    }

    .detail-content {
      padding: 32px;
      max-width: 1200px;
      margin: 0 auto;
    }

    .loading {
      padding: 24px;
      text-align: center;
      color: #999;
    }

    .album-info {
      background: white;
      padding: 24px;
      border-radius: 8px;
      box-shadow: 0 2px 8px rgba(0, 0, 0, 0.1);
      margin-bottom: 32px;
    }

    .description {
      font-size: 16px;
      color: #666;
      margin: 0 0 12px 0;
      line-height: 1.6;
    }

    .meta {
      font-size: 13px;
      color: #999;
      margin: 0;
    }

    .section-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      margin-bottom: 24px;
    }

    .section-header h2 {
      margin: 0;
      font-size: 20px;
      color: #333;
    }

    .action-btn {
      padding: 10px 20px;
      background: #27ae60;
      color: white;
      border: none;
      border-radius: 4px;
      cursor: pointer;
      font-size: 14px;
      transition: background 0.3s;
    }

    .action-btn:hover {
      background: #229954;
    }

    .photos-section {
      margin-bottom: 40px;
    }

    .photos-grid {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(150px, 1fr));
      gap: 16px;
    }

    .photo-card {
      background: white;
      border-radius: 8px;
      padding: 16px;
      box-shadow: 0 2px 8px rgba(0, 0, 0, 0.1);
      text-align: center;
    }

    .photo-placeholder {
      width: 100px;
      height: 100px;
      background: #ecf0f1;
      border-radius: 8px;
      display: flex;
      align-items: center;
      justify-content: center;
      margin: 0 auto 12px;
    }

    .photo-placeholder svg {
      width: 50px;
      height: 50px;
      fill: #bdc3c7;
    }

    .photo-card h3 {
      margin: 0 0 8px 0;
      font-size: 14px;
      color: #333;
      word-break: break-word;
    }

    .photo-meta {
      margin: 0;
      font-size: 12px;
      color: #999;
    }

    .empty-message {
      background: white;
      padding: 32px;
      border-radius: 8px;
      text-align: center;
      box-shadow: 0 2px 8px rgba(0, 0, 0, 0.1);
    }

    .empty-message p {
      color: #999;
      margin: 0 0 16px 0;
    }

    .access-codes-section {
      margin-bottom: 40px;
    }

    .codes-table {
      background: white;
      border-radius: 8px;
      overflow: hidden;
      box-shadow: 0 2px 8px rgba(0, 0, 0, 0.1);
    }

    table {
      width: 100%;
      border-collapse: collapse;
    }

    thead {
      background: #f9f9f9;
      border-bottom: 1px solid #e0e6ed;
    }

    th {
      padding: 12px 16px;
      text-align: left;
      font-weight: 600;
      color: #666;
      font-size: 13px;
    }

    td {
      padding: 12px 16px;
      border-bottom: 1px solid #e0e6ed;
      font-size: 14px;
    }

    tr:last-child td {
      border-bottom: none;
    }

    td.code {
      font-family: monospace;
      font-weight: 600;
      color: #333;
    }

    .badge {
      display: inline-block;
      padding: 4px 12px;
      border-radius: 12px;
      font-size: 12px;
      font-weight: 600;
    }

    .badge.active {
      background: #d5f4e6;
      color: #186a3b;
    }

    .badge.expired {
      background: #fadbd8;
      color: #922b21;
    }

    .delete-btn {
      padding: 6px 12px;
      background: #e74c3c;
      color: white;
      border: none;
      border-radius: 4px;
      cursor: pointer;
      font-size: 12px;
      transition: background 0.3s;
    }

    .delete-btn:hover {
      background: #c0392b;
    }

    .error {
      padding: 16px;
      background: #fadbd8;
      border-left: 4px solid #e74c3c;
      color: #922b21;
      border-radius: 4px;
    }
  `]
})
export class AlbumDetailComponent implements OnInit {
  album: Album | null = null;
  photos: Photo[] = [];
  accessCodes: AccessCode[] = [];
  isLoading = true;
  errorMessage = '';
  albumId: string = '';

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private http: HttpClient
  ) {}

  ngOnInit(): void {
    this.route.params.subscribe(params => {
      this.albumId = params['id'];
      if (this.albumId) {
        this.loadAlbumDetails();
      }
    });
  }

  loadAlbumDetails(): void {
    this.isLoading = true;
    const apiUrl = environment.apiUrl || '';

    this.http.get<Album>(`${apiUrl}/api/albums/${this.albumId}`).subscribe({
      next: (album) => {
        this.album = album;
        this.loadPhotos();
        this.loadAccessCodes();
      },
      error: (error) => {
        console.error('Error loading album:', error);
        this.errorMessage = 'Failed to load album. Please try again.';
        this.isLoading = false;
      }
    });
  }

  loadPhotos(): void {
    const apiUrl = environment.apiUrl || '';
    this.http.get<Photo[]>(`${apiUrl}/api/albums/${this.albumId}/photos`).subscribe({
      next: (photos) => {
        this.photos = photos || [];
      },
      error: (error) => {
        console.error('Error loading photos:', error);
        this.photos = [];
      }
    });
  }

  loadAccessCodes(): void {
    const apiUrl = environment.apiUrl || '';
    this.http.get<AccessCode[]>(`${apiUrl}/api/albums/${this.albumId}/access-codes`).subscribe({
      next: (codes) => {
        this.accessCodes = codes || [];
        this.isLoading = false;
      },
      error: (error) => {
        console.error('Error loading access codes:', error);
        this.accessCodes = [];
        this.isLoading = false;
      }
    });
  }

  createAccessCode(): void {
    const apiUrl = environment.apiUrl || '';
    const request = {
      expiresForever: false,
      expirationDays: 30
    };

    this.http.post<any>(`${apiUrl}/api/albums/${this.albumId}/access-codes`, request).subscribe({
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
    this.http.delete(`${apiUrl}/api/albums/${this.albumId}/access-codes/${codeId}`).subscribe({
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
}
