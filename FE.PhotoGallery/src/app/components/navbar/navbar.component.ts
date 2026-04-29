import { Component, inject } from '@angular/core';
import {GlobalStateService} from '../../services/global-state.service';
import {MatIconModule} from '@angular/material/icon';
import {MatButtonModule} from '@angular/material/button';
import {MatToolbarModule} from '@angular/material/toolbar';
import {MatFormFieldModule} from '@angular/material/form-field';
import {MatInput} from '@angular/material/input';
import {CommonModule} from '@angular/common';
import {ThemeComponent} from '../theme/theme.component';

@Component({
  selector: 'app-navbar',
  imports: [
    CommonModule, MatToolbarModule, MatButtonModule, MatIconModule, MatFormFieldModule, MatInput, ThemeComponent,
  ],
  templateUrl: './navbar.component.html',
  styleUrl: './navbar.component.scss'
})
export class NavbarComponent {

  globalStateService: GlobalStateService = inject(GlobalStateService);
  constructor() {
  }

  toggleSidebar() {
    this.globalStateService.toggleSidebar();
  }
}
