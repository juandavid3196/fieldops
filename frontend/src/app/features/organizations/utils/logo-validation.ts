/** BR-07: 2 MB (2,097,152 bytes). */
export const LOGO_MAX_BYTES = 2_097_152;
export const LOGO_TYPE_MESSAGE = 'Choose a JPG, PNG or SVG file.';
export const LOGO_SIZE_MESSAGE = 'Choose a file of 2 MB or smaller.';
export const LOGO_HELPER = 'JPG, PNG or SVG. Max 2 MB.';

const LOGO_EXTENSIONS: ReadonlySet<string> = new Set(['png', 'jpg', 'jpeg', 'svg']);
const LOGO_MIME_TYPES: ReadonlySet<string> = new Set(['image/png', 'image/jpeg', 'image/svg+xml']);

/** Value of the file input's `accept` attribute. */
export const LOGO_ACCEPT = '.png,.jpg,.jpeg,.svg,image/png,image/jpeg,image/svg+xml';

/**
 * Client mirror of BR-07: extension and declared type first, then size.
 * The server still decides from the file content; this only avoids a pointless upload.
 */
export function validateLogoFile(file: {
  readonly name: string;
  readonly size: number;
  readonly type: string;
}): string | null {
  const extension = file.name.includes('.') ? file.name.split('.').pop()!.toLowerCase() : '';
  const typeAllowed = file.type === '' || LOGO_MIME_TYPES.has(file.type.toLowerCase());
  if (!LOGO_EXTENSIONS.has(extension) || !typeAllowed) {
    return LOGO_TYPE_MESSAGE;
  }
  return file.size > LOGO_MAX_BYTES ? LOGO_SIZE_MESSAGE : null;
}
