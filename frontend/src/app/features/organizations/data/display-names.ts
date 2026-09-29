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

function timeZoneNamePart(code: string, style: 'longGeneric' | 'longOffset'): string | null {
  try {
    const parts = new Intl.DateTimeFormat('en', {
      timeZone: code,
      timeZoneName: style,
    }).formatToParts(new Date());
    return parts.find((part) => part.type === 'timeZoneName')?.value ?? null;
  } catch {
    return null;
  }
}

/** Generic name of an IANA time zone (for example "Central Time"), or the identifier itself. */
export function timezoneGenericName(code: string): string {
  return timeZoneNamePart(code, 'longGeneric') ?? code;
}

/** Current UTC offset in minutes (`GMT-05:30` is -330). */
function utcOffsetMinutes(code: string): number {
  const match = /^GMT([+-])(\d{2}):(\d{2})$/.exec(timeZoneNamePart(code, 'longOffset') ?? '');
  if (match === null) {
    return 0;
  }
  const minutes = Number(match[2]) * 60 + Number(match[3]);
  return match[1] === '-' ? -minutes : minutes;
}

/** "(UTC−06:00)" from an offset in minutes; the minus sign is U+2212 as in the handoff. */
function utcOffsetLabel(minutes: number): string {
  const sign = minutes < 0 ? '−' : '+';
  const abs = Math.abs(minutes);
  const hh = String(Math.floor(abs / 60)).padStart(2, '0');
  const mm = String(abs % 60).padStart(2, '0');
  return `(UTC${sign}${hh}:${mm})`;
}

/**
 * Time zone options (BR-08, AS-02): every IANA identifier the browser supports, labelled
 * "(UTC±hh:mm) {generic name} — {identifier}" and sorted by offset, then name.
 */
export function timezoneOptions(): SelectOption[] {
  return Intl.supportedValuesOf('timeZone')
    .map((code) => {
      const minutes = utcOffsetMinutes(code);
      const generic = timezoneGenericName(code);
      return { minutes, code, label: `${utcOffsetLabel(minutes)} ${generic} — ${code}` };
    })
    .sort((a, b) => a.minutes - b.minutes || a.label.localeCompare(b.label))
    .map(({ code, label }) => ({ code, label }));
}

/** The browser's IANA time zone, or `''` when it is not in {@link timezoneOptions} (BR-02). */
export function browserTimezone(): string {
  const zone = Intl.DateTimeFormat().resolvedOptions().timeZone;
  return Intl.supportedValuesOf('timeZone').includes(zone) ? zone : '';
}
