import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../environments/environment';

interface SavedAccessCode {
  id: string;
  code: string;
  albumId: string;
  albumTitle: string;
  savedAt: string;
  expirationDate?: string | null;
}

/**
 * Lists access codes the current authenticated user has saved to their
 * account. EPIC-02 Slice B. Each card lets the user re-open the album
 * (/code/:code) or remove the saved link.
 */
@Component({
  selector: 'app-shared-albums',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="shared-albums">
      <header>
        <h1>Shared Albums</h1>
        <p class="subtitle">Albums you've saved using an access code.</p>
      </header>

      <div *ngIf="loading" class="loading">Loading saved albums...</div>

      <div *ngIf="!loading && errorMessage" class="error">{{ errorMessage }}</div>

      <div *ngIf="!loading && !errorMessage && savedCodes.length === 0" class="empty">
        <p>You haven't saved any albums yet.</p>
        <p class="hint">
          Got a code? Visit <code>/code/&lt;your-code&gt;</code> and click
          "Save to my account".
        </p>
      </div>

      <div *ngIf="!loading && savedCodes.length > 0" class="card-grid">
        <article *ngFor="let saved of savedCodes" class="card">
          <h2>{{ saved.albumTitle || '(Untitled album)' }}</h2>
          <p class="meta">Saved {{ saved.savedAt | date: 'mediumDate' }}</p>
          <p class="meta" *ngIf="saved.expirationDate">
            Expires {{ saved.expirationDate | date: 'mediumDate' }}
          </p>
          <p class="meta" *ngIf="!saved.expirationDate">No expiration</p>
          <div class="actions">
            <button class="primary" (click)="viewAlbum(saved)">View Album</button>
            <button class="danger" (click)="remove(saved)" [disabled]="removingId === saved.id">
              {{ removingId === saved.id ? 'Removing...' : 'Remove' }}
            </button>
          </div>
        </article>
      </div>
    </div>
  `,
  styles: [`
    .shared-albums { max-width: 1200px; margin: 0 auto; padding: 24px; }
    header { margin-bottom: 24px; }
    h1 { margin: 0 0 4px 0; font-size: 26px; color: #333; }
    .subtitle { color: #666; margin: 0; }
    .loading, .error, .empty {
      text-align: center; padding: 60px 20px; color: #666;
    }
    .error { color: #c62828; }
    .empty .hint { font-size: 13px; color: #888; }
    .empty code { background: #f5f5f5; padding: 2px 6px; border-radius: 4px; }
    .card-grid {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(280px, 1fr));
      gap: 16px;
    }
    .card {
      background: white;
      border: 1px solid #e0e0e0;
      border-radius: 8px;
      padding: 16px;
      transition: box-shadow 0.15s;
    }
    .card:hover { box-shadow: 0 4px 12px rgba(0,0,0,0.08); }
    .card h2 { margin: 0 0 8px 0; font-size: 18px; color: #333; }
    .card .meta { margin: 2px 0; color: #777; font-size: 13px; }
    .actions { margin-top: 12px; display: flex; gap: 8px; }
    button { padding: 6px 12px; border-radius: 4px; cursor: pointer; font-size: 13px; border: none; }
    button.primary { background: #0066cc; color: white; }
    button.primary:hover { background: #0052a3; }
    button.danger { background: white; color: #c62828; border: 1px solid #c62828; }
    button.danger:hover:not(:disabled) { background: #ffebee; }
    button:disabled { opacity: 0.6; cursor: default; }
  `]
})
export class SharedAlbumsComponent implements OnInit {
  savedCodes: SavedAccessCode[] = [];
  loading = true;
  errorMessage = '';
  removingId: string | null = null;

  constructor(private http: HttpClient, private router: Router) {}

  ngOnInit(): void {
    this.load();
  }

  private load(): void {
    this.loading = true;
    this.errorMessage = '';
    const apiUrl = environment.apiUrl || '';
    this.http.get<SavedAccessCode[]>(`${apiUrl}/api/account/access-codes`).subscribe({
      next: (data) => {
        this.savedCodes = data ?? [];
        this.loading = false;
      },
      error: (err) => {
        console.error('Failed to load saved access codes:', err);
        this.errorMessage = 'Failed to load saved albums. Please try again later.';
        this.loading = false;
      }
    });
  }

  viewAlbum(saved: SavedAccessCode): void {
    this.router.navigate(['/code', saved.code]);
  }

  remove(saved: SavedAccessCode): void {
    if (this.removingId) return;
    this.removingId = saved.id;
    const apiUrl = environment.apiUrl || '';
    this.http.delete(`${apiUrl}/api/account/access-codes/${saved.id}`).subscribe({
      next: () => {
        this.removingId = null;
        this.load();
      },
      error: (err) => {
        console.error('Failed to remove saved access code:', err);
        this.removingId = null;
        alert('Failed to remove. Please try again.');
      }
    });
  }
}
