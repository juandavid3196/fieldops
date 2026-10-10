import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

import { API_CONFIG } from '../../core/config/api.config';
import { authInterceptor } from '../../core/interceptors/auth.interceptor';
import { errorInterceptor } from '../../core/interceptors/error.interceptor';
import { PortalSession } from '../../core/models/portal-session.model';
import { PortalSessionService } from '../../core/services/portal-session.service';
import { PortalShell } from './portal-shell';
import { bellCountText } from './portal-shell.nav';

const API = 'http://api.test';

const ACCOUNT_1 = { contactId: 'c-1', customerName: 'Sofia Martinez', organizationName: 'Northstar' };
const ACCOUNT_2 = { contactId: 'c-2', customerName: 'Sofia Martinez', organizationName: 'Acme' };

const session = (accounts: PortalSession['accounts']): PortalSession => ({
  user: { firstName: 'Sofia', lastName: 'Martinez', email: 'sofia@example.com' },
  account: { ...accounts[0], hasLogo: false },
  accounts,
});

describe('PortalShell', () => {
  let httpTesting: HttpTestingController;
  let host: HTMLElement;

  async function render(current: PortalSession, unreadCount: number) {
    TestBed.configureTestingModule({
      providers: [
        { provide: API_CONFIG, useValue: { baseUrl: API } },
        provideRouter([{ path: '**', children: [] }]),
        provideHttpClient(withInterceptors([authInterceptor, errorInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    TestBed.inject(PortalSessionService).setSession(current);
    httpTesting = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(PortalShell);
    host = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();
    httpTesting.expectOne(`${API}/portal/updates`).flush({ items: [], unreadCount });
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture;
  }

  const text = (selector: string): string =>
    (host.querySelector(selector)?.textContent ?? '').replace(/\s+/g, ' ').trim();
  const click = (selector: string): void => host.querySelector<HTMLElement>(selector)?.click();

  afterEach(() => httpTesting.verify());

  it.each([
    [0, null, 'Updates'],
    [3, '3', 'Updates, 3 unread'],
    [9, '9', 'Updates, 9 unread'],
    [12, '9+', 'Updates, 12 unread'],
  ])('the bell with %i unread shows %s and labels it for assistive technology', async (count, badge, label) => {
    expect(bellCountText(count)).toBe(badge);
    await render(session([ACCOUNT_1]), count);
    expect(host.querySelector('.pshell__bell')?.getAttribute('aria-label')).toBe(label);
    expect(host.querySelector('.pshell__bell')?.getAttribute('href')).toBe('/portal/updates');
    expect(text('.pshell__badge')).toBe(badge ?? '');
  });

  it('shows Switch account only with two or more accounts, switches and signs out', async () => {
    const single = await render(session([ACCOUNT_1]), 0);
    expect(text('.pshell__brand')).toContain('Northstar');
    expect(host.querySelectorAll('.pshell__nav a')).toHaveLength(5);
    click('.pshell__user-button');
    single.detectChanges();
    expect(text('.pshell__menu')).not.toContain('Switch account');
    expect(text('.pshell__menu')).toContain('Sign out');
    single.destroy();
    TestBed.resetTestingModule();

    const many = await render(session([ACCOUNT_1, ACCOUNT_2]), 0);
    click('.pshell__user-button');
    many.detectChanges();
    expect(text('.pshell__menu')).toContain('Switch account');
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl');

    // Switching reissues the session and returns to Home (the selector resets there).
    const items = Array.from(host.querySelectorAll<HTMLButtonElement>('.pshell__menu-item'));
    items.find((item) => item.textContent?.includes('Acme'))?.click();
    const switched = httpTesting.expectOne(`${API}/portal/sessions/current/account`);
    expect(switched.request.body).toEqual({ contactId: 'c-2' });
    switched.flush({ ...session([ACCOUNT_2, ACCOUNT_1]) });
    await many.whenStable();
    expect(navigate).toHaveBeenCalledWith('/portal');
    httpTesting.expectOne(`${API}/portal/updates`).flush({ items: [], unreadCount: 0 });

    click('.pshell__user-button');
    many.detectChanges();
    Array.from(host.querySelectorAll<HTMLButtonElement>('.pshell__menu-item'))
      .find((item) => item.textContent?.includes('Sign out'))
      ?.click();
    httpTesting.expectOne(`${API}/portal/sessions/current`).flush(null, { status: 204, statusText: 'No Content' });
    await many.whenStable();
    expect(navigate).toHaveBeenCalledWith('/portal/sign-in', { replaceUrl: true });
    expect(TestBed.inject(PortalSessionService).session()).toBeNull();
  });
});
