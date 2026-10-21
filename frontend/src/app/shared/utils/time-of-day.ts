import { t } from '../../core/i18n/translate';

/** `HH:mm` (24-hour) to 12-hour display, e.g. `08:15` to `8:15 AM`; other input is returned as is. */
export function formatTime12(value: string): string {
  const match = /^(\d{2}):(\d{2})/.exec(value);
  if (match === null) {
    return value;
  }
  const hours = Number(match[1]);
  const period = hours < 12 ? t('common.am') : t('common.pm');
  return `${hours % 12 === 0 ? 12 : hours % 12}:${match[2]} ${period}`;
}
