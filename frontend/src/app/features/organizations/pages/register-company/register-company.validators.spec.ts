import { RegisterCompanyFormValue } from '../../models/organization-registration.model';
import {
  BRANCH_CODE_MESSAGE,
  COUNTRY_MESSAGE,
  CURRENCY_MESSAGE,
  END_NOT_AFTER_START_MESSAGE,
  NEXT_INVOICE_NUMBER_MESSAGE,
  PASSWORDS_DONT_MATCH_MESSAGE,
  PASSWORD_EQUALS_EMAIL_MESSAGE,
  PASSWORD_LENGTH_MESSAGE,
  PHONE_INVALID_MESSAGE,
  PREFIX_MESSAGE,
  REQUIRED_MESSAGE,
  TAX_RATE_MESSAGE,
  TIMEZONE_MESSAGE,
  tooLongMessage,
} from './register-company.messages';
import {
  validateBusinessHoursEnd,
  validateBusinessHoursStart,
  validateField,
} from './register-company.validators';

function emailOfLength(length: number): string {
  const domain = '@example.com';
  return 'a'.repeat(length - domain.length) + domain;
}

const DEFAULT: RegisterCompanyFormValue = {
  organization: {
    name: 'Acme Field Services',
    legalName: 'Acme Field Services LLC',
    taxId: '',
    email: 'billing@acme.com',
    phone: '+1 555 111 2222',
    timezone: 'America/Chicago',
    currency: 'USD',
    defaultTaxRate: 0,
    quotePrefix: 'Q',
    workOrderPrefix: 'WO',
    invoicePrefix: 'INV',
    nextInvoiceNumber: 1,
  },
  branch: {
    name: 'Main branch',
    code: 'MAIN',
    phone: '',
    email: '',
    timezone: 'America/Chicago',
    addressLine1: '123 Main St',
    city: 'Springfield',
    stateRegion: 'IL',
    postalCode: '62701',
    countryCode: 'US',
    businessHours: {
      monday: { open: true, start: '08:00', end: '17:00' },
      tuesday: { open: true, start: '08:00', end: '17:00' },
      wednesday: { open: true, start: '08:00', end: '17:00' },
      thursday: { open: true, start: '08:00', end: '17:00' },
      friday: { open: true, start: '08:00', end: '17:00' },
      saturday: { open: true, start: '09:00', end: '13:00' },
      sunday: { open: false, start: '09:00', end: '17:00' },
    },
  },
  owner: {
    firstName: 'Jane',
    lastName: 'Doe',
    email: 'owner@acme.com',
    phone: '',
    password: 'correct horse battery',
  },
  confirmPassword: 'correct horse battery',
};

function withOrganization(
  overrides: Partial<RegisterCompanyFormValue['organization']>,
): RegisterCompanyFormValue {
  return { ...DEFAULT, organization: { ...DEFAULT.organization, ...overrides } };
}

function withBranch(
  overrides: Partial<RegisterCompanyFormValue['branch']>,
): RegisterCompanyFormValue {
  return { ...DEFAULT, branch: { ...DEFAULT.branch, ...overrides } };
}

function withOwner(
  overrides: Partial<RegisterCompanyFormValue['owner']>,
): RegisterCompanyFormValue {
  return { ...DEFAULT, owner: { ...DEFAULT.owner, ...overrides } };
}

describe('register-company validators (AC-05, AC-39)', () => {
  describe('required text fields (BR-25 rules 1-2)', () => {
    it.each([
      ['organization.name', (v: string) => withOrganization({ name: v }), 160],
      ['organization.legalName', (v: string) => withOrganization({ legalName: v }), 200],
      ['branch.name', (v: string) => withBranch({ name: v }), 140],
      ['owner.firstName', (v: string) => withOwner({ firstName: v }), 100],
      ['owner.lastName', (v: string) => withOwner({ lastName: v }), 100],
      ['branch.addressLine1', (v: string) => withBranch({ addressLine1: v }), 180],
      ['branch.city', (v: string) => withBranch({ city: v }), 100],
      ['branch.postalCode', (v: string) => withBranch({ postalCode: v }), 30],
    ] as const)('%s: empty -> required, too long -> length message', (field, build, max) => {
      expect(validateField(field, build(''))).toBe(REQUIRED_MESSAGE);
      expect(validateField(field, build('   '))).toBe(REQUIRED_MESSAGE);
      expect(validateField(field, build('a'.repeat(max + 1)))).toBe(tooLongMessage(max));
      expect(validateField(field, build('a'.repeat(max)))).toBeNull();
    });
  });

  it('organization.taxId: only the too-long rule applies (optional)', () => {
    expect(validateField('organization.taxId', withOrganization({ taxId: '' }))).toBeNull();
    expect(validateField('organization.taxId', withOrganization({ taxId: 'a'.repeat(61) }))).toBe(
      tooLongMessage(60),
    );
  });

  it('branch.stateRegion: only the too-long rule applies (optional)', () => {
    expect(validateField('branch.stateRegion', withBranch({ stateRegion: '' }))).toBeNull();
    expect(validateField('branch.stateRegion', withBranch({ stateRegion: 'a'.repeat(101) }))).toBe(
      tooLongMessage(100),
    );
  });

  describe('required emails', () => {
    it.each([
      ['organization.email', (v: string) => withOrganization({ email: v })],
      ['owner.email', (v: string) => withOwner({ email: v })],
    ] as const)('%s: required, length, format', (field, build) => {
      expect(validateField(field, build(''))).toBe(REQUIRED_MESSAGE);
      expect(validateField(field, build(emailOfLength(255)))).toBe(tooLongMessage(254));
      expect(validateField(field, build('not-an-email'))).toBe(
        'Enter a valid email address, for example name@company.com.',
      );
      expect(validateField(field, build('valid@example.com'))).toBeNull();
    });
  });

  it('branch.email: optional, length and format only', () => {
    expect(validateField('branch.email', withBranch({ email: '' }))).toBeNull();
    expect(validateField('branch.email', withBranch({ email: emailOfLength(255) }))).toBe(
      tooLongMessage(254),
    );
    expect(validateField('branch.email', withBranch({ email: 'not-an-email' }))).toBe(
      'Enter a valid email address, for example name@company.com.',
    );
  });

  it('organization.phone: required, length, format', () => {
    expect(validateField('organization.phone', withOrganization({ phone: '' }))).toBe(
      REQUIRED_MESSAGE,
    );
    expect(validateField('organization.phone', withOrganization({ phone: '1'.repeat(41) }))).toBe(
      tooLongMessage(40),
    );
    expect(validateField('organization.phone', withOrganization({ phone: '123456' }))).toBe(
      PHONE_INVALID_MESSAGE,
    );
    expect(
      validateField('organization.phone', withOrganization({ phone: '+1 555 000 1111' })),
    ).toBeNull();
  });

  it.each([
    ['branch.phone', (v: string) => withBranch({ phone: v })],
    ['owner.phone', (v: string) => withOwner({ phone: v })],
  ] as const)('%s: optional, length and format only', (field, build) => {
    expect(validateField(field, build(''))).toBeNull();
    expect(validateField(field, build('1'.repeat(41)))).toBe(tooLongMessage(40));
    expect(validateField(field, build('123456'))).toBe(PHONE_INVALID_MESSAGE);
  });

  it.each([
    ['organization.timezone', (v: string) => withOrganization({ timezone: v })],
    ['branch.timezone', (v: string) => withBranch({ timezone: v })],
  ] as const)('%s: empty or unknown zone -> select message', (field, build) => {
    expect(validateField(field, build(''))).toBe(TIMEZONE_MESSAGE);
    expect(validateField(field, build('Not/AZone'))).toBe(TIMEZONE_MESSAGE);
    expect(validateField(field, build('America/Chicago'))).toBeNull();
  });

  it('organization.currency: empty or unsupported code -> select message', () => {
    expect(validateField('organization.currency', withOrganization({ currency: '' }))).toBe(
      CURRENCY_MESSAGE,
    );
    expect(validateField('organization.currency', withOrganization({ currency: 'XAU' }))).toBe(
      CURRENCY_MESSAGE,
    );
    expect(
      validateField('organization.currency', withOrganization({ currency: 'USD' })),
    ).toBeNull();
  });

  it('branch.countryCode: empty or unsupported code -> select message', () => {
    expect(validateField('branch.countryCode', withBranch({ countryCode: '' }))).toBe(
      COUNTRY_MESSAGE,
    );
    expect(validateField('branch.countryCode', withBranch({ countryCode: 'ZZ' }))).toBe(
      COUNTRY_MESSAGE,
    );
    expect(validateField('branch.countryCode', withBranch({ countryCode: 'US' }))).toBeNull();
  });

  it('organization.defaultTaxRate: required, range and decimals', () => {
    expect(
      validateField('organization.defaultTaxRate', withOrganization({ defaultTaxRate: null })),
    ).toBe(REQUIRED_MESSAGE);
    expect(
      validateField('organization.defaultTaxRate', withOrganization({ defaultTaxRate: 101 })),
    ).toBe(TAX_RATE_MESSAGE);
    expect(
      validateField('organization.defaultTaxRate', withOrganization({ defaultTaxRate: 1.23456 })),
    ).toBe(TAX_RATE_MESSAGE);
    expect(
      validateField('organization.defaultTaxRate', withOrganization({ defaultTaxRate: 7.5 })),
    ).toBeNull();
  });

  it.each([
    'organization.quotePrefix',
    'organization.workOrderPrefix',
    'organization.invoicePrefix',
  ] as const)('%s: required, length/characters', (field) => {
    const withPrefix = (v: string) =>
      withOrganization({ quotePrefix: v, workOrderPrefix: v, invoicePrefix: v });
    expect(validateField(field, withPrefix(''))).toBe(REQUIRED_MESSAGE);
    expect(validateField(field, withPrefix('A'.repeat(21)))).toBe(PREFIX_MESSAGE);
    expect(validateField(field, withPrefix('AB_1'))).toBe(PREFIX_MESSAGE);
    expect(validateField(field, withPrefix('Q-1'))).toBeNull();
  });

  it('organization.nextInvoiceNumber: required, integer range', () => {
    expect(
      validateField(
        'organization.nextInvoiceNumber',
        withOrganization({ nextInvoiceNumber: null }),
      ),
    ).toBe(REQUIRED_MESSAGE);
    expect(
      validateField('organization.nextInvoiceNumber', withOrganization({ nextInvoiceNumber: 0 })),
    ).toBe(NEXT_INVOICE_NUMBER_MESSAGE);
    expect(
      validateField(
        'organization.nextInvoiceNumber',
        withOrganization({ nextInvoiceNumber: 1_000_000_000_000 }),
      ),
    ).toBe(NEXT_INVOICE_NUMBER_MESSAGE);
    expect(
      validateField('organization.nextInvoiceNumber', withOrganization({ nextInvoiceNumber: 1.5 })),
    ).toBe(NEXT_INVOICE_NUMBER_MESSAGE);
    expect(
      validateField('organization.nextInvoiceNumber', withOrganization({ nextInvoiceNumber: 1 })),
    ).toBeNull();
  });

  it('branch.code: required, length/characters', () => {
    expect(validateField('branch.code', withBranch({ code: '' }))).toBe(REQUIRED_MESSAGE);
    expect(validateField('branch.code', withBranch({ code: 'ABCDEFGHI' }))).toBe(
      BRANCH_CODE_MESSAGE,
    );
    expect(validateField('branch.code', withBranch({ code: 'AB_1' }))).toBe(BRANCH_CODE_MESSAGE);
    expect(validateField('branch.code', withBranch({ code: 'MAIN' }))).toBeNull();
  });

  describe('business hours (open day, AC-31)', () => {
    it('start: empty -> required', () => {
      expect(validateBusinessHoursStart('')).toBe(REQUIRED_MESSAGE);
      expect(validateBusinessHoursStart('08:00')).toBeNull();
    });

    it('end: empty -> required, not after start -> end message', () => {
      expect(validateBusinessHoursEnd('08:00', '')).toBe(REQUIRED_MESSAGE);
      expect(validateBusinessHoursEnd('08:00', '08:00')).toBe(END_NOT_AFTER_START_MESSAGE);
      expect(validateBusinessHoursEnd('08:00', '07:00')).toBe(END_NOT_AFTER_START_MESSAGE);
      expect(validateBusinessHoursEnd('08:00', '17:00')).toBeNull();
    });
  });

  describe('owner.password', () => {
    it('empty -> required', () => {
      expect(validateField('owner.password', withOwner({ password: '' }))).toBe(REQUIRED_MESSAGE);
    });

    it('too short or too long -> length message', () => {
      expect(validateField('owner.password', withOwner({ password: 'short11' }))).toBe(
        PASSWORD_LENGTH_MESSAGE,
      );
      expect(validateField('owner.password', withOwner({ password: 'a'.repeat(129) }))).toBe(
        PASSWORD_LENGTH_MESSAGE,
      );
    });

    it('equal to the email (ignoring case) -> different-from-email message', () => {
      expect(
        validateField(
          'owner.password',
          withOwner({ email: 'owner@acme.com', password: 'OWNER@ACME.COM' }),
        ),
      ).toBe(PASSWORD_EQUALS_EMAIL_MESSAGE);
    });

    it('AC-39: a short password equal to the email shows only the length message', () => {
      expect(
        validateField('owner.password', withOwner({ email: 'ab@cd.io', password: 'ab@cd.io' })),
      ).toBe(PASSWORD_LENGTH_MESSAGE);
    });
  });

  describe('confirmPassword (BR-18, client-only)', () => {
    it('empty -> required', () => {
      expect(validateField('confirmPassword', { ...DEFAULT, confirmPassword: '' })).toBe(
        REQUIRED_MESSAGE,
      );
    });

    it('different from password -> mismatch message', () => {
      expect(
        validateField('confirmPassword', { ...DEFAULT, confirmPassword: 'something else12' }),
      ).toBe(PASSWORDS_DONT_MATCH_MESSAGE);
    });

    it('equal to password -> valid', () => {
      expect(validateField('confirmPassword', DEFAULT)).toBeNull();
    });
  });
});
