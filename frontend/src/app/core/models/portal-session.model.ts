/** Body of `POST /portal/sessions`, `GET /portal/sessions/current` and account switching (BR-05). */
export interface PortalSession {
  readonly user: PortalSessionUser;
  readonly account: PortalAccount;
  /** Every active link in BR-04 order. */
  readonly accounts: readonly PortalAccountSummary[];
}

export interface PortalSessionUser {
  readonly firstName: string;
  readonly lastName: string;
  readonly email: string;
}

export interface PortalAccountSummary {
  readonly contactId: string;
  readonly customerName: string;
  readonly organizationName: string;
}

export interface PortalAccount extends PortalAccountSummary {
  readonly hasLogo: boolean;
}

/** Body of `POST /portal/sessions`. It never carries an organization or other identifier. */
export interface PortalSignInRequest {
  readonly email: string;
  readonly password: string;
  readonly rememberMe: boolean;
}
