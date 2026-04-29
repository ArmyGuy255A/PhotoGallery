import { Component } from '@angular/core';
import {TestComponent} from '../components/test/test.component';

@Component({
  selector: 'app-home',
  imports: [
    TestComponent
  ],
  templateUrl: './home.component.html',
  styleUrl: './home.component.scss'
})
export class HomeComponent {

}
