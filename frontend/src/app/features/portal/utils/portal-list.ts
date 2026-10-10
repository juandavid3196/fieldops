import { DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Observable, catchError, of } from 'rxjs';

import { PropertyOption } from '../components/portal-property-filter/portal-property-filter';
import { Page } from '../models/portal.model';
import { PortalPropertiesService } from '../services/portal-properties.service';
import { PortalResource, pageResource } from './portal-resource';

/**
 * Paginated, property-filterable list of a portal module (20 per page, server total). Create it in
 * a field initializer (it needs an injection context). The property filter is page state only.
 */
export class PortalList<T> {
  readonly resource: PortalResource<Page<T>> = pageResource<T>();
  readonly page = signal(1);
  /** Selected property id; `null` is All properties. */
  readonly propertyId = signal<string | null>(null);
  readonly properties = signal<readonly PropertyOption[]>([]);

  constructor(
    private readonly fetch: (page: number, propertyId: string | null) => Observable<Page<T>>,
    filterable = true,
  ) {
    if (filterable) {
      inject(PortalPropertiesService)
        .list()
        .pipe(
          catchError(() => of([] as readonly PropertyOption[])),
          takeUntilDestroyed(inject(DestroyRef)),
        )
        .subscribe((properties) => this.properties.set(properties));
    }
    this.reload();
  }

  reload(): void {
    this.resource.load(this.fetch(this.page(), this.propertyId()));
  }

  goTo(page: number): void {
    this.page.set(page);
    this.reload();
  }

  filter(propertyId: string | null): void {
    this.propertyId.set(propertyId);
    this.page.set(1);
    this.reload();
  }
}
