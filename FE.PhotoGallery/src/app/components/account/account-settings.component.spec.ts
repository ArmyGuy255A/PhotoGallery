import { ComponentFixture, TestBed } from '@angular/core/testing';

import { AccountSettingsComponent } from './account-settings.component';

describe('AccountSettingsComponent', () => {
  let component: AccountSettingsComponent;
  let fixture: ComponentFixture<AccountSettingsComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AccountSettingsComponent]
    }).compileComponents();

    fixture = TestBed.createComponent(AccountSettingsComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('renders the Account Settings heading', () => {
    const heading = fixture.nativeElement.querySelector('h1') as HTMLElement;
    expect(heading).toBeTruthy();
    expect(heading.textContent).toContain('Account Settings');
  });

  it('renders the "Coming soon" copy directing users to their administrator', () => {
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Profile editing is coming soon.');
    expect(text).toContain('contact your administrator');
  });

  it('exposes data-testid="account-settings-page" on the page root for e2e targeting', () => {
    const root = fixture.nativeElement.querySelector('[data-testid="account-settings-page"]');
    expect(root).toBeTruthy();
  });
});
