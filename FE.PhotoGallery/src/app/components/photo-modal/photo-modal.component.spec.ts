import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';

import { PhotoModalComponent, ModalPhoto } from './photo-modal.component';

const PHOTOS: ModalPhoto[] = [
  { photoId: 'p1', fileName: 'one.jpg', displayUrl: 'http://x/one.jpg' },
  { photoId: 'p2', fileName: 'two.jpg', displayUrl: 'http://x/two.jpg' }
];

describe('PhotoModalComponent', () => {
  let fixture: ComponentFixture<PhotoModalComponent>;
  let component: PhotoModalComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [PhotoModalComponent]
    }).compileComponents();

    fixture = TestBed.createComponent(PhotoModalComponent);
    component = fixture.componentInstance;
    component.photos = PHOTOS;
    component.currentIndex = 0;
    component.isOpen = true;
    component.showCartButton = true;
  });

  describe('cart button branching by [isInCart]', () => {
    it('renders the green "+ Add to Cart" button when isInCart=false', () => {
      component.isInCart = false;
      fixture.detectChanges();

      const addBtn = fixture.debugElement.query(By.css('.cart-btn-add'));
      const removeBtn = fixture.debugElement.query(By.css('.cart-btn-remove'));
      expect(addBtn).withContext('Add button rendered').toBeTruthy();
      expect(removeBtn).withContext('Remove button NOT rendered').toBeFalsy();
      expect(addBtn.nativeElement.textContent.trim()).toContain('Add to Cart');
    });

    it('renders the red "− Remove from Cart" button when isInCart=true', () => {
      component.isInCart = true;
      fixture.detectChanges();

      const addBtn = fixture.debugElement.query(By.css('.cart-btn-add'));
      const removeBtn = fixture.debugElement.query(By.css('.cart-btn-remove'));
      expect(removeBtn).withContext('Remove button rendered').toBeTruthy();
      expect(addBtn).withContext('Add button NOT rendered').toBeFalsy();
      expect(removeBtn.nativeElement.textContent.trim()).toContain('Remove from Cart');
    });

    it('flips from Add to Remove when isInCart toggles (live update)', () => {
      component.isInCart = false;
      fixture.detectChanges();
      expect(fixture.debugElement.query(By.css('.cart-btn-add'))).toBeTruthy();

      component.isInCart = true;
      fixture.detectChanges();
      expect(fixture.debugElement.query(By.css('.cart-btn-add'))).toBeFalsy();
      expect(fixture.debugElement.query(By.css('.cart-btn-remove'))).toBeTruthy();
    });
  });

  describe('(cartAction) emission', () => {
    it('emits cartAction with the current photo when Add is clicked', () => {
      component.isInCart = false;
      fixture.detectChanges();

      let emitted: ModalPhoto | undefined;
      component.cartAction.subscribe(p => emitted = p);

      const addBtn = fixture.debugElement.query(By.css('.cart-btn-add'));
      addBtn.triggerEventHandler('click', new MouseEvent('click'));

      expect(emitted).toBeDefined();
      expect(emitted!.photoId).toBe('p1');
    });

    it('emits cartAction with the current photo when Remove is clicked', () => {
      component.isInCart = true;
      fixture.detectChanges();

      let emitted: ModalPhoto | undefined;
      component.cartAction.subscribe(p => emitted = p);

      const removeBtn = fixture.debugElement.query(By.css('.cart-btn-remove'));
      removeBtn.triggerEventHandler('click', new MouseEvent('click'));

      expect(emitted).toBeDefined();
      expect(emitted!.photoId).toBe('p1');
    });

    it('does not render any cart button when showCartButton=false', () => {
      component.showCartButton = false;
      component.isInCart = false;
      fixture.detectChanges();
      expect(fixture.debugElement.query(By.css('.cart-btn-add'))).toBeFalsy();
      expect(fixture.debugElement.query(By.css('.cart-btn-remove'))).toBeFalsy();
    });
  });
});
