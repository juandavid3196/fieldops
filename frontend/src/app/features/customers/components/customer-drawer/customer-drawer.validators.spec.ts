import { CustomerFieldKey } from '../../models/customer.model';
import { formatPhone, normalizePhone } from '../../utils/customer-format';
import {
  CustomerFormValue,
  mapServerFieldErrors,
  validateCustomer,
  validateCustomerField,
} from './customer-drawer.validators';

const VALID: CustomerFormValue = {
  type: 'residential',
  companyName: '',
  firstName: 'Sofia',
  lastName: 'Martinez',
  title: '',
  email: 'sofia@example.com',
  phone: '(512) 555-7832',
  prefersEmail: true,
  prefersSms: false,
  addressLine1: '12 Oak St',
  city: 'Austin',
  stateRegion: 'TX',
  postalCode: '78701-1234',
  serviceInstructions: '',
  branchId: 'b-1',
  tagIds: [],
  internalNote: '',
};

describe('Customer field validation (BR-10, BR-12, BR-13)', () => {
  it.each<[string, CustomerFieldKey, Partial<CustomerFormValue>, boolean, string | null]>([
    ['valid residential form', 'email', {}, true, null],
    [
      'company name required for commercial',
      'companyName',
      { type: 'commercial' },
      true,
      'Enter a company name.',
    ],
    ['company name ignored for residential', 'companyName', { companyName: '' }, true, null],
    [
      'company name too long',
      'companyName',
      { type: 'commercial', companyName: 'x'.repeat(181) },
      true,
      'Use 180 characters or fewer.',
    ],
    ['first name required', 'firstName', { firstName: '  ' }, true, 'Enter a first name.'],
    [
      'last name too long',
      'lastName',
      { lastName: 'x'.repeat(101) },
      true,
      'Use 100 characters or fewer.',
    ],
    [
      'residential full name too long',
      'lastName',
      { firstName: 'x'.repeat(90), lastName: 'y'.repeat(90) },
      true,
      'Use 180 characters or fewer for the full name.',
    ],
    [
      'title too long (commercial)',
      'title',
      { type: 'commercial', title: 'x'.repeat(101) },
      true,
      'Use 100 characters or fewer.',
    ],
    ['email required', 'email', { email: '' }, true, 'Enter an email.'],
    ['email malformed', 'email', { email: 'not-an-email' }, true, 'Enter a valid email.'],
    ['phone too short', 'phone', { phone: '555-1234' }, true, 'Enter a valid phone number.'],
    ['phone with letters', 'phone', { phone: '512-555-CALL' }, true, 'Enter a valid phone number.'],
    ['phone optional', 'phone', { phone: '' }, true, null],
    [
      'no preference',
      'preferences',
      { prefersEmail: false, prefersSms: false },
      true,
      'Choose at least one communication method.',
    ],
    [
      'SMS without phone',
      'preferences',
      { prefersSms: true, phone: '' },
      true,
      'Add a mobile phone to use SMS.',
    ],
    ['SMS with phone', 'preferences', { prefersSms: true }, true, null],
    ['address required', 'addressLine1', { addressLine1: '' }, true, 'Enter an address.'],
    ['city required', 'city', { city: '' }, true, 'Enter a city.'],
    [
      'US state outside the list',
      'stateRegion',
      { stateRegion: 'ZZ' },
      true,
      'Choose a valid state.',
    ],
    ['non-US state is free text', 'stateRegion', { stateRegion: 'Ontario' }, false, null],
    ['US ZIP malformed', 'postalCode', { postalCode: '1234' }, true, 'Enter a valid ZIP code.'],
    ['non-US postal code up to 30', 'postalCode', { postalCode: 'M5V 2T6' }, false, null],
    [
      'instructions too long',
      'serviceInstructions',
      { serviceInstructions: 'x'.repeat(2001) },
      true,
      'Use 2,000 characters or fewer.',
    ],
    [
      'note too long',
      'internalNote',
      { internalNote: 'x'.repeat(2001) },
      true,
      'Use 2,000 characters or fewer.',
    ],
    ['branch required', 'branchId', { branchId: null }, true, 'Choose a branch.'],
    [
      'eleven tags',
      'tagIds',
      { tagIds: Array.from({ length: 11 }, (_, i) => `t-${i}`) },
      true,
      'Choose up to 10 tags.',
    ],
  ])('%s', (_name, field, changes, us, expected) => {
    expect(validateCustomerField(field, { ...VALID, ...changes }, us)).toBe(expected);
  });

  it('accepts a complete form, normalizes phones and maps server errors under their fields', () => {
    expect(validateCustomer(VALID, true)).toEqual({});
    expect(normalizePhone('+1 (512) 555-7832')).toBe('5125557832');
    expect(normalizePhone('+44 20 7946 0958')).toBe('442079460958');
    expect(formatPhone('5125557832')).toBe('(512) 555-7832');
    expect(
      mapServerFieldErrors({
        'contact.email': ['Enter a valid email.'],
        'property.addressLine1': ['Enter an address.'],
        'contact.PreferredCommunication': ['Add a mobile phone to use SMS.'],
        tagIds: ['Choose up to 10 tags.'],
        branchId: ['Choose a branch you have access to.'],
        unknown: ['ignored'],
      }),
    ).toEqual({
      email: 'Enter a valid email.',
      addressLine1: 'Enter an address.',
      preferences: 'Add a mobile phone to use SMS.',
      tagIds: 'Choose up to 10 tags.',
      branchId: 'Choose a branch you have access to.',
    });
  });
});
