/** A selectable option: the value sent to the server and its display label. */
export interface SelectOption {
  readonly code: string;
  readonly label: string;
}

/** Country options (AS-05), labelled with `Intl.DisplayNames`, sorted by label. */
export function countryOptions(codes: readonly string[]): SelectOption[] {
  const names = new Intl.DisplayNames(['en'], { type: 'region' });
  return codes
    .map((code) => ({ code, label: names.of(code) ?? code }))
    .sort((a, b) => a.label.localeCompare(b.label));
}

/** Currency options (BR-09), labelled with `Intl.DisplayNames`, sorted by code. */
export function currencyOptions(codes: readonly string[]): SelectOption[] {
  const names = new Intl.DisplayNames(['en'], { type: 'currency' });
  return codes
    .map((code) => ({ code, label: `${code} — ${names.of(code) ?? code}` }))
    .sort((a, b) => a.code.localeCompare(b.code));
}

/** Time zone options (BR-08): every IANA identifier the browser supports. */
export function timezoneOptions(): SelectOption[] {
  return Intl.supportedValuesOf('timeZone')
    .map((code) => ({ code, label: code }))
    .sort((a, b) => a.label.localeCompare(b.label));
}

/** The browser's IANA time zone, or `''` when it is not in {@link timezoneOptions} (BR-02). */
export function browserTimezone(): string {
  const zone = Intl.DateTimeFormat().resolvedOptions().timeZone;
  return Intl.supportedValuesOf('timeZone').includes(zone) ? zone : '';
}
