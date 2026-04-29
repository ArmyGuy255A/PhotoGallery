import {Component, inject} from '@angular/core';
import {GlobalStateService} from '../../services/global-state.service';
import {MatSlideToggle} from '@angular/material/slide-toggle';
import {MatIconModule} from '@angular/material/icon';
import {MatButtonModule} from '@angular/material/button';

@Component({
  selector: 'app-theme',
  imports: [
    MatSlideToggle,
    MatIconModule,
    MatButtonModule
  ],
  templateUrl: './theme.component.html',
  styleUrl: './theme.component.scss'
})
export class ThemeComponent {

  tooltip = 'Toggle dark mode';
  currentTheme = 'light';
  iconLight = 'light_mode';
  iconDark = 'dark_mode';
  icon = this.iconDark;

  globalStateService: GlobalStateService = inject(GlobalStateService);

  constructor() {
    this.setIcon()
  }

  toggleTheme() {
    this.globalStateService.toggleTheme();
    this.setIcon();
  }

  setIcon() {
    if (this.globalStateService.darkMode) {
      this.currentTheme = 'dark';
      this.icon = this.iconLight;
    } else {
      this.currentTheme = 'light';
      this.icon = this.iconDark;
    }
  }

  isDarkModeEnabled(): string {
    return this.globalStateService.darkMode ? 'true' : 'false';
  }
}
