import { Component, computed, inject, linkedSignal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { ButtonDirective } from 'primeng/button';
import { map } from 'rxjs';

import { HelpBreadcrumb } from '../../components/help-breadcrumb/help-breadcrumb';
import { HelpSupportCard } from '../../components/help-support-card/help-support-card';
import { HelpArticleRef } from '../../models/help.model';
import { injectHelpContext } from '../../utils/help-audience';
import { findArticle, neighbours, readingMinutes } from '../../utils/help-content';
import { HelpNotFound } from '../help-not-found/help-not-found';

/** `/help/:topicSlug/:articleSlug`: the single reusable article template (FR-09, FR-10). */
@Component({
  selector: 'app-help-article',
  imports: [ButtonDirective, HelpBreadcrumb, HelpNotFound, HelpSupportCard, RouterLink],
  templateUrl: './help-article.html',
  styleUrl: './help-article.scss',
})
export class HelpArticlePage {
  private readonly route = inject(ActivatedRoute);
  private readonly context = injectHelpContext();

  readonly base = this.context.base;

  private readonly slugs = toSignal(
    this.route.paramMap.pipe(
      map((params) => ({ topic: params.get('topicSlug'), article: params.get('articleSlug') })),
    ),
    { initialValue: { topic: null, article: null } },
  );

  /** `null` for an unknown topic, an unknown article or an article under another topic. */
  readonly ref = computed(() =>
    findArticle(this.slugs().topic, this.slugs().article, this.context.audience),
  );
  readonly minutes = computed(() => {
    const ref = this.ref();
    return ref === null ? 1 : readingMinutes(ref.article);
  });
  readonly neighbours = computed(() => {
    const ref = this.ref();
    return ref === null ? null : neighbours(ref.topic, ref.article);
  });
  readonly crumbs = computed(() => {
    const ref = this.ref();
    return [
      { label: 'Help', link: this.base },
      { label: ref?.topic.title ?? '', link: `${this.base}/${ref?.topic.slug}` },
      { label: ref?.article.title ?? '' },
    ];
  });

  /** In-memory only: it resets whenever another article opens (FR-10). */
  readonly answered = linkedSignal<HelpArticleRef | null, boolean>({
    source: this.ref,
    computation: () => false,
  });

  protected answer(): void {
    this.answered.set(true);
  }
}
