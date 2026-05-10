import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
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

interface SavedAccessCode {
  id: string;
  code: string;
  albumId: string;
  albumTitle: string;
  savedAt: string;
  expirationDate?: string | null;
}

/**
 * Dashboard component. Renders different sections based on the user's role:
 *
 *   - Admins see "My Albums" (the photographer's library) + admin stats.
 *   - Regular users see "Shared Albums" (albums they've saved via access code)
 *     plus an inline "Enter Access Code" form for adding a new shared album
 *     without leaving the dashboard.
 *
 * Both views are read-only for code-driven sharing — actual album creation
 * remains admin-only at the server (POST /api/albums requires Admin role).
 */
@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, UserDropdownComponent],
  template: `
    <div class="dashboard-container">
      <header class="dashboard-header">
        <h1>Dashboard</h1>
        <app-user-dropdown></app-user-dropdown>
      </header>

      <main class="dashboard-content">
        <!-- Admin-only: My Albums (the photographer's owned library) -->
        <section class="albums-section" *ngIf="isAdmin">
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

        <!-- Visible to everyone: Shared Albums (access-code-saved albums) -->
        <section class="albums-section">
          <div class="section-header">
            <h2>Shared Albums</h2>
            <button class="create-btn" (click)="toggleAddCodeForm()" data-testid="add-shared-album-btn">
              {{ showAddCodeForm ? 'Cancel' : '+ Add by Code' }}
            </button>
          </div>

          <div class="add-code-form" *ngIf="showAddCodeForm">
            <label for="newAccessCode" class="form-label">Access Code</label>
            <div class="form-row">
              <input
                id="newAccessCode"
                #codeInput
                type="text"
                class="code-input"
                [(ngModel)]="newCodeInput"
                (keydown.enter)="submitNewCode()"
                placeholder="e.g. 3M50YD237995"
                autocomplete="off"
                spellcheck="false"
                [disabled]="addingCode"
                data-testid="add-shared-album-input">
              <button
                class="action-btn"
                (click)="submitNewCode()"
                [disabled]="!newCodeInput.trim() || addingCode"
                data-testid="add-shared-album-submit">
                {{ addingCode ? 'Adding...' : 'Add Album' }}
              </button>
            </div>
            <div *ngIf="addCodeError" class="error-inline" data-testid="add-shared-album-error">
              {{ addCodeError }}
            </div>
          </div>

          <div class="loading" *ngIf="isLoadingShared">
            <p>Loading shared albums...</p>
          </div>

          <div class="albums-grid" *ngIf="!isLoadingShared">
            <div *ngIf="sharedAlbums.length === 0" class="album-card empty-state">
              <div class="album-placeholder">
                <svg viewBox="0 0 24 24">
                  <path d="M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm-1 17.93c-3.94-.49-7-3.85-7-7.93 0-.62.08-1.21.21-1.79L9 15v1c0 1.1.9 2 2 2v1.93zm6.9-2.54c-.26-.81-1-1.39-1.9-1.39h-1v-3c0-.55-.45-1-1-1H8v-2h2c.55 0 1-.45 1-1V7h2c1.1 0 2-.9 2-2v-.41c2.93 1.19 5 4.06 5 7.41 0 2.08-.8 3.97-2.1 5.39z"/>
                </svg>
              </div>
              <h3>No Shared Albums Yet</h3>
              <p>Got a code from a photographer? Click "+ Add by Code" above to view their album.</p>
            </div>

            <article *ngFor="let saved of sharedAlbums" class="album-card">
              <div class="album-header">
                <h3>{{ saved.albumTitle || '(Untitled album)' }}</h3>
                <div class="album-actions">
                  <button class="action-icon delete" title="Remove" (click)="removeSavedCode(saved)" [disabled]="removingId === saved.id">✕</button>
                </div>
              </div>
              <p class="album-description">Code: {{ saved.code }}</p>
              <p class="album-date">
                Saved {{ saved.savedAt | date: 'short' }}
                <span *ngIf="saved.expirationDate"> · Expires {{ saved.expirationDate | date: 'mediumDate' }}</span>
                <span *ngIf="!saved.expirationDate"> · No expiration</span>
              </p>
              <button class="action-btn" (click)="viewSharedAlbum(saved)">View Album</button>
            </article>
          </div>

          <div class="error" *ngIf="sharedErrorMessage">
            <p>{{ sharedErrorMessage }}</p>
          </div>
        </section>

        <section class="stats-section" *ngIf="isAdmin">
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

    /* Add-by-code form (regular users) */
    .add-code-form {
      background: white;
      padding: 16px 20px;
      border-radius: 8px;
      box-shadow: 0 2px 8px rgba(0, 0, 0, 0.06);
      margin-bottom: 20px;
    }
    .form-label {
      display: block;
      font-size: 13px;
      color: #555;
      margin-bottom: 8px;
      font-weight: 500;
    }
    .form-row {
      display: flex;
      gap: 12px;
      align-items: stretch;
    }
    .code-input {
      flex: 1;
      padding: 10px 14px;
      font-size: 16px;
      border: 1px solid #d0d6df;
      border-radius: 4px;
      letter-spacing: 1px;
      text-transform: uppercase;
      font-family: 'Courier New', monospace;
    }
    .code-input:focus {
      outline: none;
      border-color: #3498db;
      box-shadow: 0 0 0 3px rgba(52, 152, 219, 0.15);
    }
    .code-input:disabled { opacity: 0.6; }
    .error-inline {
      margin-top: 10px;
      color: #c62828;
      font-size: 13px;
    }
    .empty-state {
      grid-column: 1 / -1;
      text-align: center;
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

  // Shared albums (regular users + admins both see this)
  sharedAlbums: SavedAccessCode[] = [];
  isLoadingShared = true;
  sharedErrorMessage = '';
  showAddCodeForm = false;
  newCodeInput = '';
  addingCode = false;
  addCodeError = '';
  removingId: string | null = null;

  private destroy$ = new Subject<void>();

  get isAdmin(): boolean {
    return this.authService.isAdmin();
  }

  constructor(
    private authService: AuthService,
    private http: HttpClient,
    private router: Router
  ) {}

  ngOnInit(): void {
    this.authService.currentUser$.subscribe(user => {
      this.currentUser = user;
    });

    // Admin-only data; skip the request entirely for non-admins so they don't
    // see a 403 in the browser console for an endpoint they shouldn't be
    // hitting in the first place.
    if (this.isAdmin) {
      this.loadAlbums();
      this.loadStats();
    } else {
      this.isLoading = false;
    }

    this.loadSharedAlbums();

    // Reload data when returning to dashboard
    this.router.events
      .pipe(
        filter(event => event instanceof ActivationEnd && event.snapshot.component === DashboardComponent),
        takeUntil(this.destroy$)
      )
      .subscribe(() => {
        if (this.isAdmin) {
          this.loadAlbums();
          this.loadStats();
        }
        this.loadSharedAlbums();
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

  // -------- Shared albums (any authenticated user) --------

  loadSharedAlbums(): void {
    this.isLoadingShared = true;
    this.sharedErrorMessage = '';
    const apiUrl = environment.apiUrl || '';
    this.http.get<SavedAccessCode[]>(`${apiUrl}/api/account/access-codes`).subscribe({
      next: (data) => {
        this.sharedAlbums = data ?? [];
        this.isLoadingShared = false;
      },
      error: (error) => {
        console.error('Error loading shared albums:', error);
        this.sharedErrorMessage = 'Failed to load shared albums. Please try again.';
        this.isLoadingShared = false;
      }
    });
  }

  toggleAddCodeForm(): void {
    this.showAddCodeForm = !this.showAddCodeForm;
    this.newCodeInput = '';
    this.addCodeError = '';
  }

  submitNewCode(): void {
    const code = (this.newCodeInput || '').trim();
    if (!code || this.addingCode) return;

    this.addingCode = true;
    this.addCodeError = '';
    const apiUrl = environment.apiUrl || '';
    this.http.post(`${apiUrl}/api/account/access-codes`, { code }).subscribe({
      next: () => {
        this.addingCode = false;
        this.showAddCodeForm = false;
        this.newCodeInput = '';
        this.loadSharedAlbums();
      },
      error: (err) => {
        console.error('Failed to add access code:', err);
        this.addingCode = false;
        if (err?.status === 404) {
          this.addCodeError = 'Access code not found. Double-check the code and try again.';
        } else if (err?.status === 400) {
          this.addCodeError = err?.error?.error ?? 'This code is invalid or expired.';
        } else if (err?.status === 401) {
          this.addCodeError = 'You need to sign in again before adding a code.';
        } else {
          this.addCodeError = 'Failed to add the album. Please try again.';
        }
      }
    });
  }

  viewSharedAlbum(saved: SavedAccessCode): void {
    this.router.navigate(['/code', saved.code]);
  }

  removeSavedCode(saved: SavedAccessCode): void {
    if (this.removingId) return;
    if (!confirm(`Remove "${saved.albumTitle || 'this album'}" from your shared albums?`)) return;

    this.removingId = saved.id;
    const apiUrl = environment.apiUrl || '';
    this.http.delete(`${apiUrl}/api/account/access-codes/${saved.id}`).subscribe({
      next: () => {
        this.removingId = null;
        this.loadSharedAlbums();
      },
      error: (err) => {
        console.error('Failed to remove saved access code:', err);
        this.removingId = null;
        this.sharedErrorMessage = 'Failed to remove the album. Please try again.';
      }
    });
  }

  // -------- Admin-only album management --------

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
    this.authService.logout();
    window.location.href = '/login';
  }
}
