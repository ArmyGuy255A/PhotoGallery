import { Component } from '@angular/core';
import {NavbarComponent} from '../components/navbar/navbar.component';
import {SidenavComponent} from '../components/sidenav/sidenav.component';
import {CartDrawerComponent} from '../components/cart/cart-drawer.component';

/**
 * Authenticated app shell — wraps every route guarded by `authGuard`
 * with the global navbar and sidenav.
 *
 * The `<router-outlet/>` for child feature routes lives inside
 * `<app-sidenav>`'s `<mat-sidenav-content>` slot — see
 * `sidenav.component.html`. BaseLayoutComponent intentionally does not
 * declare its own outlet to avoid double-nesting routed content.
 */
@Component({
  selector: 'app-base-layout',
  imports: [
    NavbarComponent,
    SidenavComponent,
    CartDrawerComponent
  ],
  templateUrl: './base-layout.component.html',
  styleUrl: './base-layout.component.scss'
})
export class BaseLayoutComponent {

}
