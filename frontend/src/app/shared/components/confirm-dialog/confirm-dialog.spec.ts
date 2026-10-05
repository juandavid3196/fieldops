import { Component, inject, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Confirmation, ConfirmationService } from 'primeng/api';

import {
  DiscardChangesDialog,
  discardChangesConfirmation,
} from '../discard-changes-dialog/discard-changes-dialog';
import { ConfirmDialog } from './confirm-dialog';

@Component({
  imports: [ConfirmDialog, DiscardChangesDialog],
  providers: [ConfirmationService],
  template: '<app-confirm-dialog /><app-discard-changes-dialog />',
})
class Host {
  readonly confirmation = inject(ConfirmationService);
}

@Component({
  imports: [ConfirmDialog],
  providers: [ConfirmationService],
  template: `<app-confirm-dialog
    checkboxLabel="Notify customer by email"
    checkboxHelper="This customer has no email address."
    [checkboxDisabled]="disabled()"
    [(checkboxChecked)]="notify"
  />`,
})
class CheckboxHost {
  readonly confirmation = inject(ConfirmationService);
  readonly notify = signal(false);
  readonly disabled = signal(false);
}

describe('ConfirmDialog', () => {
  const dialog = (): HTMLElement | null =>
    document.querySelector('[role="alertdialog"][aria-modal]');
  const buttons = (): HTMLButtonElement[] =>
    Array.from(
      dialog()?.querySelectorAll<HTMLButtonElement>('.confirm-dialog__footer button') ?? [],
    );
  const dialogButton = (label: string): HTMLButtonElement | undefined =>
    Array.from(dialog()?.querySelectorAll('button') ?? []).find(
      (b) => (b.getAttribute('aria-label') ?? b.textContent?.trim()) === label,
    );

  async function setup(): Promise<{ open: (c: Confirmation) => Promise<void> }> {
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    return {
      open: async (confirmation) => {
        fixture.componentInstance.confirmation.confirm(confirmation);
        await fixture.whenStable();
      },
    };
  }

  it('renders a generic confirmation with its labels, danger tone and accept focus', async () => {
    const { open } = await setup();
    const results: string[] = [];

    await open({
      header: 'Archive Acme?',
      message: 'Archived customers are hidden from active lists.',
      acceptButtonProps: { label: 'Archive', severity: 'danger' },
      rejectButtonProps: { label: 'Cancel', severity: 'secondary', outlined: true },
      accept: () => results.push('archive'),
      reject: () => results.push('cancel'),
    });

    const root = dialog()!;
    expect(document.getElementById(root.getAttribute('aria-labelledby')!)?.textContent).toBe(
      'Archive Acme?',
    );
    expect(root.querySelector('.confirm-dialog__icon--danger')).not.toBeNull();
    expect(root.querySelector('.confirm-dialog__note')).toBeNull();
    expect(buttons().map((b) => b.textContent?.trim())).toEqual(['Cancel', 'Archive']);
    await vi.waitFor(() => expect(document.activeElement).toBe(dialogButton('Archive')));

    dialogButton('Archive')!.click();
    expect(results).toEqual(['archive']);
  });

  it('renders the discard preset with its note and Keep editing focus; close and Keep editing reject', async () => {
    const { open } = await setup();
    const results: string[] = [];
    const discard = discardChangesConfirmation({
      subject: 'this customer',
      accept: () => results.push('discard'),
      reject: () => results.push('keep'),
    });

    await open(discard);
    const root = dialog()!;
    expect(document.getElementById(root.getAttribute('aria-labelledby')!)?.textContent).toBe(
      'Discard unsaved changes?',
    );
    expect(document.getElementById(root.getAttribute('aria-describedby')!)?.textContent).toBe(
      'You have unsaved changes in this customer. If you leave now, those changes will be lost.',
    );
    expect(root.querySelector('.confirm-dialog__note')?.textContent?.trim()).toBe(
      "This action can't be undone.",
    );
    expect(buttons().map((b) => b.textContent?.trim())).toEqual([
      'Discard changes',
      'Keep editing',
    ]);
    await vi.waitFor(() => expect(document.activeElement).toBe(dialogButton('Keep editing')));

    dialogButton('Close')!.click();
    await open(discard);
    dialogButton('Keep editing')!.click();
    await open(discard);
    dialogButton('Discard changes')!.click();

    expect(results).toEqual(['keep', 'keep', 'discard']);
  });

  it('shows an optional checkbox with its helper, off by default, and reports changes and disabled state', async () => {
    const fixture = TestBed.createComponent(CheckboxHost);
    await fixture.whenStable();
    const host = fixture.componentInstance;
    const results: string[] = [];
    host.confirmation.confirm({
      header: 'Cancel assessment?',
      message: 'The request returns to Needs review.',
      accept: () => results.push(`accept:${host.notify()}`),
    });
    await fixture.whenStable();

    const checkbox = (): HTMLInputElement =>
      dialog()!.querySelector<HTMLInputElement>('input[type="checkbox"]')!;
    expect(dialog()?.querySelector('.confirm-dialog__option label')?.textContent?.trim()).toBe(
      'Notify customer by email',
    );
    expect(dialog()?.querySelector('.confirm-dialog__helper')?.textContent).toContain(
      'no email address',
    );
    expect(checkbox().checked).toBe(false);
    expect(checkbox().disabled).toBe(false);

    checkbox().click();
    await fixture.whenStable();
    expect(host.notify()).toBe(true);
    expect(checkbox().checked).toBe(true);

    host.disabled.set(true);
    await fixture.whenStable();
    await vi.waitFor(() => expect(checkbox().disabled).toBe(true));

    dialogButton('Confirm')!.click();
    expect(results).toEqual(['accept:true']);
  });
});
