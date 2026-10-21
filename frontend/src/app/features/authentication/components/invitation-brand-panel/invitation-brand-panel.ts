import { NgOptimizedImage } from '@angular/common';
import { Component } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';

/**
 * Decorative brand panel of the invitation screens (designs 26); shown from `lg`.
 * Dedicated component: the Sign In panel has fixed, different copy and imagery.
 * The photo stays lazy so it is not downloaded below `lg` (see the sign-in panel).
 */
@Component({
  selector: 'app-invitation-brand-panel',
  imports: [TranslocoPipe, NgOptimizedImage],
  templateUrl: './invitation-brand-panel.html',
  styleUrl: './invitation-brand-panel.scss',
})
export class InvitationBrandPanel {}
