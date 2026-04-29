import { Component } from '@angular/core';
import {RouterOutlet} from '@angular/router';
import {NavbarComponent} from '../components/navbar/navbar.component';
import {SidenavComponent} from '../components/sidenav/sidenav.component';
import {FooterComponent} from '../components/footer/footer.component';

@Component({
  selector: 'app-base-layout',
  imports: [
    RouterOutlet,
    NavbarComponent,
    SidenavComponent,
    FooterComponent
  ],
  templateUrl: './base-layout.component.html',
  styleUrl: './base-layout.component.scss'
})

export class BaseLayoutComponent {

}
