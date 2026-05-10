import {inject, Injectable, signal} from '@angular/core';
import {MediaMatcher} from '@angular/cdk/layout';

@Injectable({
  providedIn: 'root'
})
export class GlobalStateService {

  // Title
  title: string = 'Photo Gallery';

  // Logo
  logo: string = 'assets/images/logo.png';

  // Theme
  darkMode: boolean = false;

  // Sidebar
  sidebarOpen: boolean = false;
  sidebarToggled: boolean = false;

  // Available Modules
  modules: string[] = [
    'home',
    'gallery',
    'about',
    'contact'
  ];

  public readonly isMobile = signal(true);
  private readonly _mobileQuery: MediaQueryList;
  private readonly _mobileQueryListener: () => void;

  constructor() {
    // Issue #112: do NOT propagate `prefers-color-scheme: dark` to the
    // document — the app's visual design is light-mode only, and surfacing
    // the dark UA palette to native form controls clashes with the white
    // cards. `styles.scss` pins `:root { color-scheme: light }`. If we ever
    // ship a real dark theme this can become a feature flag instead.
    const prefersDark = window.matchMedia('(prefers-color-scheme: dark)').matches;
    this.darkMode = prefersDark;
    document.documentElement.style.colorScheme = 'light';

    // Check if the user is on a mobile device
    const media = inject(MediaMatcher);
    this._mobileQuery = media.matchMedia('(max-width: 600px)');
    this.isMobile.set(this._mobileQuery.matches);
    this._mobileQueryListener = () => this.isMobile.set(this._mobileQuery.matches);
    this._mobileQuery.addEventListener('change', this._mobileQueryListener);

  }

  toggleSidebar() {
    this.sidebarToggled = !this.sidebarToggled;
    this.sidebarOpen = this.sidebarToggled;
    this.debug();
  }

  openSidebar() {
    this.sidebarToggled = true;
    this.debug();

  }

  closeSidebar() {
    this.sidebarToggled = false;
    this.debug();

  }

  toggleTheme() {
    this.darkMode = !this.darkMode;

    const htmlEl = document.documentElement;

    if (this.darkMode) {
      htmlEl.style.colorScheme = 'dark';
    } else {
      htmlEl.style.colorScheme = 'light';
    }

    this.debug();
  }

  debug() {
    console.log(
      'GlobalStateService',
      'sidebarOpen:', this.sidebarOpen,
      'sidebarToggled:', this.sidebarToggled,
      'darkMode:', this.darkMode,
    )
  }

}
