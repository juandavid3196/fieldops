import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

import { NAV_GROUPS } from '../app-shell/app-shell.nav';
import { ShellSidebar } from './shell-sidebar';

@Component({ template: '' })
class Stub {}

describe('Shell sidebar Invoices group', () => {
  // [url, current child (null = none), group highlighted]
  const cases: [string, string | null, boolean][] = [
    ['/invoices', 'All invoices', true],
    ['/invoices?tab=invoices', 'All invoices', true],
    ['/invoices?status=bogus', 'All invoices', true],
    ['/invoices?status=draft', 'Drafts', true],
    ['/invoices?status=sent', 'Sent', true],
    ['/invoices?status=overdue', 'Overdue', true],
    ['/invoices?tab=payments', 'Payments', true],
    ['/invoices/review', 'Needs review', true],
    ['/invoices/review?workOrderId=wo-1', 'Needs review', true],
    ['/invoices/inv-1', null, true],
    ['/customers', null, false],
  ];

  it.each(cases)(
    'marks the child matching path and query as current: %s (BR-27, AC-14)',
    async (url, current, groupActive) => {
      TestBed.configureTestingModule({
        providers: [provideRouter([{ path: '**', component: Stub }])],
      });
      await TestBed.inject(Router).navigateByUrl(url);
      const fixture = TestBed.createComponent(ShellSidebar);
      fixture.componentRef.setInput('groups', NAV_GROUPS);
      fixture.detectChanges();
      const root = fixture.nativeElement as HTMLElement;
      const group = root.querySelector<HTMLButtonElement>('.sidebar__link--parent')!;
      expect(group.classList.contains('sidebar__link--active')).toBe(groupActive);
      if (!groupActive) {
        group.click();
        fixture.detectChanges();
      }
      const children = Array.from(
        root.querySelectorAll<HTMLAnchorElement>(`#${group.getAttribute('aria-controls')} a`),
      );
      expect(children.map((link) => [link.textContent?.trim(), link.getAttribute('href')])).toEqual(
        [
          ['All invoices', '/invoices'],
          ['Needs review', '/invoices/review'],
          ['Drafts', '/invoices?status=draft'],
          ['Sent', '/invoices?status=sent'],
          ['Payments', '/invoices?tab=payments'],
          ['Overdue', '/invoices?status=overdue'],
        ],
      );
      expect(
        children
          .filter((link) => link.getAttribute('aria-current') === 'page')
          .map((link) => link.textContent?.trim()),
      ).toEqual(current === null ? [] : [current]);
    },
  );
});
