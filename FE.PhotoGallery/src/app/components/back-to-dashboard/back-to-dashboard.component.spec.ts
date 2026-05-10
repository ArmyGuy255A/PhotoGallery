import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { By } from '@angular/platform-browser';

import { BackToDashboardComponent } from './back-to-dashboard.component';

describe('BackToDashboardComponent', () => {
  let fixture: ComponentFixture<BackToDashboardComponent>;
  let component: BackToDashboardComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [BackToDashboardComponent],
      providers: [provideRouter([])],
    }).compileComponents();

    fixture = TestBed.createComponent(BackToDashboardComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('renders default label and points at /dashboard', () => {
    const link: HTMLAnchorElement = fixture.debugElement
      .query(By.css('a[data-testid="back-to-dashboard"]')).nativeElement;
    expect(link.textContent?.trim()).toBe('← Back to Dashboard');
    expect(link.getAttribute('href')).toBe('/dashboard');
  });

  it('honours custom label and routerLink inputs', () => {
    component.label = 'Back to Albums';
    component.routerLink = '/shared-albums';
    fixture.detectChanges();

    const link: HTMLAnchorElement = fixture.debugElement
      .query(By.css('a[data-testid="back-to-dashboard"]')).nativeElement;
    expect(link.textContent?.trim()).toBe('← Back to Albums');
    expect(link.getAttribute('href')).toBe('/shared-albums');
  });
});
