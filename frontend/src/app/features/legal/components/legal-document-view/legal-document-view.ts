import { Component, ElementRef, computed, inject, input, signal } from '@angular/core';
import { ButtonDirective } from 'primeng/button';
import { TranslocoPipe } from '@jsverse/transloco';

import { LanguageService } from '../../../../core/i18n/language.service';
import { LEGAL_EFFECTIVE_DATE, legalMailto } from '../../data/legal-placeholders';
import { LegalDocument, LegalListItem } from '../../models/legal.model';

/** Distance from the viewport top at which a section counts as the one being read. */
const ACTIVE_OFFSET_PX = 120;

/** Hero, "On this page" navigation and numbered sections of a public legal page. */
@Component({
  selector: 'app-legal-document-view',
  imports: [TranslocoPipe, ButtonDirective],
  templateUrl: './legal-document-view.html',
  styleUrl: './legal-document-view.scss',
  host: { '(window:scroll)': 'syncActive()' },
})
export class LegalDocumentView {
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  private readonly language = inject(LanguageService);

  readonly content = input.required<LegalDocument>();
  /** BR-17: the Spanish pages carry a pending-review note; English shows none. */
  protected readonly showReviewNote = computed(() => this.language.lang() === 'es');

  protected get lastUpdated(): string {
    return LEGAL_EFFECTIVE_DATE();
  }
  protected readonly mailto = computed(() => legalMailto(this.content().contact.subject));
  private readonly scrolledTo = signal<string | null>(null);
  protected readonly activeId = computed(
    () => this.scrolledTo() ?? this.content().sections[0]?.id ?? null,
  );

  protected isTerm(item: LegalListItem): item is { term: string; text: string } {
    return typeof item !== 'string';
  }

  protected goTo(event: Event, id: string): void {
    event.preventDefault();
    this.sectionElements()
      .find((section) => section.id === id)
      ?.scrollIntoView({ behavior: 'smooth', block: 'start' });
    this.scrolledTo.set(id);
  }

  protected syncActive(): void {
    const sections = this.sectionElements();
    if (sections.length === 0) {
      return;
    }
    const root = document.documentElement;
    // At the bottom the last sections can never reach the offset; the last one is then current.
    if (window.innerHeight + window.scrollY >= root.scrollHeight - 2) {
      this.scrolledTo.set(sections[sections.length - 1].id);
      return;
    }
    let current = sections[0].id;
    for (const section of sections) {
      if (section.getBoundingClientRect().top <= ACTIVE_OFFSET_PX) {
        current = section.id;
      }
    }
    this.scrolledTo.set(current);
  }

  private sectionElements(): HTMLElement[] {
    return Array.from(this.host.nativeElement.querySelectorAll<HTMLElement>('.doc__section'));
  }
}
