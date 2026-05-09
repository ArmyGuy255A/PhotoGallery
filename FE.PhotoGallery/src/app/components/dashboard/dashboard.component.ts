import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink, Router, ActivationEnd } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { AuthService, User } from '../../services/auth.service';
import { UserDropdownComponent } from '../user-dropdown/user-dropdown.component';
import { environment } from '../../../environments/environment';
import { Subject } from 'rxjs';
import { takeUntil, filter } from 'rxjs/operators';

interface Album {
  id: string;
  title: string;
  description: string;
  createdDate: string;
  ownerId: string;
  canManage: boolean;
}

/**
 * Admin dashboard component showing albums and management options
 */
@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CommonModule, RouterLink, UserDropdownComponent],
  template: `
    <div class="dashboard-container">
      <header class="dashboard-header">
        <h1>Dashboard</h1>
        <app-user-dropdown></app-user-dropdown>
      </header>

      <main class="dashboard-content">
        <section class="albums-section">
          <div class="section-header">
            <h2>My Albums</h2>
            <button routerLink="/albums/create" class="create-btn">+ New Album</button>
          </div>

          <div class="loading" *ngIf="isLoading">
            <p>Loading albums...</p>
          </div>

          <div class="albums-grid" *ngIf="!isLoading">
            <div *ngIf="albums.length === 0" class="album-card">
              <div class="album-placeholder">
                <svg viewBox="0 0 24 24">
                  <path d="M19 13h-6v6h-2v-6H5v-2h6V5h2v6h6v2z"/>
                </svg>
              </div>
              <h3>Create Your First Album</h3>
              <p>Get started by creating a new album to organize and share your photos</p>
              <button routerLink="/albums/create" class="action-btn">Create Album</button>
            </div>

            <div *ngFor="let album of albums" class="album-card">
              <div class="album-header">
                <h3>{{ album.title }}</h3>
                <div class="album-actions" *ngIf="album.canManage">
                  <button class="action-icon" title="Edit" (click)="editAlbum(album.id)">✎</button>
                  <button class="action-icon delete" title="Delete" (click)="deleteAlbum(album.id)">✕</button>
                </div>
              </div>
              <p class="album-description">{{ album.description || 'No description' }}</p>
              <p class="album-date">{{ (album.createdDate | date: 'short') }}</p>
              <button class="action-btn" (click)="viewAlbum(album.id)">View Album</button>
            </div>
          </div>

          <div class="error" *ngIf="errorMessage">
            <p>{{ errorMessage }}</p>
          </div>
        </section>

        <section class="stats-section" *ngIf="currentUser?.roles?.includes('Admin')">
          <h2>Admin Stats</h2>
          <div class="stats-grid">
            <div class="stat-card">
              <div class="stat-value">{{ albums.length }}</div>
              <div class="stat-label">Total Albums</div>
            </div>
            <div class="stat-card">
              <div class="stat-value">{{ totalPhotos }}</div>
              <div class="stat-label">Total Photos</div>
            </div>
            <div class="stat-card">
              <div class="stat-value">{{ activeCodes }}</div>
              <div class="stat-label">Active Codes</div>
            </div>
          </div>
        </section>
      </main>
    </div>
  `,
  styles: [`
    .dashboard-container {
      min-height: 100vh;
      background: #f5f7fa;
    }

    .dashboard-header {
      background: white;
      padding: 24px;
      border-bottom: 1px solid #e0e6ed;
      display: flex;
      justify-content: space-between;
      align-items: center;
    }

    .dashboard-header h1 {
      margin: 0;
      font-size: 28px;
      color: #333;
    }

    .user-info {
      display: flex;
      align-items: center;
      gap: 16px;
      color: #666;
    }

    .logout-btn {
      padding: 8px 16px;
      background: #e74c3c;
      color: white;
      border: none;
      border-radius: 4px;
      cursor: pointer;
      font-size: 14px;
      transition: background 0.3s;
    }

    .logout-btn:hover {
      background: #c0392b;
    }

    .dashboard-content {
      padding: 32px;
      max-width: 1200px;
      margin: 0 auto;
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

    .create-btn {
      padding: 10px 20px;
      background: #3498db;
      color: white;
      border: none;
      border-radius: 4px;
      cursor: pointer;
      font-weight: 500;
      transition: background 0.3s;
    }

    .create-btn:hover {
      background: #2980b9;
    }

    .loading {
      padding: 24px;
      text-align: center;
      color: #999;
    }

    .albums-grid {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(280px, 1fr));
      gap: 20px;
      margin-bottom: 40px;
    }

    .album-card {
      background: white;
      border-radius: 8px;
      padding: 20px;
      box-shadow: 0 2px 8px rgba(0, 0, 0, 0.1);
      transition: transform 0.3s, box-shadow 0.3s;
      display: flex;
      flex-direction: column;
    }

    .album-card:hover {
      transform: translateY(-4px);
      box-shadow: 0 4px 16px rgba(0, 0, 0, 0.15);
    }

    .album-header {
      display: flex;
      justify-content: space-between;
      align-items: start;
      margin-bottom: 12px;
    }

    .album-card h3 {
      margin: 0;
      font-size: 16px;
      color: #333;
      flex: 1;
    }

    .album-actions {
      display: flex;
      gap: 8px;
    }

    .action-icon {
      width: 28px;
      height: 28px;
      padding: 0;
      background: #ecf0f1;
      border: none;
      border-radius: 4px;
      cursor: pointer;
      font-size: 14px;
      color: #666;
      transition: background 0.2s;
    }

    .action-icon:hover {
      background: #d5dbdb;
    }

    .action-icon.delete:hover {
      background: #fadbd8;
      color: #e74c3c;
    }

    .album-placeholder {
      width: 80px;
      height: 80px;
      background: #ecf0f1;
      border-radius: 8px;
      display: flex;
      align-items: center;
      justify-content: center;
      margin: 0 auto 16px;
    }

    .album-placeholder svg {
      width: 40px;
      height: 40px;
      fill: #bdc3c7;
    }

    .album-description {
      margin: 0 0 8px 0;
      color: #666;
      font-size: 13px;
      line-height: 1.4;
      flex: 1;
    }

    .album-date {
      margin: 0 0 12px 0;
      color: #999;
      font-size: 12px;
    }

    .action-btn {
      padding: 8px 16px;
      background: #27ae60;
      color: white;
      border: none;
      border-radius: 4px;
      cursor: pointer;
      font-size: 14px;
      transition: background 0.3s;
      align-self: flex-start;
    }

    .action-btn:hover {
      background: #229954;
    }

    .error {
      padding: 16px;
      background: #fadbd8;
      border-left: 4px solid #e74c3c;
      color: #922b21;
      border-radius: 4px;
    }

    .stats-section {
      margin-top: 40px;
    }

    .stats-section h2 {
      margin: 0 0 20px 0;
      font-size: 20px;
      color: #333;
    }

    .stats-grid {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
      gap: 20px;
    }

    .stat-card {
      background: white;
      padding: 24px;
      border-radius: 8px;
      text-align: center;
      box-shadow: 0 2px 8px rgba(0, 0, 0, 0.1);
    }

    .stat-value {
      font-size: 32px;
      font-weight: 600;
      color: #3498db;
      margin-bottom: 8px;
    }

    .stat-label {
      font-size: 14px;
      color: #999;
    }
  `]
})
export class DashboardComponent implements OnInit, OnDestroy {
  currentUser: User | null = null;
  albums: Album[] = [];
  isLoading = true;
  errorMessage = '';
  totalPhotos = 0;
  activeCodes = 0;
  private destroy$ = new Subject<void>();

  constructor(
    private authService: AuthService,
    private http: HttpClient,
    private router: Router
  ) {}

  ngOnInit(): void {
    this.authService.currentUser$.subscribe(user => {
      this.currentUser = user;
    });

    this.loadAlbums();
    this.loadStats();

    // Reload albums when returning to dashboard
    this.router.events
      .pipe(
        filter(event => event instanceof ActivationEnd && event.snapshot.component === DashboardComponent),
        takeUntil(this.destroy$)
      )
      .subscribe(() => {
        this.loadAlbums();
        this.loadStats();
      });
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  loadAlbums(): void {
    this.isLoading = true;
    this.errorMessage = '';

    const apiUrl = environment.apiUrl || '';
    const endpoint = `${apiUrl}/api/albums`;

    this.http.get<Album[]>(endpoint).subscribe({
      next: (data) => {
        this.albums = data || [];
        this.isLoading = false;
        console.log(`Loaded ${this.albums.length} albums`);
      },
      error: (error) => {
        console.error('Error loading albums:', error);
        this.errorMessage = 'Failed to load albums. Please try again.';
        this.isLoading = false;
        this.albums = [];
      }
    });
  }

  loadStats(): void {
    const apiUrl = environment.apiUrl || '';

    // Load total photos
    this.http.get<any>(`${apiUrl}/api/stats/photos`).subscribe({
      next: (data) => {
        this.totalPhotos = data?.count || 0;
      },
      error: (error) => {
        console.error('Error loading photo stats:', error);
        this.totalPhotos = 0;
      }
    });

    // Load active access codes
    this.http.get<any>(`${apiUrl}/api/stats/access-codes`).subscribe({
      next: (data) => {
        this.activeCodes = data?.count || 0;
      },
      error: (error) => {
        console.error('Error loading access code stats:', error);
        this.activeCodes = 0;
      }
    });
  }

  viewAlbum(albumId: string): void {
    console.log('View album:', albumId);
    this.router.navigate(['/albums', albumId]);
  }

  editAlbum(albumId: string): void {
    console.log('Edit album:', albumId);
    this.router.navigate(['/albums', albumId, 'edit']);
  }

  deleteAlbum(albumId: string): void {
    if (!confirm('Are you sure you want to delete this album? This action cannot be undone.')) {
      return;
    }

    const apiUrl = environment.apiUrl || '';
    const endpoint = `${apiUrl}/api/albums/${albumId}`;

    this.http.delete(endpoint).subscribe({
      next: () => {
        console.log('Album deleted successfully');
        this.loadAlbums();
      },
      error: (error) => {
        console.error('Error deleting album:', error);
        this.errorMessage = 'Failed to delete album. Please try again.';
      }
    });
  }

  logout(): void {
    this.authService.logout().subscribe(() => {
      window.location.href = '/login';
    });
  }
}
