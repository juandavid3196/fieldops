/**
 * Resolves page tokens to concrete values for Stripe's Appearance API, which cannot read CSS
 * custom properties or `light-dark()`. A probe element lets the browser compute them.
 */
export function resolveStripeAppearance(doc: Document): Record<string, string> {
  const probe = doc.createElement('span');
  probe.style.display = 'none';
  doc.body.appendChild(probe);
  const resolve = (property: 'color' | 'fontFamily' | 'borderRadius', token: string): string => {
    probe.style[property] = `var(${token})`;
    return doc.defaultView?.getComputedStyle(probe)[property] ?? '';
  };
  const entries: [string, string][] = [
    ['colorPrimary', resolve('color', '--fo-color-primary')],
    ['colorBackground', resolve('color', '--fo-color-surface')],
    ['colorText', resolve('color', '--fo-color-text')],
    ['colorTextSecondary', resolve('color', '--fo-color-text-secondary')],
    ['colorTextPlaceholder', resolve('color', '--fo-color-text-muted')],
    ['colorDanger', resolve('color', '--fo-color-danger-solid')],
    ['fontFamily', resolve('fontFamily', '--fo-font-family')],
    ['borderRadius', resolve('borderRadius', '--fo-radius-control')],
  ];
  probe.remove();
  return Object.fromEntries(entries.filter(([, value]) => value !== ''));
}
