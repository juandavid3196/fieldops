import { Component } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';

/** Public layout route: header and footer shared by Privacy, Terms and the public Help page. */
@Component({
  selector: 'app-public-site-shell',
  imports: [RouterLink, RouterLinkActive, RouterOutlet],
  templateUrl: './public-site-shell.html',
  styleUrl: './public-site-shell.scss',
})
export class PublicSiteShell {
  protected readonly links = [
    { label: 'Privacy', path: '/privacy' },
    { label: 'Terms', path: '/terms' },
    { label: 'Help', path: '/support' },
  ] as const;
  protected readonly year = new Date().getFullYear();
}
