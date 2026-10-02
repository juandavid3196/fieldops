import { AvailabilityData, ServiceData } from './models/service-request.model';
import {
  addDays,
  todayInTimeZone,
  validateAvailability,
  validateContact,
  validateNewFile,
  validateProperty,
  validateService,
} from './service-request.validators';

const MB = 1024 * 1024;
const contact = {
  firstName: 'Sofia',
  lastName: 'Martinez',
  email: 'sofia@example.com',
  phone: '(512) 555-0147',
  prefersEmail: true,
  prefersSms: false,
};
const property = {
  propertyType: 'home' as const,
  addressLine1: '742 Maple Ave',
  addressLine2: '',
  city: 'Austin',
  state: 'TX',
  postalCode: '78704',
  accessInstructions: '',
};
const service: ServiceData = {
  categoryId: 'c1',
  serviceId: 's1',
  notSure: false,
  description: 'Leak',
  urgency: 'standard',
  hasActiveDamage: false,
};
const availability: AvailabilityData = {
  dateMode: 'asap',
  preferredDate: '',
  timeWindow: 'morning',
  schedulingNotes: '',
};

describe('service request validators', () => {
  it('reports step field errors under the spec field paths (AC-03, AC-05)', () => {
    expect(validateContact(contact)).toEqual({});
    expect(validateProperty(property)).toEqual({});
    expect(validateService(service)).toEqual({});

    const cases: [string, Record<string, string>, string[]][] = [
      [
        'empty contact',
        validateContact({ ...contact, firstName: ' ', email: '', phone: '' }),
        ['contact.firstName', 'contact.email', 'contact.phone'],
      ],
      ['bad email', validateContact({ ...contact, email: 'a@b' }), ['contact.email']],
      ['short phone', validateContact({ ...contact, phone: '555-0147' }), ['contact.phone']],
      [
        'no update method',
        validateContact({ ...contact, prefersEmail: false }),
        ['contact.prefersEmail'],
      ],
      [
        'bad state and zip',
        validateProperty({ ...property, state: 'ZZ', postalCode: '7870' }),
        ['property.state', 'property.postalCode'],
      ],
      [
        'too long line 1',
        validateProperty({ ...property, addressLine1: 'x'.repeat(181) }),
        ['property.addressLine1'],
      ],
      [
        'no category/service/description',
        validateService({ ...service, categoryId: '', serviceId: '', description: ' ' }),
        ['service.categoryId', 'service.serviceId', 'service.description'],
      ],
      // "I'm not sure" needs only category, description and urgency (AC-05).
      [
        'not sure without service',
        validateService({ ...service, serviceId: '', notSure: true }),
        [],
      ],
    ];
    for (const [name, errors, expected] of cases) {
      expect(Object.keys(errors), name).toEqual(expected);
    }
  });

  it('requires a date within today..+90 only for "Choose a date" (AC-07)', () => {
    const today = '2026-03-10';
    const cases: [Partial<AvailabilityData>, string[]][] = [
      [{ dateMode: 'asap' }, []],
      [{ dateMode: 'flexible', preferredDate: '1999-01-01' }, []],
      [{ dateMode: 'date', preferredDate: '' }, ['availability.preferredDate']],
      [{ dateMode: 'date', preferredDate: '2026-03-09' }, ['availability.preferredDate']],
      [{ dateMode: 'date', preferredDate: '2026-03-10' }, []],
      [{ dateMode: 'date', preferredDate: '2026-06-08' }, []],
      [{ dateMode: 'date', preferredDate: '2026-06-09' }, ['availability.preferredDate']],
      [{ dateMode: 'date', preferredDate: '2026-02-30' }, ['availability.preferredDate']],
    ];
    for (const [patch, expected] of cases) {
      expect(
        Object.keys(validateAvailability({ ...availability, ...patch }, today)),
        JSON.stringify(patch),
      ).toEqual(expected);
    }
    expect(addDays('2026-12-31', 1)).toBe('2027-01-01');
    expect(todayInTimeZone('Pacific/Kiritimati', new Date('2026-03-10T11:00:00Z'))).toBe(
      '2026-03-11',
    );
    expect(todayInTimeZone('Pacific/Pago_Pago', new Date('2026-03-10T05:00:00Z'))).toBe(
      '2026-03-09',
    );
  });

  it('rejects files by count, type, size and total, naming the file (AC-06)', () => {
    const file = (name: string, size: number, type = '') => ({ name, size, type });
    const five = Array.from({ length: 5 }, (_, i) => file(`f${i}.jpg`, 1));
    const cases: [typeof five, ReturnType<typeof file>, string | null][] = [
      [[], file('a.JPG', MB, 'image/jpeg'), null],
      [[], file('a.pdf', MB, 'application/pdf'), null],
      [
        five,
        file('sixth.png', 1, 'image/png'),
        'sixth.png was not added: you can add up to 5 files.',
      ],
      [
        [],
        file('doc.txt', 1, 'text/plain'),
        'doc.txt was not added: only JPG, PNG or PDF files are allowed.',
      ],
      [
        [],
        file('fake.png', 1, 'application/pdf'),
        'fake.png was not added: only JPG, PNG or PDF files are allowed.',
      ],
      [[], file('empty.png', 0, 'image/png'), 'empty.png was not added: the file is empty.'],
      [
        [],
        file('big.png', 10 * MB + 1, 'image/png'),
        'big.png was not added: files can be at most 10 MB.',
      ],
      [
        [file('a.png', 10 * MB), file('b.png', 10 * MB)],
        file('c.png', 5 * MB + 1),
        'c.png was not added: files can total at most 25 MB.',
      ],
    ];
    for (const [accepted, candidate, expected] of cases) {
      expect(validateNewFile(accepted, candidate), candidate.name).toBe(expected);
    }
  });
});
