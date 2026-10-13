import { Component } from '@angular/core';

/**
 * Static, decorative street-map illustration with a location pin (Design 28 property preview).
 * It is not a real map: no provider, no geocoding, hidden from assistive technology.
 */
@Component({
  selector: 'app-portal-map-illustration',
  template: `
    <svg
      class="map"
      viewBox="0 0 320 190"
      preserveAspectRatio="xMidYMid slice"
      aria-hidden="true"
      focusable="false"
    >
      <rect class="map__ground" width="320" height="190" />
      <g class="map__block">
        <rect x="8" y="10" width="54" height="34" rx="3" />
        <rect x="76" y="6" width="40" height="44" rx="3" />
        <rect x="200" y="12" width="44" height="30" rx="3" />
        <rect x="20" y="70" width="46" height="38" rx="3" />
        <rect x="96" y="78" width="36" height="30" rx="3" />
        <rect x="214" y="78" width="40" height="40" rx="3" />
        <rect x="270" y="96" width="44" height="34" rx="3" />
        <rect x="10" y="140" width="56" height="40" rx="3" />
        <rect x="150" y="140" width="48" height="38" rx="3" />
        <rect x="236" y="150" width="40" height="30" rx="3" />
      </g>
      <g class="map__park">
        <rect x="258" y="8" width="54" height="46" rx="4" />
        <rect x="84" y="136" width="44" height="36" rx="4" />
      </g>
      <g class="map__street">
        <path d="M-10 58 L330 34" />
        <path d="M-10 128 L330 92" />
        <path d="M70 -10 L148 200" />
        <path d="M188 -10 L214 200" />
      </g>
      <g class="map__lane">
        <path d="M-10 172 L330 140" />
        <path d="M262 -10 L292 200" />
      </g>
      <ellipse class="map__shadow" cx="160" cy="112" rx="14" ry="5" />
      <path
        class="map__pin"
        d="M160 110c-1.6 0-17-17.4-17-30a17 17 0 0 1 34 0c0 12.6-15.4 30-17 30z"
      />
      <circle class="map__pin-dot" cx="160" cy="80" r="6.5" />
    </svg>
  `,
  styles: `
    :host {
      display: block;
      overflow: hidden;
      border-radius: var(--fo-radius-control);
    }

    .map {
      display: block;
      inline-size: 100%;
      block-size: 11rem;
    }

    .map__ground {
      fill: var(--fo-color-surface-muted);
    }

    .map__block {
      fill: var(--fo-color-border);
    }

    .map__park {
      fill: var(--fo-color-success-background);
    }

    .map__street,
    .map__lane {
      fill: none;
      stroke: var(--fo-color-surface);
      stroke-linecap: round;
    }

    .map__street {
      stroke-width: 12;
    }

    .map__lane {
      stroke-width: 7;
    }

    .map__shadow {
      fill: var(--fo-color-primary-soft);
    }

    .map__pin {
      fill: var(--fo-color-primary-strong);
    }

    .map__pin-dot {
      fill: var(--fo-color-surface);
    }
  `,
})
export class PortalMapIllustration {}
