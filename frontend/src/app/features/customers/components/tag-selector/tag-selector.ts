import { Component, computed, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { InputText } from 'primeng/inputtext';

import { CustomerTag, MAX_TAGS } from '../../models/customer.model';

interface TagOption {
  readonly key: string;
  readonly label: string;
  readonly tag: CustomerTag | null;
  readonly createName: string | null;
  readonly selected: boolean;
  readonly disabled: boolean;
}

export const MAX_TAG_NAME_LENGTH = 40;

/**
 * Tag selector (BR-12): filter-as-you-type list of the organization tags with an inline
 * "Create "<text>"" option (Owner/Dispatcher), up to 10 selected tags shown as removable chips.
 * Presentational: the drawer owns the selection and the `POST /customer-tags` call.
 */
@Component({
  selector: 'app-tag-selector',
  imports: [FormsModule, InputText],
  templateUrl: './tag-selector.html',
  styleUrl: './tag-selector.scss',
})
export class TagSelector {
  readonly inputId = input.required<string>();
  readonly tags = input.required<readonly CustomerTag[]>();
  readonly selected = input.required<readonly CustomerTag[]>();
  readonly loading = input(false);
  readonly disabled = input(false);
  readonly canCreate = input(false);
  readonly creating = input(false);
  readonly invalid = input(false);
  readonly describedBy = input<string | null>(null);

  readonly toggled = output<CustomerTag>();
  readonly removed = output<CustomerTag>();
  readonly createRequested = output<string>();

  readonly query = signal('');
  readonly open = signal(false);
  readonly activeIndex = signal(-1);
  readonly maxTags = MAX_TAGS;

  readonly listId = computed(() => `${this.inputId()}-list`);
  readonly atLimit = computed(() => this.selected().length >= MAX_TAGS);
  private readonly selectedIds = computed(() => new Set(this.selected().map((tag) => tag.id)));

  readonly options = computed<readonly TagOption[]>(() => {
    const text = this.query().trim();
    const needle = text.toLowerCase();
    const chosen = this.selectedIds();
    const limit = this.atLimit();
    const found = this.tags()
      .filter((tag) => needle.length === 0 || tag.name.toLowerCase().includes(needle))
      .map((tag): TagOption => ({
        key: tag.id,
        label: tag.name,
        tag,
        createName: null,
        selected: chosen.has(tag.id),
        disabled: limit && !chosen.has(tag.id),
      }));
    const exact = this.tags().some((tag) => tag.name.toLowerCase() === needle);
    if (this.canCreate() && text.length > 0 && text.length <= MAX_TAG_NAME_LENGTH && !exact) {
      found.push({
        key: 'create',
        label: `Create "${text}"`,
        tag: null,
        createName: text,
        selected: false,
        disabled: limit || this.creating(),
      });
    }
    return found;
  });

  optionId(index: number): string {
    return `${this.inputId()}-option-${index}`;
  }

  onQuery(value: string): void {
    this.query.set(value);
    this.open.set(true);
    this.activeIndex.set(this.options().length > 0 ? 0 : -1);
  }

  show(): void {
    if (!this.disabled()) {
      this.open.set(true);
    }
  }

  onFocusOut(event: FocusEvent, host: HTMLElement): void {
    if (!host.contains(event.relatedTarget as Node | null)) {
      this.open.set(false);
    }
  }

  onKeydown(event: KeyboardEvent): void {
    const count = this.options().length;
    switch (event.key) {
      case 'ArrowDown':
      case 'ArrowUp': {
        event.preventDefault();
        this.open.set(true);
        if (count === 0) {
          return;
        }
        const step = event.key === 'ArrowDown' ? 1 : -1;
        this.activeIndex.set((this.activeIndex() + step + count) % count);
        return;
      }
      case 'Enter': {
        if (!this.open()) {
          return;
        }
        event.preventDefault();
        const option = this.options()[this.activeIndex()];
        if (option !== undefined) {
          this.choose(option);
        }
        return;
      }
      case 'Escape':
        if (this.open()) {
          event.preventDefault();
          event.stopPropagation();
          this.open.set(false);
        }
        return;
    }
  }

  choose(option: TagOption): void {
    if (option.disabled || this.disabled()) {
      return;
    }
    if (option.createName !== null) {
      this.createRequested.emit(option.createName);
      this.query.set('');
      return;
    }
    if (option.tag !== null) {
      this.toggled.emit(option.tag);
    }
  }
}
