import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AdminSettingsComponent } from './admin-settings.component';

describe('AdminSettingsComponent', () => {
  let fixture: ComponentFixture<AdminSettingsComponent>;
  let component: AdminSettingsComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AdminSettingsComponent]
    }).compileComponents();

    fixture = TestBed.createComponent(AdminSettingsComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

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

  it('renders the coming-soon copy and hint', () => {
    const host: HTMLElement = fixture.nativeElement;
    const coming = host.querySelector('.coming-soon');
    expect(coming).toBeTruthy();
    expect(coming!.textContent).toContain('Admin configuration is coming soon.');
    expect(coming!.textContent).toContain('Site-wide settings, user management, and role assignment will live here.');
  });
});
