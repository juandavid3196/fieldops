import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';

/**
 * "Other ways to get started" card of the Sign In page (design 26). Only "Create a company
 * account" navigates; the other rows are visual placeholders and stay inert.
 */
@Component({
  selector: 'app-sign-in-other-ways',
  imports: [RouterLink],
  templateUrl: './sign-in-other-ways.html',
  styleUrl: './sign-in-other-ways.scss',
})
export class SignInOtherWays {}
