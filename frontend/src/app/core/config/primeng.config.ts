import { definePreset } from '@primeuix/themes';
import Aura from '@primeuix/themes/aura';
import { PrimeNGConfigType } from 'primeng/config';

/**
 * FieldOps preset: Aura with the teal palette as primary.
 * `primary.color` is darkened to teal.700 in light mode so white text on it meets WCAG AA.
 * Success and error message text is darkened one step in light mode (and error text
 * lightened one step in dark mode) for the same reason.
 * See docs/frontend/styling-architecture.md.
 */
const FieldOpsPreset = definePreset(Aura, {
  semantic: {
    primary: {
      50: '{teal.50}',
      100: '{teal.100}',
      200: '{teal.200}',
      300: '{teal.300}',
      400: '{teal.400}',
      500: '{teal.500}',
      600: '{teal.600}',
      700: '{teal.700}',
      800: '{teal.800}',
      900: '{teal.900}',
      950: '{teal.950}',
      color: 'light-dark({primary.700}, {primary.400})',
      hoverColor: 'light-dark({primary.800}, {primary.300})',
      activeColor: 'light-dark({primary.900}, {primary.200})',
    },
  },
  components: {
    // Aura's light success/error message text (green.600/red.600) fails WCAG AA
    // on the message background; one step darker passes (sign-in spec, AC-56).
    // Dark error text uses red.400 (red.500 is 3.98:1 on the dark message background);
    // dark success keeps green.500, which already passes.
    message: {
      success: {
        color: 'light-dark({green.700}, {green.500})',
        simple: { color: 'light-dark({green.700}, {green.500})' },
      },
      error: {
        color: 'light-dark({red.700}, {red.400})',
        simple: { color: 'light-dark({red.700}, {red.400})' },
      },
    },
  },
});

/** Evaluated once at bootstrap; safe where `window` or `matchMedia` is unavailable. */
const prefersReducedMotion =
  typeof window !== 'undefined' &&
  typeof window.matchMedia === 'function' &&
  window.matchMedia('(prefers-reduced-motion: reduce)').matches;

export const primeNgConfig: PrimeNGConfigType = {
  ripple: !prefersReducedMotion,
  theme: {
    preset: FieldOpsPreset,
    options: {
      // Dark mode applies when `.app-dark` is set on <html>.
      darkModeSelector: '.app-dark',
    },
  },
};
