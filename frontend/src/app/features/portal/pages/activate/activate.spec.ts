import { Location } from '@angular/common';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';

import { API_CONFIG } from '../../../../core/config/api.config';
import { errorInterceptor } from '../../../../core/interceptors/error.interceptor';
import { PortalSessionService } from '../../../../core/services/portal-session.service';
import { PortalActivate } from './activate';

const API = 'http://api.test';
const PASSWORD = 'correct horse battery';
const SESSION = {
  user: { firstName: 'Sofia', lastName: 'Rivera', email: 'sofia@example.com' },
  account: {
    contactId: 'c-1',
    customerName: 'Sofia',
    organizationName: 'Northstar',
    hasLogo: false,
  },
  accounts: [{ contactId: 'c-1', customerName: 'Sofia', organizationName: 'Northstar' }],
};
const DETAILS = {
  organizationName: 'Northstar',
  firstName: 'Sofia',
  email: 'sofia@example.com',
  accountExists: false,
  lastNameRequired: true,
};

describe('PortalActivate (BR-12, BR-13)', () => {
  let fixture: ComponentFixture<PortalActivate>;
  let httpTesting: HttpTestingController;
  let replaceState: ReturnType<typeof vi.spyOn>;

  function setup(fragment: string | null): void {
    sessionStorage.clear();
    TestBed.configureTestingModule({
      providers: [
        { provide: API_CONFIG, useValue: { baseUrl: API } },
        provideRouter([]),
        provideHttpClient(withInterceptors([errorInterceptor])),
        provideHttpClientTesting(),
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { fragment, queryParamMap: convertToParamMap({}) } },
        },
      ],
    });
    httpTesting = TestBed.inject(HttpTestingController);
    replaceState = vi.spyOn(TestBed.inject(Location), 'replaceState');
    fixture = TestBed.createComponent(PortalActivate);
    fixture.detectChanges();
  }

  const host = () => fixture.nativeElement as HTMLElement;
  const text = () => (host().textContent ?? '').replace(/\s+/g, ' ').trim();
  const settle = async (): Promise<void> => {
    await fixture.whenStable();
    fixture.detectChanges();
  };
  async function fill(id: string, value: string): Promise<void> {
    const field = host().querySelector<HTMLInputElement>(`#${id}`)!;
    field.value = value;
    field.dispatchEvent(new Event('input'));
    await settle();
  }
  async function submit(): Promise<void> {
    host().querySelector('form')!.dispatchEvent(new Event('submit'));
    await settle();
  }

  afterEach(() => {
    httpTesting.verify();
    sessionStorage.clear();
  });

  it('captures the fragment token, requires a last name and sends the token only in POST bodies', async () => {
    setup('token=abc123');
    expect(replaceState).toHaveBeenCalledWith('/portal/activate');
    const validate = httpTesting.expectOne(`${API}/portal/invitations/validate`);
    expect(validate.request.url).not.toContain('abc123');
    expect(validate.request.body).toEqual({ token: 'abc123' });
    validate.flush(DETAILS);
    await settle();

    // Omitting the last name is blocked before any request.
    await fill('portal-activate-password', PASSWORD);
    await fill('portal-activate-confirm', PASSWORD);
    await submit();
    expect(text()).toContain('Enter your last name.');
    httpTesting.expectNone(`${API}/portal/invitations/accept`);

    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    await fill('portal-last-name', '  Rivera ');
    await submit();
    const accept = httpTesting.expectOne(`${API}/portal/invitations/accept`);
    expect(accept.request.body).toEqual({
      token: 'abc123',
      password: PASSWORD,
      lastName: 'Rivera',
      preferredLanguage: 'en',
    });
    accept.flush(SESSION);
    await settle();
    expect(TestBed.inject(PortalSessionService).session()?.account.contactId).toBe('c-1');
    expect(sessionStorage.getItem('fieldops.portal-invitation-token')).toBeNull();
    expect(navigate).toHaveBeenCalledWith('/portal', { replaceUrl: true });
  });

  it('shows the unavailable message with or without a known organization and the mapped accept errors', async () => {
    // Unknown organization: the validation itself returns 410.
    setup('token=gone');
    httpTesting
      .expectOne(`${API}/portal/invitations/validate`)
      .flush({ code: 'portal_invitation_unavailable' }, { status: 410, statusText: 'Gone' });
    await settle();
    expect(text()).toContain("This invitation isn't available. Ask the company to send a new one.");
    fixture.destroy();
    TestBed.resetTestingModule();

    // Known organization: a 409 on accept and then a 410 name the company or the organization.
    setup('token=abc');
    httpTesting
      .expectOne(`${API}/portal/invitations/validate`)
      .flush({ ...DETAILS, lastNameRequired: false });
    await settle();
    await fill('portal-activate-password', PASSWORD);
    await fill('portal-activate-confirm', PASSWORD);
    await submit();
    const conflict = httpTesting.expectOne(`${API}/portal/invitations/accept`);
    expect(conflict.request.body).toEqual({
      token: 'abc',
      password: PASSWORD,
      preferredLanguage: 'en',
    });
    conflict.flush(
      { code: 'portal_invitation_ineligible' },
      { status: 409, statusText: 'Conflict' },
    );
    await settle();
    expect(text()).toContain("This account can't be linked. Contact the company.");

    await submit();
    httpTesting
      .expectOne(`${API}/portal/invitations/accept`)
      .flush({ code: 'portal_invitation_unavailable' }, { status: 410, statusText: 'Gone' });
    await settle();
    expect(text()).toContain('Ask Northstar to send a new one.');
  });
});
