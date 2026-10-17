import { Component, input, model } from '@angular/core';
import { IconField } from 'primeng/iconfield';
import { InputIcon } from 'primeng/inputicon';
import { InputText } from 'primeng/inputtext';

/** Search box with an accessible name; the page owns the query. */
@Component({
  selector: 'app-help-search-field',
  imports: [IconField, InputIcon, InputText],
  templateUrl: './help-search-field.html',
  styleUrl: './help-search-field.scss',
})
export class HelpSearchField {
  readonly query = model('');
  readonly placeholder = input.required<string>();
  readonly inputId = input.required<string>();

  protected onInput(event: Event): void {
    this.query.set((event.target as HTMLInputElement).value);
  }
}
