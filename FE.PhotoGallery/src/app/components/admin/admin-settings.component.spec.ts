import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { AdminSettingsComponent } from './admin-settings.component';
import { environment } from '../../../environments/environment';

describe('AdminSettingsComponent', () => {
  let fixture: ComponentFixture<AdminSettingsComponent>;
  let component: AdminSettingsComponent;
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AdminSettingsComponent],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();

    fixture = TestBed.createComponent(AdminSettingsComponent);
    component = fixture.componentInstance;
    httpMock = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
  });

  afterEach(() => httpMock.verify());

  it('creates the component', () => {
    expect(component).toBeTruthy();
  });

  it('renders the page wrapper with the data-testid', () => {
    const wrapper = fixture.nativeElement.querySelector('[data-testid="admin-settings-page"]');
    expect(wrapper).toBeTruthy();
  });

  it('renders the Admin Settings heading', () => {
    const h1: HTMLHeadingElement = fixture.nativeElement.querySelector('h1');
    expect(h1).toBeTruthy();
    expect(h1.textContent?.trim()).toBe('Admin Settings');
  });

  it('renders the Storage Maintenance card with both action buttons', () => {
    const host: HTMLElement = fixture.nativeElement;
    expect(host.querySelector('[data-testid="admin-card-storage-maintenance"]')).toBeTruthy();
    expect(host.querySelector('[data-testid="admin-reap-orphans-button"]')).toBeTruthy();
    expect(host.querySelector('[data-testid="admin-reconcile-storage-button"]')).toBeTruthy();
  });

  it('reap-orphans button POSTs to the admin endpoint and renders the summary', () => {
    const btn = fixture.nativeElement.querySelector('[data-testid="admin-reap-orphans-button"]') as HTMLButtonElement;
    btn.click();

    const req = httpMock.expectOne(`${environment.apiUrl}/api/photos/admin/reap-orphans`);
    expect(req.request.method).toBe('POST');
    req.flush({
      scanned: 12, orphanedAlbums: 1, orphanedPhotos: 4, blobsDeleted: 20,
      bytesReclaimed: 5_242_880, skippedByGracePeriod: 0, elapsedMs: 123
    });
    fixture.detectChanges();

    const report = fixture.nativeElement.querySelector('[data-testid="admin-reap-orphans-report"]');
    expect(report).toBeTruthy();
    expect(report.textContent).toContain('20');
    expect(report.textContent).toContain('5.0 MB');
  });

  it('reap-orphans surfaces a friendly forbidden error for 403', () => {
    const btn = fixture.nativeElement.querySelector('[data-testid="admin-reap-orphans-button"]') as HTMLButtonElement;
    btn.click();
    const req = httpMock.expectOne(`${environment.apiUrl}/api/photos/admin/reap-orphans`);
    req.flush('forbidden', { status: 403, statusText: 'Forbidden' });
    fixture.detectChanges();

    const err = fixture.nativeElement.querySelector('[data-testid="admin-reap-orphans-error"]');
    expect(err).toBeTruthy();
    expect(err.textContent).toContain('admin account');
  });

  it('reconcile-storage button POSTs and renders summary', () => {
    const btn = fixture.nativeElement.querySelector('[data-testid="admin-reconcile-storage-button"]') as HTMLButtonElement;
    btn.click();
    const req = httpMock.expectOne(`${environment.apiUrl}/api/photos/admin/reconcile-storage`);
    expect(req.request.method).toBe('POST');
    req.flush({ scanned: 200, reconciled: 198, errors: 2, elapsedMs: 456 });
    fixture.detectChanges();

    const report = fixture.nativeElement.querySelector('[data-testid="admin-reconcile-report"]');
    expect(report).toBeTruthy();
    expect(report.textContent).toContain('200');
    expect(report.textContent).toContain('456');
  });
});
