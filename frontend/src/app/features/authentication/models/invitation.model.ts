/** Body of `POST /invitations/validate` (BR-04). It carries no identifiers. */
export interface InvitationDetails {
  readonly organizationName: string;
  readonly inviterName: string;
  readonly email: string;
  readonly firstName: string;
  readonly lastName: string;
  readonly role: { readonly code: string; readonly name: string };
  readonly isAllBranches: boolean;
  readonly branches: readonly { readonly name: string }[];
  readonly expiresAt: string;
}

/** Body of `POST /invitations/accept` (new account). */
export interface AcceptInvitationRequest {
  readonly token: string;
  readonly firstName: string;
  readonly lastName: string;
  readonly password: string;
}
