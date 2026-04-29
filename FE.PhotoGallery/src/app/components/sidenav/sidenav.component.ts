import {Component, inject, signal, WritableSignal} from '@angular/core';
import {MatListModule} from '@angular/material/list';
import {MatSidenavModule} from '@angular/material/sidenav';
import {MatIconModule} from '@angular/material/icon';
import {MatButtonModule} from '@angular/material/button';
import {CommonModule} from '@angular/common';
import {GlobalStateService} from '../../services/global-state.service';
import {RouterOutlet} from '@angular/router';

@Component({
  selector: 'app-sidenav',
  imports: [CommonModule, MatButtonModule, MatIconModule, MatSidenavModule, MatListModule, RouterOutlet],
  templateUrl: './sidenav.component.html',
  styleUrl: './sidenav.component.scss'
})
export class SidenavComponent {
  globalStateService: GlobalStateService = inject(GlobalStateService);

  constructor() {
  }

  isMobile() : WritableSignal<boolean> {
    return this.globalStateService.isMobile;
  }

  close() {
    // Logic to close the sidenav
  }

  toggle() {
    // Logic to toggle the sidenav
  }

  open() {
    // Logic to open the sidenav
  }
}
