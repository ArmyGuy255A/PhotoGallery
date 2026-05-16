import { Component, inject, signal } from '@angular/core';
import { CommonModule, DecimalPipe, DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { catchError, of, Subject, debounceTime, distinctUntilChanged } from 'rxjs';
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
  loginCount: number;
  createdDate: string;
  isActive: boolean;
  albumCount: number;
  downloadCount: number;
}
interface AdminUserPage {
  items: AdminUser[];
  totalCount: number;
  page: number;
  pageSize: number;
  hasMore: boolean;
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
interface AccessCodeStat {
  codeId: string;
  code: string;
  albumId: string;
  albumTitle: string;
  accessCount: number;
  distinctIps: number;
  distinctUserAgents: number;
  photoDownloadCount: number;
  lastAccessedAt?: string | null;
  createdDate: string;
  expirationDate?: string | null;
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
type UserSortKey = 'email' | 'name' | 'lastLogin' | 'loginCount' | 'downloads' | 'albums' | 'created';
type SortDir = 'asc' | 'desc';

@Component({
  selector: 'app-admin-settings',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, BackToDashboardComponent, DecimalPipe, DatePipe],
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
          Sortable, searchable user list with server-side pagination. Toggle
          the Admin role inline. The last administrator cannot be demoted —
          promote someone else first.
        </p>

        <div class="toolbar">
          <input type="search"
            class="search-input"
            placeholder="Search by email or name…"
            data-testid="admin-users-search"
            [ngModel]="search()"
            (ngModelChange)="onSearchChange($event)" />
          <label class="pagesize-label">
            Page size
            <select [ngModel]="pageSize()" (ngModelChange)="onPageSizeChange($event)" data-testid="admin-users-pagesize">
              <option [ngValue]="25">25</option>
              <option [ngValue]="50">50</option>
              <option [ngValue]="100">100</option>
              <option [ngValue]="200">200</option>
            </select>
          </label>
          <span class="muted" *ngIf="usersPage() as p">
            {{ p.totalCount | number }} user(s) total
          </span>
        </div>

        <div *ngIf="usersError() as err" class="alert alert-error" data-testid="admin-users-error">{{ err }}</div>
        <div *ngIf="usersLoading()" class="loading-row">Loading users…</div>

        <table *ngIf="usersPage() as p" class="admin-table sortable" data-testid="admin-users-table">
          <thead>
            <tr>
              <th (click)="onSort('email')" class="sortable-th">
                Email <span class="sort-indicator">{{ sortIndicator('email') }}</span>
              </th>
              <th (click)="onSort('name')" class="sortable-th">
                Name <span class="sort-indicator">{{ sortIndicator('name') }}</span>
              </th>
              <th (click)="onSort('lastLogin')" class="sortable-th">
                Last login <span class="sort-indicator">{{ sortIndicator('lastLogin') }}</span>
              </th>
              <th class="num sortable-th" (click)="onSort('loginCount')">
                Logins <span class="sort-indicator">{{ sortIndicator('loginCount') }}</span>
              </th>
              <th class="num sortable-th" (click)="onSort('albums')">
                Albums <span class="sort-indicator">{{ sortIndicator('albums') }}</span>
              </th>
              <th class="num sortable-th" (click)="onSort('downloads')">
                Downloads <span class="sort-indicator">{{ sortIndicator('downloads') }}</span>
              </th>
              <th>Roles</th>
              <th>Admin</th>
            </tr>
          </thead>
          <tbody>
            <tr *ngFor="let u of p.items" [attr.data-user-id]="u.id">
              <td>{{ u.email }}</td>
              <td>{{ formatName(u) }}</td>
              <td>
                <span *ngIf="u.lastLoginAt; else neverLoggedIn">{{ u.lastLoginAt | date:'short' }}</span>
                <ng-template #neverLoggedIn><span class="muted">never</span></ng-template>
              </td>
              <td class="num">{{ u.loginCount | number }}</td>
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
            <tr *ngIf="p.items.length === 0">
              <td colspan="8" class="muted center">No users match the current filter.</td>
            </tr>
          </tbody>
        </table>

        <div *ngIf="usersPage() as p" class="pagination">
          <button type="button" class="btn-secondary"
            (click)="onPrevPage()" [disabled]="page() <= 1"
            data-testid="admin-users-prev">‹ Prev</button>
          <span class="page-indicator">
            Page {{ p.page }} of {{ Math.max(1, Math.ceil(p.totalCount / p.pageSize)) }}
          </span>
          <button type="button" class="btn-secondary"
            (click)="onNextPage()" [disabled]="!p.hasMore"
            data-testid="admin-users-next">Next ›</button>
        </div>
      </section>

      <!-- Download stats tab -->
      <section *ngIf="activeTab() === 'stats'" class="admin-card" data-testid="admin-card-stats">
        <h2>Download analytics</h2>
        <p class="card-hint">
          Click an album row to see its top-downloaded photos with direct
          links into the album view.
        </p>
        <div *ngIf="statsError() as err" class="alert alert-error">{{ err }}</div>
        <div *ngIf="statsLoading()" class="loading-row">Loading stats…</div>

        <h3>Downloads per album</h3>
        <table *ngIf="albumStats().length > 0; else noAlbumStats" class="admin-table clickable-rows" data-testid="admin-album-stats-table">
          <thead><tr><th>Album</th><th class="num">Downloads</th><th>Last</th><th></th></tr></thead>
          <tbody>
            <tr *ngFor="let a of albumStats()"
              [attr.data-album-id]="a.albumId"
              (click)="openAlbumDrilldown(a)"
              class="row-clickable"
              data-testid="admin-album-stats-row">
              <td>{{ a.title }}</td>
              <td class="num">{{ a.downloadCount | number }}</td>
              <td>{{ a.lastDownloadedAt ? (a.lastDownloadedAt | date:'short') : '—' }}</td>
              <td class="row-arrow">›</td>
            </tr>
          </tbody>
        </table>
        <ng-template #noAlbumStats><p class="muted">No downloads recorded yet.</p></ng-template>

        <hr class="card-divider" />

        <h3>Access code usage</h3>
        <p class="muted small">
          Every code, sorted by access count. Distinct IP / Browser columns
          show how many unique clients have hit each code (logged on the
          <code>/code/&lt;code&gt;/validate</code> call).
        </p>
        <table class="admin-table" *ngIf="accessCodeStats().length > 0; else noCodeStats" data-testid="admin-access-codes-table">
          <thead>
            <tr>
              <th>Code</th>
              <th>Album</th>
              <th class="num">Accesses</th>
              <th class="num">IPs</th>
              <th class="num">Browsers</th>
              <th class="num">Photos</th>
              <th>Last access</th>
              <th>Expires</th>
            </tr>
          </thead>
          <tbody>
            <tr *ngFor="let c of accessCodeStats()" [attr.data-code-id]="c.codeId">
              <td><code>{{ c.code }}</code></td>
              <td><a [routerLink]="['/albums', c.albumId]" class="link-button">{{ c.albumTitle }}</a></td>
              <td class="num">{{ c.accessCount | number }}</td>
              <td class="num">{{ c.distinctIps | number }}</td>
              <td class="num">{{ c.distinctUserAgents | number }}</td>
              <td class="num">{{ c.photoDownloadCount | number }}</td>
              <td>{{ c.lastAccessedAt ? (c.lastAccessedAt | date:'short') : '—' }}</td>
              <td>{{ c.expirationDate ? (c.expirationDate | date:'shortDate') : 'never' }}</td>
            </tr>
          </tbody>
        </table>
        <ng-template #noCodeStats><p class="muted">No access codes recorded yet.</p></ng-template>
      </section>

      <!-- Per-album top-photos drill-down modal -->
      <div *ngIf="albumDrilldown() as album"
        class="modal-backdrop" (click)="closeAlbumDrilldown()"
        data-testid="admin-album-drilldown-modal">
        <div class="modal" (click)="$event.stopPropagation()">
          <header class="modal-head">
            <h2>{{ album.title }} — top photos</h2>
            <button type="button" class="aside-dismiss"
              (click)="closeAlbumDrilldown()" aria-label="Close">×</button>
          </header>
          <div class="modal-body">
            <div *ngIf="albumPhotosError() as err" class="alert alert-error">{{ err }}</div>
            <div *ngIf="albumPhotosLoading()" class="loading-row">Loading photos…</div>
            <p *ngIf="!albumPhotosLoading() && albumPhotos().length === 0" class="muted">
              No download data for this album yet.
            </p>
            <table *ngIf="albumPhotos().length > 0" class="admin-table">
              <thead>
                <tr>
                  <th>Photo</th>
                  <th class="num">Downloads</th>
                  <th>Last</th>
                </tr>
              </thead>
              <tbody>
                <tr *ngFor="let p of albumPhotos()" [attr.data-photo-id]="p.photoId">
                  <td>
                    <a [routerLink]="['/albums', p.albumId]"
                      [queryParams]="{ photoId: p.photoId }"
                      class="link-button"
                      (click)="closeAlbumDrilldown()"
                      data-testid="admin-album-photo-link">{{ p.fileName }}</a>
                  </td>
                  <td class="num">{{ p.downloadCount | number }}</td>
                  <td>{{ p.lastDownloadedAt ? (p.lastDownloadedAt | date:'short') : '—' }}</td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>
      </div>

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
                  <td>
                    <a [routerLink]="['/albums', d.albumId]"
                      [queryParams]="{ photoId: d.photoId }"
                      class="link-button"
                      (click)="closeDrilldown()">{{ d.fileName }}</a>
                  </td>
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
    h3 { margin: 16px 0 8px; font-size: 14px; color: #374151; }

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

    .toolbar {
      display: flex; align-items: center; gap: 12px; margin-bottom: 12px;
      flex-wrap: wrap;
    }
    .search-input {
      flex: 1; min-width: 220px;
      padding: 8px 12px; border: 1px solid #d1d5db; border-radius: 6px;
      font-size: 14px;
    }
    .pagesize-label {
      display: inline-flex; align-items: center; gap: 6px;
      color: #6b7280; font-size: 12px;
    }
    .pagesize-label select {
      padding: 4px 8px; border-radius: 4px; border: 1px solid #d1d5db;
      font-size: 13px;
    }

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
    .admin-table.sortable .sortable-th { cursor: pointer; user-select: none; }
    .admin-table.sortable .sortable-th:hover { background: #f3f4f6; }
    .sort-indicator { color: #9ca3af; font-size: 10px; margin-left: 2px; }
    .admin-table.clickable-rows .row-clickable { cursor: pointer; }
    .admin-table .row-arrow { color: #9ca3af; text-align: right; }
    .muted { color: #9ca3af; }
    .center { text-align: center; }
    .small { font-size: 12px; }

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

    .pagination {
      display: flex; align-items: center; justify-content: center; gap: 12px;
      margin-top: 16px; font-size: 13px;
    }
    .page-indicator { color: #6b7280; }

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
  /** Exposed so the template can reach Math.ceil without a method wrapper. */
  readonly Math = Math;

  // Tab state
  readonly activeTab = signal<AdminTab>('maintenance');

  // Maintenance
  readonly reapState = signal<JobState>('idle');
  readonly reapReport = signal<OrphanReapReport | null>(null);
  readonly reapError = signal<string | null>(null);
  readonly reconcileState = signal<JobState>('idle');
  readonly reconcileReport = signal<ReconcileReport | null>(null);
  readonly reconcileError = signal<string | null>(null);

  // Users — paginated/sorted/filtered
  readonly usersPage = signal<AdminUserPage | null>(null);
  readonly usersLoading = signal<boolean>(false);
  readonly usersError = signal<string | null>(null);
  readonly roleUpdateFor = signal<string | null>(null);
  readonly search = signal<string>('');
  readonly page = signal<number>(1);
  readonly pageSize = signal<number>(25);
  readonly sortBy = signal<UserSortKey>('email');
  readonly sortDir = signal<SortDir>('asc');
  private readonly searchInput$ = new Subject<string>();
  private usersLoaded = false;

  // Stats
  readonly albumStats = signal<AlbumDownloadStat[]>([]);
  readonly topPhotos = signal<TopPhoto[]>([]); // retained for future use
  readonly accessCodeStats = signal<AccessCodeStat[]>([]);
  readonly statsLoading = signal<boolean>(false);
  readonly statsError = signal<string | null>(null);
  private statsLoaded = false;

  // Per-album drill-down modal
  readonly albumDrilldown = signal<AlbumDownloadStat | null>(null);
  readonly albumPhotos = signal<TopPhoto[]>([]);
  readonly albumPhotosLoading = signal<boolean>(false);
  readonly albumPhotosError = signal<string | null>(null);

  // Per-user drill-down modal
  readonly drilldownUser = signal<AdminUser | null>(null);
  readonly drilldownDownloads = signal<UserDownload[]>([]);
  readonly drilldownLoading = signal<boolean>(false);
  readonly drilldownError = signal<string | null>(null);

  constructor() {
    // Debounced search → reload users from page 1.
    this.searchInput$
      .pipe(debounceTime(300), distinctUntilChanged())
      .subscribe(() => {
        this.page.set(1);
        this.loadUsers();
      });
  }

  onSelectTab(tab: AdminTab): void {
    this.activeTab.set(tab);
    if (tab === 'users' && !this.usersLoaded) this.loadUsers();
    if (tab === 'stats' && !this.statsLoaded) this.loadStats();
  }

  // ---- Maintenance ---------------------------------------------------------

  runReapOrphans(): void {
    this.reapState.set('running');
    this.reapReport.set(null);
    this.reapError.set(null);
    this.http.post<OrphanReapReport>(`${environment.apiUrl}/api/photos/admin/reap-orphans`, {})
      .pipe(catchError(err => { this.reapState.set('error'); this.reapError.set(this.extractErrorMessage(err)); return of(null); }))
      .subscribe(report => { if (!report) return; this.reapState.set('success'); this.reapReport.set(report); });
  }

  runReconcile(): void {
    this.reconcileState.set('running');
    this.reconcileReport.set(null);
    this.reconcileError.set(null);
    this.http.post<ReconcileReport>(`${environment.apiUrl}/api/photos/admin/reconcile-storage`, {})
      .pipe(catchError(err => { this.reconcileState.set('error'); this.reconcileError.set(this.extractErrorMessage(err)); return of(null); }))
      .subscribe(report => { if (!report) return; this.reconcileState.set('success'); this.reconcileReport.set(report); });
  }

  // ---- Users ---------------------------------------------------------------

  onSearchChange(value: string): void {
    this.search.set(value ?? '');
    this.searchInput$.next(value ?? '');
  }

  onPageSizeChange(value: number): void {
    this.pageSize.set(value);
    this.page.set(1);
    this.loadUsers();
  }

  onSort(key: UserSortKey): void {
    if (this.sortBy() === key) {
      this.sortDir.set(this.sortDir() === 'asc' ? 'desc' : 'asc');
    } else {
      this.sortBy.set(key);
      this.sortDir.set(key === 'lastLogin' || key === 'loginCount' || key === 'downloads' || key === 'albums' ? 'desc' : 'asc');
    }
    this.page.set(1);
    this.loadUsers();
  }

  sortIndicator(key: UserSortKey): string {
    if (this.sortBy() !== key) return '↕';
    return this.sortDir() === 'asc' ? '▲' : '▼';
  }

  onPrevPage(): void {
    if (this.page() > 1) { this.page.update(p => p - 1); this.loadUsers(); }
  }
  onNextPage(): void {
    if (this.usersPage()?.hasMore) { this.page.update(p => p + 1); this.loadUsers(); }
  }

  private loadUsers(): void {
    this.usersLoading.set(true);
    this.usersError.set(null);
    const params = new URLSearchParams({
      page: this.page().toString(),
      pageSize: this.pageSize().toString(),
      sortBy: this.sortBy(),
      sortDir: this.sortDir()
    });
    const s = this.search().trim();
    if (s) params.set('search', s);
    this.http.get<AdminUserPage>(`${environment.apiUrl}/api/admin/users?${params.toString()}`)
      .pipe(catchError(err => {
        this.usersError.set(this.extractErrorMessage(err));
        return of<AdminUserPage>({ items: [], totalCount: 0, page: 1, pageSize: this.pageSize(), hasMore: false });
      }))
      .subscribe(p => {
        this.usersPage.set(p);
        this.usersLoading.set(false);
        this.usersLoaded = true;
      });
  }

  isAdmin(u: AdminUser): boolean { return u.roles.some(r => r === 'Admin'); }
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
        this.loadUsers();
        return of(null);
      }))
      .subscribe(updated => {
        this.roleUpdateFor.set(null);
        if (!updated) return;
        const page = this.usersPage();
        if (!page) return;
        this.usersPage.set({
          ...page,
          items: page.items.map(x => x.id === updated.id ? { ...x, roles: updated.roles } : x)
        });
      });
  }

  // ---- Stats ---------------------------------------------------------------

  private loadStats(): void {
    this.statsLoading.set(true);
    this.statsError.set(null);
    Promise.all([
      this.http.get<AlbumDownloadStat[]>(`${environment.apiUrl}/api/admin/stats/downloads-by-album`).toPromise(),
      this.http.get<AccessCodeStat[]>(`${environment.apiUrl}/api/admin/stats/access-codes`).toPromise()
    ]).then(([albums, codes]) => {
      this.albumStats.set(albums ?? []);
      this.accessCodeStats.set(codes ?? []);
      this.statsLoading.set(false);
      this.statsLoaded = true;
    }).catch(err => {
      this.statsError.set(this.extractErrorMessage(err));
      this.statsLoading.set(false);
    });
  }

  openAlbumDrilldown(a: AlbumDownloadStat): void {
    this.albumDrilldown.set(a);
    this.albumPhotos.set([]);
    this.albumPhotosError.set(null);
    this.albumPhotosLoading.set(true);
    this.http.get<TopPhoto[]>(`${environment.apiUrl}/api/admin/stats/album/${a.albumId}/top-photos`)
      .pipe(catchError(err => { this.albumPhotosError.set(this.extractErrorMessage(err)); return of<TopPhoto[]>([]); }))
      .subscribe(rows => { this.albumPhotos.set(rows); this.albumPhotosLoading.set(false); });
  }
  closeAlbumDrilldown(): void {
    this.albumDrilldown.set(null);
    this.albumPhotos.set([]);
    this.albumPhotosError.set(null);
  }

  // ---- Per-user drill-down -------------------------------------------------

  openUserDownloads(u: AdminUser): void {
    this.drilldownUser.set(u);
    this.drilldownLoading.set(true);
    this.drilldownError.set(null);
    this.drilldownDownloads.set([]);
    this.http.get<UserDownload[]>(`${environment.apiUrl}/api/admin/users/${u.id}/downloads`)
      .pipe(catchError(err => { this.drilldownError.set(this.extractErrorMessage(err)); return of<UserDownload[]>([]); }))
      .subscribe(rows => { this.drilldownDownloads.set(rows); this.drilldownLoading.set(false); });
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
