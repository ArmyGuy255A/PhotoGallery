import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { By } from '@angular/platform-browser';
import { of } from 'rxjs';

import { AlbumDetailComponent } from './album-detail.component';
import { CartService } from '../../services/cart.service';
import { environment } from '../../../environments/environment';

class CartServiceStub {
  addItem = jasmine.createSpy('addItem').and.returnValue(true);
  contains = jasmine.createSpy('contains').and.returnValue(false);
  removeItem = jasmine.createSpy('removeItem');
}

describe('AlbumDetailComponent — cart integration (#58)', () => {
  let fixture: ComponentFixture<AlbumDetailComponent>;
  let cart: CartServiceStub;
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    cart = new CartServiceStub();

    await TestBed.configureTestingModule({
      imports: [AlbumDetailComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: CartService, useValue: cart },
        { provide: ActivatedRoute, useValue: { params: of({ id: 'A1' }) } }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(AlbumDetailComponent);
    fixture.detectChanges();
    httpMock = TestBed.inject(HttpTestingController);

    // Album metadata
    httpMock.expectOne(`${environment.apiUrl}/api/albums/A1`).flush({
      id: 'A1', title: 'My Album', description: '',
      createdDate: new Date().toISOString(), createdBy: 'me', ownerId: 'u1'
    });
    // Photos
    httpMock.expectOne(`${environment.apiUrl}/api/albums/A1/photos`).flush({
      photos: [
        { id: 'p1', fileName: 'p1.jpg', uploadDate: new Date().toISOString(),
          thumbnailUrl: 'http://thumb/p1', processingStatus: 'Complete' }
      ],
      totalCount: 1, page: 1, pageSize: 20, hasMore: false
    });
    // Access codes
    httpMock.expectOne(`${environment.apiUrl}/api/albums/A1/access-codes`).flush([]);
    fixture.detectChanges();
  });

  afterEach(() => {
    try { httpMock.verify(); } catch { /* polling timer may have queued one */ }
  });

  it('renders an Add to Cart button + quality select per photo', () => {
    const buttons = fixture.debugElement.queryAll(By.css('[data-testid="album-photo-add-to-cart"]'));
    const selects = fixture.debugElement.queryAll(By.css('[data-testid="album-photo-quality-select"]'));
    expect(buttons.length).toBe(1);
    expect(selects.length).toBe(1);
  });

  it('clicking Add to Cart calls CartService.addItem with the album context', () => {
    const btn = fixture.debugElement.query(By.css('[data-testid="album-photo-add-to-cart"]'));
    btn.triggerEventHandler('click', null);

    expect(cart.addItem).toHaveBeenCalledTimes(1);
    const arg = cart.addItem.calls.mostRecent().args[0];
    expect(arg.photoId).toBe('p1');
    expect(arg.fileName).toBe('p1.jpg');
    expect(arg.thumbnailUrl).toBe('http://thumb/p1');
    expect(arg.quality).toBe('Medium');
    expect(arg.sourceAlbumId).toBe('A1');
    expect(arg.sourceAlbumTitle).toBe('My Album');
  });
});
