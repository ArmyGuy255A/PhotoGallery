import { Component, inject, signal } from '@angular/core';
import { CommonModule, DecimalPipe, DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { catchError, forkJoin, of } from 'rxjs';
import { BackToDashboardComponent } from '../back-to-dashboard/back-to-dashboard.component';
import { environment } from '../../../environments/environment';

interface OrphanReapReport {
  scanned?: number;
  orphanedAlbums?: number;
  orphanedPhotos?: number;
  blobsDeleted?: number;
  bytesReclaimed?: number;
  skippedByGracePeriod?: number;
  elapsedMs?: number;
}
interface ReconcileReport {
  scanned?: number;
  reconciled?: number;
  errors?: number;
  elapsedMs?: number;
}
interface AdminUser {
  id: string;
  email: string;
  firstName?: string | null;
  lastName?: string | null;
  roles: string[];
  lastLoginAt?: string | null;
  createdDate: string;
  isActive: boolean;
  albumCount: number;
  downloadCount: number;
}
interface AlbumDownloadStat {
  albumId: string;
  title: string;
  downloadCount: number;
  lastDownloadedAt?: string | null;
}
interface TopPhoto {
  photoId: string;
  fileName: string;
  albumId: string;
  albumTitle: string;
  downloadCount: number;
  lastDownloadedAt?: string | null;
}
interface UserDownload {
  downloadId: string;
  photoId: string;
  fileName: string;
  albumId: string;
  albumTitle: string;
  quality: string;
  downloadedAt: string;
}

type JobState = 'idle' | 'running' | 'success' | 'error';
type AdminTab = 'maintenance' | 'users' | 'stats';

/**
 * Admin Settings page (issue #70). Three tabs:
 *   - 🧹 Maintenance       — storage reap + reconcile sweeps
 *   - 👥 Users             — list, last login, role toggle, per-user drill-down
 *   - 📊 Download stats    — per-album counts + top-downloaded photos
 *
 * Tabs lazy-load data the first time they are opened so the page doesn't
 * fire four admin endpoints on mount when only one is in view. The
 * per-user drill-down lives in a modal driven off the Users tab's
 * download-count cell.
 */
@Component({
  selector: 'app-admin-settings',
  standalone: true,
  imports: [CommonModule, FormsModule, BackToDashboardComponent, DecimalPipe, DatePipe],
  template: `
    <section
      class="admin-settings"
      data-testid="admin-settings-page"
      aria-labelledby="admin-settings-title"
    >
      <app-back-to-dashboard></app-back-to-dashboard>
      <h1 id="admin-settings-title">Admin Settings</h1>

      <nav class="admin-tabs" role="tablist" aria-label="Admin sections">
        <button type="button" role="tab"
          [attr.aria-selected]="activeTab() === 'maintenance'"
          [class.active]="activeTab() === 'maintenance'"
          (click)="activeTab.set('maintenance')"
          data-testid="admin-tab-maintenance">🧹 Maintenance</button>
        <button type="button" role="tab"
          [attr.aria-selected]="activeTab() === 'users'"
          [class.active]="activeTab() === 'users'"
          (click)="onSelectTab('users')"
          data-testid="admin-tab-users">👥 Users</button>
        <button type="button" role="tab"
          [attr.aria-selected]="activeTab() === 'stats'"
          [class.active]="activeTab() === 'stats'"
          (click)="onSelectTab('stats')"
          data-testid="admin-tab-stats">📊 Download stats</button>
      </nav>

      <!-- Maintenance tab -->
      <section *ngIf="activeTab() === 'maintenance'" class="admin-card" data-testid="admin-card-storage-maintenance">
        <h2>Storage Maintenance</h2>
        <p class="card-hint">
          Run a maintenance sweep against the active storage backend (Minio in
          development, Azure Blob in production). Both jobs are synchronous —
          the page will sit on the button until the server responds with the
          per-cycle summary.
        </p>
        <div class="action-row">
          <button type="button" class="btn-primary"
            data-testid="admin-reap-orphans-button"
            [disabled]="reapState() === 'running'"
            (click)="runReapOrphans()">
            <ng-container *ngIf="reapState() !== 'running'; else reapingTpl">🧹 Reap orphaned blobs</ng-container>
            <ng-template #reapingTpl>Reaping…</ng-template>
          </button>
          <span class="action-hint">
            Deletes any blob under <code>photogallery/&lt;album&gt;/&lt;photo&gt;/</code>
            whose matching DB row no longer exists.
          </span>
        </div>
        <div *ngIf="reapError() as err" class="alert alert-error" data-testid="admin-reap-orphans-error">{{ err }}</div>
        <div *ngIf="reapReport() as r" class="alert alert-success" data-testid="admin-reap-orphans-report">
          <strong>Reap complete:</strong>
          <ul class="report-list">
            <li>{{ r.scanned | number }} prefix(es) scanned</li>
            <li>{{ r.orphanedAlbums | number }} orphaned album(s)</li>
            <li>{{ r.orphanedPhotos | number }} orphaned photo(s)</li>
            <li>{{ r.blobsDeleted | number }} blob(s) deleted</li>
            <li>{{ formatBytes(r.bytesReclaimed ?? 0) }} reclaimed</li>
            <li *ngIf="r.skippedByGracePeriod">{{ r.skippedByGracePeriod | number }} skipped (within grace window)</li>
            <li>completed in {{ r.elapsedMs | number }} ms</li>
          </ul>
        </div>
        <hr class="card-divider" />
        <div class="action-row">
          <button type="button" class="btn-secondary"
            data-testid="admin-reconcile-storage-button"
            [disabled]="reconcileState() === 'running'"
            (click)="runReconcile()">
            <ng-container *ngIf="reconcileState() !== 'running'; else reconcilingTpl">⚖️ Reconcile storage ↔ database</ng-container>
            <ng-template #reconcilingTpl>Reconciling…</ng-template>
          </button>
          <span class="action-hint">
            Walks DB rows that point at storage and verifies each blob is
            actually present; logs anything broken.
          </span>
        </div>
        <div *ngIf="reconcileError() as err" class="alert alert-error" data-testid="admin-reconcile-error">{{ err }}</div>
        <div *ngIf="reconcileReport() as r" class="alert alert-success" data-testid="admin-reconcile-report">
          <strong>Reconciliation complete:</strong>
          <ul class="report-list">
            <li>{{ r.scanned | number }} item(s) scanned</li>
            <li>{{ r.reconciled | number }} reconciled</li>
            <li *ngIf="r.errors">{{ r.errors | number }} error(s)</li>
            <li>completed in {{ r.elapsedMs | number }} ms</li>
          </ul>
        </div>
      </section>

      <!-- Users tab -->
      <section *ngIf="activeTab() === 'users'" class="admin-card" data-testid="admin-card-users">
        <h2>Users</h2>
        <p class="card-hint">
          Every signed-up user, sorted by most-recent login. Toggle the Admin
          role inline. The last administrator cannot be demoted to a regular
          user — promote someone else first.
        </p>
        <div *ngIf="usersError() as err" class="alert alert-error" data-testid="admin-users-error">{{ err }}</div>
        <div *ngIf="usersLoading()" class="loading-row">Loading users…</div>
        <table *ngIf="users().length > 0" class="admin-table" data-testid="admin-users-table">
          <thead>
            <tr>
              <th>Email</th>
              <th>Name</th>
              <th>Last login</th>
              <th class="num">Albums</th>
              <th class="num">Downloads</th>
              <th>Roles</th>
              <th>Admin</th>
            </tr>
          </thead>
          <tbody>
            <tr *ngFor="let u of users()" [attr.data-user-id]="u.id">
              <td>{{ u.email }}</td>
              <td>{{ formatName(u) }}</td>
              <td>
                <span *ngIf="u.lastLoginAt; else neverLoggedIn">{{ u.lastLoginAt | date:'short' }}</span>
                <ng-template #neverLoggedIn><span class="muted">never</span></ng-template>
              </td>
              <td class="num">{{ u.albumCount | number }}</td>
              <td class="num">
                <button *ngIf="u.downloadCount > 0; else zeroDownloads"
                  type="button" class="link-button"
                  data-testid="admin-user-downloads-link"
                  (click)="openUserDownloads(u)">{{ u.downloadCount | number }}</button>
                <ng-template #zeroDownloads><span class="muted">0</span></ng-template>
              </td>
              <td>
                <span *ngFor="let r of u.roles" class="role-chip" [class.role-admin]="r === 'Admin'">{{ r }}</span>
              </td>
              <td>
                <label class="role-toggle">
                  <input type="checkbox" [checked]="isAdmin(u)"
                    [disabled]="roleUpdateFor() === u.id"
                    (change)="toggleAdmin(u, $event)"
                    [attr.data-testid]="'admin-user-toggle-' + u.id" />
                </label>
              </td>
            </tr>
          </tbody>
        </table>
      </section>

      <!-- Download stats tab -->
      <section *ngIf="activeTab() === 'stats'" class="admin-card" data-testid="admin-card-stats">
        <h2>Download analytics</h2>
        <p class="card-hint">Aggregate download counts across the catalogue.</p>
        <div *ngIf="statsError() as err" class="alert alert-error">{{ err }}</div>
        <div *ngIf="statsLoading()" class="loading-row">Loading stats…</div>

        <div class="stats-grid">
          <div class="stats-card">
            <h3>Downloads per album</h3>
            <table class="admin-table" *ngIf="albumStats().length > 0; else noAlbumStats">
              <thead><tr><th>Album</th><th class="num">Downloads</th><th>Last</th></tr></thead>
              <tbody>
                <tr *ngFor="let a of albumStats()" [attr.data-album-id]="a.albumId">
                  <td>{{ a.title }}</td>
                  <td class="num">{{ a.downloadCount | number }}</td>
                  <td>{{ a.lastDownloadedAt ? (a.lastDownloadedAt | date:'short') : '—' }}</td>
                </tr>
              </tbody>
            </table>
            <ng-template #noAlbumStats><p class="muted">No downloads recorded yet.</p></ng-template>
          </div>

          <div class="stats-card">
            <h3>Top downloaded photos</h3>
            <table class="admin-table" *ngIf="topPhotos().length > 0; else noTopPhotos">
              <thead><tr><th>Photo</th><th>Album</th><th class="num">Downloads</th></tr></thead>
              <tbody>
                <tr *ngFor="let p of topPhotos()" [attr.data-photo-id]="p.photoId">
                  <td>{{ p.fileName }}</td>
                  <td>{{ p.albumTitle }}</td>
                  <td class="num">{{ p.downloadCount | number }}</td>
                </tr>
              </tbody>
            </table>
            <ng-template #noTopPhotos><p class="muted">No downloads recorded yet.</p></ng-template>
          </div>
        </div>
      </section>

      <!-- Per-user downloads drill-down modal -->
      <div *ngIf="drilldownUser() as user" class="modal-backdrop"
        (click)="closeDrilldown()" data-testid="admin-user-downloads-modal">
        <div class="modal" (click)="$event.stopPropagation()">
          <header class="modal-head">
            <h2>{{ user.email }} — downloads</h2>
            <button type="button" class="aside-dismiss"
              (click)="closeDrilldown()" aria-label="Close">×</button>
          </header>
          <div class="modal-body">
            <div *ngIf="drilldownError() as err" class="alert alert-error">{{ err }}</div>
            <div *ngIf="drilldownLoading()" class="loading-row">Loading downloads…</div>
            <p *ngIf="!drilldownLoading() && drilldownDownloads().length === 0" class="muted">
              No cart downloads recorded for this user yet (access-code downloads aren't tracked per-user).
            </p>
            <table *ngIf="drilldownDownloads().length > 0" class="admin-table">
              <thead><tr><th>When</th><th>Photo</th><th>Album</th><th>Quality</th></tr></thead>
              <tbody>
                <tr *ngFor="let d of drilldownDownloads()">
                  <td>{{ d.downloadedAt | date:'short' }}</td>
                  <td>{{ d.fileName }}</td>
                  <td>{{ d.albumTitle }}</td>
                  <td>{{ d.quality }}</td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>
      </div>
    </section>
  `,
  styles: [`
    .admin-settings { padding: 24px; max-width: 1200px; margin: 0 auto; }
    h1 { margin: 8px 0 12px; font-size: 24px; }

    .admin-tabs {
      display: flex; gap: 4px; margin: 16px 0 0;
      border-bottom: 1px solid #e0e6ed;
    }
    .admin-tabs button {
      padding: 10px 16px; border: none; background: transparent;
      cursor: pointer; font-size: 14px; font-weight: 500; color: #6b7280;
      border-bottom: 2px solid transparent;
    }
    .admin-tabs button.active {
      color: #0d6efd; border-bottom-color: #0d6efd;
    }

    .admin-card {
      background: #ffffff; border: 1px solid #e0e6ed; border-radius: 12px;
      padding: 20px; margin: 16px 0; box-shadow: 0 1px 3px rgba(0,0,0,0.04);
    }
    .admin-card h2 { margin: 0 0 6px; font-size: 18px; color: #1f2937; }
    .card-hint { color: #6b7280; font-size: 13px; margin: 0 0 16px; }
    .card-divider { border: none; border-top: 1px solid #f0f1f3; margin: 20px 0; }
    .action-row { display: flex; align-items: center; gap: 12px; flex-wrap: wrap; }
    .action-hint { color: #6b7280; font-size: 12px; max-width: 520px; }
    .action-hint code { background: #f4f5f7; padding: 1px 5px; border-radius: 4px; font-size: 11px; }

    .btn-primary, .btn-secondary {
      padding: 8px 16px; border-radius: 6px; font-size: 14px; font-weight: 600;
      cursor: pointer; border: 1px solid transparent; transition: background 0.15s ease;
    }
    .btn-primary { background: #0d6efd; color: white; }
    .btn-primary:hover:not(:disabled) { background: #0b5ed7; }
    .btn-secondary { background: #ffffff; color: #1f2937; border-color: #d1d5db; }
    .btn-secondary:hover:not(:disabled) { background: #f9fafb; }
    .btn-primary:disabled, .btn-secondary:disabled { opacity: 0.6; cursor: not-allowed; }

    .alert { padding: 10px 14px; border-radius: 8px; margin-top: 12px; font-size: 13px; }
    .alert-error { background: #fef2f2; border: 1px solid #fecaca; color: #991b1b; }
    .alert-success { background: #ecfdf5; border: 1px solid #a7f3d0; color: #065f46; }
    .report-list { margin: 6px 0 0; padding-left: 20px; }
    .loading-row { padding: 16px; color: #6b7280; font-size: 13px; }

    .admin-table {
      width: 100%; border-collapse: collapse; margin-top: 12px; font-size: 13px;
    }
    .admin-table th, .admin-table td {
      padding: 8px 10px; border-bottom: 1px solid #f0f1f3; text-align: left;
    }
    .admin-table th { color: #6b7280; font-weight: 600; font-size: 11px; text-transform: uppercase; letter-spacing: 0.04em; }
    .admin-table .num { text-align: right; font-variant-numeric: tabular-nums; }
    .admin-table tbody tr:hover { background: #f9fafb; }
    .muted { color: #9ca3af; }

    .role-chip {
      display: inline-block; padding: 2px 8px; margin: 0 4px 2px 0;
      border-radius: 999px; font-size: 11px; background: #f3f4f6; color: #374151;
    }
    .role-chip.role-admin { background: #fef3c7; color: #92400e; }
    .role-toggle { display: inline-flex; align-items: center; gap: 4px; cursor: pointer; }
    .link-button {
      background: transparent; border: none; color: #0d6efd;
      cursor: pointer; padding: 0; font: inherit; text-decoration: underline;
    }

    .stats-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(360px, 1fr)); gap: 16px; }
    .stats-card h3 { margin: 0 0 8px; font-size: 14px; color: #374151; }

    .modal-backdrop {
      position: fixed; inset: 0; background: rgba(0,0,0,0.4);
      display: flex; align-items: center; justify-content: center; z-index: 2000;
    }
    .modal {
      background: #fff; border-radius: 12px; width: min(720px, 90vw);
      max-height: 80vh; display: flex; flex-direction: column; overflow: hidden;
    }
    .modal-head {
      display: flex; justify-content: space-between; align-items: center;
      padding: 14px 18px; border-bottom: 1px solid #e5e7eb;
    }
    .modal-head h2 { margin: 0; font-size: 16px; }
    .modal-body { padding: 14px 18px; overflow: auto; }
    .aside-dismiss {
      background: transparent; border: none; font-size: 22px;
      line-height: 1; color: #6b7280; cursor: pointer;
    }
  `]
})
export class AdminSettingsComponent {
  private readonly http = inject(HttpClient);

  // Tab state
  readonly activeTab = signal<AdminTab>('maintenance');

  // Maintenance
  readonly reapState = signal<JobState>('idle');
  readonly reapReport = signal<OrphanReapReport | null>(null);
  readonly reapError = signal<string | null>(null);
  readonly reconcileState = signal<JobState>('idle');
  readonly reconcileReport = signal<ReconcileReport | null>(null);
  readonly reconcileError = signal<string | null>(null);

  // Users
  readonly users = signal<AdminUser[]>([]);
  readonly usersLoading = signal<boolean>(false);
  readonly usersError = signal<string | null>(null);
  readonly roleUpdateFor = signal<string | null>(null);
  private usersLoaded = false;

  // Stats
  readonly albumStats = signal<AlbumDownloadStat[]>([]);
  readonly topPhotos = signal<TopPhoto[]>([]);
  readonly statsLoading = signal<boolean>(false);
  readonly statsError = signal<string | null>(null);
  private statsLoaded = false;

  // Drill-down modal
  readonly drilldownUser = signal<AdminUser | null>(null);
  readonly drilldownDownloads = signal<UserDownload[]>([]);
  readonly drilldownLoading = signal<boolean>(false);
  readonly drilldownError = signal<string | null>(null);

  onSelectTab(tab: AdminTab): void {
    this.activeTab.set(tab);
    if (tab === 'users' && !this.usersLoaded) this.loadUsers();
    if (tab === 'stats' && !this.statsLoaded) this.loadStats();
  }

  runReapOrphans(): void {
    this.reapState.set('running');
    this.reapReport.set(null);
    this.reapError.set(null);
    this.http.post<OrphanReapReport>(`${environment.apiUrl}/api/photos/admin/reap-orphans`, {})
      .pipe(catchError(err => {
        this.reapState.set('error');
        this.reapError.set(this.extractErrorMessage(err));
        return of(null);
      }))
      .subscribe(report => {
        if (!report) return;
        this.reapState.set('success');
        this.reapReport.set(report);
      });
  }

  runReconcile(): void {
    this.reconcileState.set('running');
    this.reconcileReport.set(null);
    this.reconcileError.set(null);
    this.http.post<ReconcileReport>(`${environment.apiUrl}/api/photos/admin/reconcile-storage`, {})
      .pipe(catchError(err => {
        this.reconcileState.set('error');
        this.reconcileError.set(this.extractErrorMessage(err));
        return of(null);
      }))
      .subscribe(report => {
        if (!report) return;
        this.reconcileState.set('success');
        this.reconcileReport.set(report);
      });
  }

  private loadUsers(): void {
    this.usersLoading.set(true);
    this.usersError.set(null);
    this.http.get<AdminUser[]>(`${environment.apiUrl}/api/admin/users`)
      .pipe(catchError(err => {
        this.usersError.set(this.extractErrorMessage(err));
        return of<AdminUser[]>([]);
      }))
      .subscribe(list => {
        this.users.set(list);
        this.usersLoading.set(false);
        this.usersLoaded = true;
      });
  }

  private loadStats(): void {
    this.statsLoading.set(true);
    this.statsError.set(null);
    forkJoin({
      albums: this.http.get<AlbumDownloadStat[]>(`${environment.apiUrl}/api/admin/stats/downloads-by-album`),
      photos: this.http.get<TopPhoto[]>(`${environment.apiUrl}/api/admin/stats/top-downloaded-photos`)
    }).pipe(catchError(err => {
      this.statsError.set(this.extractErrorMessage(err));
      return of({ albums: [] as AlbumDownloadStat[], photos: [] as TopPhoto[] });
    })).subscribe(({ albums, photos }) => {
      this.albumStats.set(albums);
      this.topPhotos.set(photos);
      this.statsLoading.set(false);
      this.statsLoaded = true;
    });
  }

  isAdmin(u: AdminUser): boolean {
    return u.roles.some(r => r === 'Admin');
  }

  formatName(u: AdminUser): string {
    const n = [u.firstName, u.lastName].filter(x => !!x).join(' ').trim();
    return n || '—';
  }

  toggleAdmin(u: AdminUser, evt: Event): void {
    const want = (evt.target as HTMLInputElement).checked;
    const next = want
      ? Array.from(new Set([...u.roles, 'Admin']))
      : u.roles.filter(r => r !== 'Admin');
    if (next.length === 0) next.push('User');

    this.roleUpdateFor.set(u.id);
    this.http.put<AdminUser>(`${environment.apiUrl}/api/admin/users/${u.id}/roles`, { roles: next })
      .pipe(catchError(err => {
        this.usersError.set(this.extractErrorMessage(err));
        // Revert checkbox visually by triggering a reload
        this.loadUsers();
        return of(null);
      }))
      .subscribe(updated => {
        this.roleUpdateFor.set(null);
        if (!updated) return;
        // Splice in the updated user so the table re-sorts naturally on next reload
        this.users.update(list => list.map(x => x.id === updated.id ? { ...x, roles: updated.roles } : x));
      });
  }

  openUserDownloads(u: AdminUser): void {
    this.drilldownUser.set(u);
    this.drilldownLoading.set(true);
    this.drilldownError.set(null);
    this.drilldownDownloads.set([]);
    this.http.get<UserDownload[]>(`${environment.apiUrl}/api/admin/users/${u.id}/downloads`)
      .pipe(catchError(err => {
        this.drilldownError.set(this.extractErrorMessage(err));
        return of<UserDownload[]>([]);
      }))
      .subscribe(rows => {
        this.drilldownDownloads.set(rows);
        this.drilldownLoading.set(false);
      });
  }

  closeDrilldown(): void {
    this.drilldownUser.set(null);
    this.drilldownDownloads.set([]);
    this.drilldownError.set(null);
  }

  formatBytes(bytes: number): string {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    if (bytes < 1024 * 1024 * 1024) return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
    return `${(bytes / (1024 * 1024 * 1024)).toFixed(2)} GB`;
  }

  private extractErrorMessage(err: unknown): string {
    const e = err as { error?: { message?: string; error?: string }; status?: number; message?: string };
    if (e?.error?.message) return e.error.message;
    if (e?.error?.error) return e.error.error;
    if (e?.status === 403) return 'Forbidden — this page requires an admin account.';
    if (e?.status === 401) return 'Unauthorized — please sign in as an admin.';
    if (e?.status === 409) return 'Conflict — see server message.';
    return e?.message || 'Request failed';
  }
}
