import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { ActivatedRoute } from '@angular/router';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { NEVER } from 'rxjs';

import { AlbumDetailComponent } from './album-detail.component';

interface Photo {
  id: string;
  fileName: string;
  uploadDate: string;
  uploadedBy?: string;
  processingStatus?: string;
  thumbnailUrl?: string;
  mediumUrl?: string;
}

describe('AlbumDetailComponent', () => {
  let fixture: ComponentFixture<AlbumDetailComponent>;
  let component: AlbumDetailComponent;
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AlbumDetailComponent, HttpClientTestingModule],
      providers: [
        // params: NEVER so ngOnInit's subscribe never fires and we can set state by hand.
        { provide: ActivatedRoute, useValue: { params: NEVER } }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(AlbumDetailComponent);
    component = fixture.componentInstance;
    httpMock = TestBed.inject(HttpTestingController);

    // Bypass the loader so the photos grid renders.
    component.isLoading = false;
    component.album = {
      id: 'album-1',
      title: 'Test Album',
      description: 'desc',
      createdDate: '2026-01-01T00:00:00Z',
      createdBy: 'tester',
      ownerId: 'owner-1'
    } as any;
  });

  afterEach(() => {
    httpMock.verify();
  });

  function makePhoto(overrides: Partial<Photo> = {}): Photo {
    return {
      id: 'p1',
      fileName: 'photo.jpg',
      uploadDate: '2026-01-01T00:00:00Z',
      ...overrides
    };
  }

  describe('getShouldShowBadge()', () => {
    it('returns false for "Complete"', () => {
      expect(component.getShouldShowBadge(makePhoto({ processingStatus: 'Complete' }))).toBe(false);
    });

    it('returns false for null', () => {
      expect(component.getShouldShowBadge(makePhoto({ processingStatus: null as any }))).toBe(false);
    });

    it('returns false for undefined', () => {
      expect(component.getShouldShowBadge(makePhoto({ processingStatus: undefined }))).toBe(false);
    });

    it('returns false for empty string', () => {
      expect(component.getShouldShowBadge(makePhoto({ processingStatus: '' }))).toBe(false);
    });

    it('returns true for "Processing"', () => {
      expect(component.getShouldShowBadge(makePhoto({ processingStatus: 'Processing' }))).toBe(true);
    });

    it('returns true for "Failed"', () => {
      expect(component.getShouldShowBadge(makePhoto({ processingStatus: 'Failed' }))).toBe(true);
    });
  });

  describe('photo-status-badge DOM rendering', () => {
    function renderWithStatus(processingStatus: string | null | undefined) {
      component.photos = [makePhoto({ id: 'p1', processingStatus: processingStatus as any })];
      fixture.detectChanges();
    }

    function badgeEl() {
      return fixture.debugElement.query(By.css('[data-testid="photo-status-badge"]'));
    }

    it('renders the badge for "Processing"', () => {
      renderWithStatus('Processing');
      expect(badgeEl()).withContext('badge present for Processing').toBeTruthy();
    });

    it('renders the badge for "Failed"', () => {
      renderWithStatus('Failed');
      expect(badgeEl()).withContext('badge present for Failed').toBeTruthy();
    });

    it('does NOT render the badge for "Complete"', () => {
      renderWithStatus('Complete');
      expect(badgeEl()).withContext('badge hidden for Complete').toBeFalsy();
    });

    it('does NOT render the badge for null', () => {
      renderWithStatus(null);
      expect(badgeEl()).withContext('badge hidden for null').toBeFalsy();
    });

    it('does NOT render the badge for undefined', () => {
      renderWithStatus(undefined);
      expect(badgeEl()).withContext('badge hidden for undefined').toBeFalsy();
    });

    it('does NOT render the badge for empty string', () => {
      renderWithStatus('');
      expect(badgeEl()).withContext('badge hidden for empty string').toBeFalsy();
    });
  });
});
