import { Component, input, output } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { TooltipModule } from 'primeng/tooltip';

import { NavGroup } from '../app-shell/app-shell.nav';

/**
 * Navy navigation (FR-01, FR-02). Full: brand, grouped items and Collapse. Rail: icons only,
 * each item keeping its accessible name and tooltip, group headings hidden. In the mobile drawer
 * (`drawer`) only the navigation list renders; the drawer supplies its own header.
 */
@Component({
  selector: 'app-shell-sidebar',
  imports: [RouterLink, RouterLinkActive, TooltipModule],
  templateUrl: './shell-sidebar.html',
  styleUrl: './shell-sidebar.scss',
})
export class ShellSidebar {
  readonly groups = input.required<readonly NavGroup[]>();
  readonly rail = input(false);
  readonly drawer = input(false);
  readonly navigated = output<void>();
  readonly closeRequested = output<void>();
  readonly collapseToggled = output<void>();
}
