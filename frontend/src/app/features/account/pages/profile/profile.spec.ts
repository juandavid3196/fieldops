import { HttpEventType, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { API_CONFIG } from '../../../../core/config/api.config';
import { authInterceptor } from '../../../../core/interceptors/auth.interceptor';
import { errorInterceptor } from '../../../../core/interceptors/error.interceptor';
import { Session } from '../../../../core/models/session.model';
import { SessionService } from '../../../../core/services/session.service';
import { Profile } from './profile';

const API = 'http://api.test';
const PROFILE_URL = `${API}/account/profile`;
const PHOTO_URL = `${API}/account/photo`;
const SESSION_URL = `${API}/sessions/current`;

const SESSION: Session = {
  user: {
    id: 'u-1',
    firstName: 'Ana',
    lastName: 'Lopez',
    email: 'ana@example.com',
    photoUpdatedAt: null,
  },
  organization: { id: 'o-1', name: 'Acme' },
  role: { code: 'owner', name: 'Owner' },
};

const PROFILE = {
  firstName: 'Ana',
  lastName: 'Lopez',
  email: 'ana@example.com',
  phone: '+1 555 123 4567',
  organization: { name: 'Acme' },
  role: { name: 'Owner' },
  photo: null,
};

const PNG_BYTES = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 0, 0];

describe('Profile', () => {
  let harness: RouterTestingHarness;
  let httpTesting: HttpTestingController;
  let sessionService: SessionService;

  const host = () => harness.routeNativeElement as HTMLElement;
  const stable = () => harness.fixture.whenStable();
  const input = (id: string) => host().querySelector<HTMLInputElement>(`#profile-${id}`)!;
  const button = (label: string) =>
    Array.from(host().querySelectorAll<HTMLButtonElement>('button')).find(
      (candidate) => candidate.textContent?.trim() === label,
    );
  const text = () => host().textContent?.replace(/\s+/g, ' ') ?? '';
  const type = (id: string, value: string): void => {
    input(id).value = value;
    input(id).dispatchEvent(new Event('input'));
  };

  async function setup(): Promise<void> {
    TestBed.configureTestingModule({
      providers: [
        { provide: API_CONFIG, useValue: { baseUrl: API } },
        provideRouter([{ path: '', component: Profile }]),
        provideHttpClient(withInterceptors([authInterceptor, errorInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    httpTesting = TestBed.inject(HttpTestingController);
    sessionService = TestBed.inject(SessionService);
    sessionService.setSession(SESSION);
    harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/', Profile);
  }

  afterEach(() => httpTesting.verify());

  it('shows a skeleton, a retryable load error and the read-only fields, then saves trimmed values and refreshes the session without sign-out (FR-06, FR-07, AC-06 to AC-09, AC-11, AC-12)', async () => {
    await setup();
    expect(host().querySelector('[aria-busy="true"]')).not.toBeNull();
    expect(host().querySelector('input')).toBeNull();

    httpTesting.expectOne(PROFILE_URL).flush(null, { status: 500, statusText: 'Server Error' });
    await stable();
    expect(text()).toContain("We couldn't load your profile. Try again.");
    button('Retry')!.click();
    httpTesting.expectOne(PROFILE_URL).flush(PROFILE);
    await stable();

    expect(input('firstName').value).toBe('Ana');
    expect(input('phone').value).toBe('+1 555 123 4567');
    for (const id of ['email', 'organization', 'role']) {
      expect(input(id).readOnly).toBe(true);
      expect(input(id).disabled).toBe(false);
    }
    expect(input('email').value).toBe('ana@example.com');
    expect(text()).toContain('Your email address cannot be changed.');
    expect(text()).toContain('Your organization and role are managed by an administrator.');
    expect(text()).toContain('JPG or PNG. Max 5 MB.');
    expect(button('Change photo')).toBeDefined();
    expect(button('Remove photo')).toBeUndefined();
    expect(host().querySelector('.profile__header app-user-avatar')?.textContent?.trim()).toBe(
      'AL',
    );

    // Client validation: table messages under the fields, no request.
    type('firstName', '   ');
    type('lastName', 'x'.repeat(101));
    type('phone', 'abc');
    button('Save changes')!.click();
    await stable();
    expect(host().querySelector('#profile-firstName-error')?.textContent).toBe(
      'This field is required.',
    );
    expect(host().querySelector('#profile-lastName-error')?.textContent).toBe(
      'Use 100 characters or fewer.',
    );
    expect(host().querySelector('#profile-phone-error')?.textContent).toBe(
      'Enter a valid phone number.',
    );
    expect(input('firstName').getAttribute('aria-describedby')).toBe('profile-firstName-error');

    // Cancel restores the last saved values and clears the errors without a request.
    button('Cancel')!.click();
    await stable();
    expect(input('firstName').value).toBe('Ana');
    expect(host().querySelector('.profile__error')).toBeNull();

    // A failed save keeps the edits and shows the generic message.
    type('firstName', ' Anna ');
    button('Save changes')!.click();
    httpTesting
      .expectOne({ method: 'PUT', url: PROFILE_URL })
      .flush(null, { status: 500, statusText: 'x' });
    await stable();
    expect(text()).toContain("We couldn't save your changes. Try again.");
    expect(input('firstName').value).toBe(' Anna ');

    // Success: trimmed body, empty phone sent as null, one request while saving, then pristine.
    type('lastName', 'Lopez ');
    type('phone', '');
    button('Save changes')!.click();
    await stable();
    expect(button('Saving…')).toBeDefined();
    button('Saving…')!.click();
    const save = httpTesting.expectOne({ method: 'PUT', url: PROFILE_URL });
    expect(save.request.body).toEqual({ firstName: 'Anna', lastName: 'Lopez', phone: null });
    save.flush({ ...PROFILE, firstName: 'Anna', phone: null });
    await stable();
    // A failing reload keeps the session instead of signing out.
    httpTesting.expectOne(SESSION_URL).flush(null, { status: 500, statusText: 'x' });
    await stable();
    expect(sessionService.session()).not.toBeNull();
    expect(text()).toContain('Profile updated');
    expect(input('firstName').value).toBe('Anna');
    expect(input('phone').value).toBe('');
    expect(host().querySelector('.profile__error')).toBeNull();
    expect(harness.routeDebugElement!.componentInstance.canLeave()).toBe(true);

    // The reload that succeeds updates the top bar data.
    sessionService.refreshCurrent().subscribe();
    httpTesting
      .expectOne(SESSION_URL)
      .flush({ ...SESSION, user: { ...SESSION.user, firstName: 'Anna' } });
    expect(sessionService.session()?.user.firstName).toBe('Anna');
  }, 30_000);

  it('validates the photo before uploading, then uploads with progress, updates the avatar and removes it (FR-08, AC-13 to AC-15)', async () => {
    await setup();
    httpTesting.expectOne(PROFILE_URL).flush(PROFILE);
    await stable();

    const choose = async (file: File): Promise<void> => {
      const fileInput = host().querySelector<HTMLInputElement>('input[type="file"]')!;
      Object.defineProperty(fileInput, 'files', {
        value: { length: 1, item: () => file },
        configurable: true,
      });
      fileInput.dispatchEvent(new Event('change'));
      await new Promise((resolve) => setTimeout(resolve));
      await stable();
    };
    const big = new File([new Uint8Array(5_242_881)], 'big.png', { type: 'image/png' });
    const gif = new File([new Uint8Array([0x47, 0x49, 0x46, 0x38, 0x39, 0x61])], 'a.gif', {
      type: 'image/gif',
    });
    const fake = new File([new Uint8Array([1, 2, 3, 4, 5, 6, 7, 8])], 'fake.png', {
      type: 'image/png',
    });

    for (const [file, message] of [
      [big, 'Choose a file of 5 MB or smaller.'],
      [gif, 'Choose a JPG or PNG file.'],
      [fake, 'Choose a JPG or PNG file.'],
    ] as const) {
      await choose(file);
      expect(host().querySelector('#profile-photo-error')?.textContent).toBe(message);
    }
    httpTesting.expectNone(PHOTO_URL);

    await choose(new File([new Uint8Array(PNG_BYTES)], 'me.png', { type: 'image/png' }));
    const upload = httpTesting.expectOne({ method: 'PUT', url: PHOTO_URL });
    expect((upload.request.body as FormData).get('file')).toBeInstanceOf(File);
    upload.event({ type: HttpEventType.UploadProgress, loaded: 50, total: 100 });
    await stable();
    expect(host().querySelector('[role="progressbar"]')?.getAttribute('aria-valuenow')).toBe('50');
    upload.flush({ contentType: 'image/png', sizeBytes: 10, updatedAt: '2026-10-16T10:00:00Z' });
    await stable();

    expect(text()).toContain('Photo updated');
    expect(sessionService.session()?.user.photoUpdatedAt).toBe('2026-10-16T10:00:00Z');
    expect(
      host().querySelector<HTMLImageElement>('.profile__header img')?.getAttribute('src'),
    ).toBe(`${PHOTO_URL}?v=2026-10-16T10%3A00%3A00Z`);
    expect(host().querySelector('.profile__header img')?.getAttribute('alt')).toBe('');
    expect(button('Remove photo')).toBeDefined();
    expect(harness.routeDebugElement!.componentInstance.canLeave()).toBe(true);

    // An image that fails to load falls back to the initials.
    host().querySelector('.profile__header img')!.dispatchEvent(new Event('error'));
    await stable();
    expect(host().querySelector('.profile__header img')).toBeNull();
    expect(host().querySelector('.profile__header app-user-avatar')?.textContent?.trim()).toBe(
      'AL',
    );

    // Server-side file message is shown under the control; a 413 shows the generic photo message.
    await choose(new File([new Uint8Array(PNG_BYTES)], 'me.png', { type: 'image/png' }));
    httpTesting
      .expectOne({ method: 'PUT', url: PHOTO_URL })
      .flush(
        { errors: { file: ['Choose a JPG or PNG file.'] } },
        { status: 400, statusText: 'Bad' },
      );
    await stable();
    expect(host().querySelector('#profile-photo-error')?.textContent).toBe(
      'Choose a JPG or PNG file.',
    );
    await choose(new File([new Uint8Array(PNG_BYTES)], 'me.png', { type: 'image/png' }));
    httpTesting
      .expectOne({ method: 'PUT', url: PHOTO_URL })
      .flush(null, { status: 413, statusText: 'Large' });
    await stable();
    expect(host().querySelector('#profile-photo-error')?.textContent).toBe(
      "We couldn't update your photo. Try again.",
    );

    button('Remove photo')!.click();
    httpTesting
      .expectOne({ method: 'DELETE', url: PHOTO_URL })
      .flush(null, { status: 204, statusText: 'No Content' });
    await stable();
    expect(text()).toContain('Photo removed');
    expect(sessionService.session()?.user.photoUpdatedAt).toBeNull();
    expect(button('Remove photo')).toBeUndefined();
  }, 30_000);
});
