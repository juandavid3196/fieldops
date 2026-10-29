/** Body of `POST /portal/sessions`, `GET /portal/sessions/current` and account switching (BR-05). */
export interface PortalSession {
  readonly user: PortalSessionUser;
  readonly account: PortalAccount;
  /** Every active link in BR-04 order. */
  readonly accounts: readonly PortalAccountSummary[];
  /** `true` when the organization is a read-only demo; decided by the API, used only for display. */
  readonly demoReadOnly?: boolean;
}

export interface PortalSessionUser {
  readonly firstName: string;
  readonly lastName: string;
  readonly email: string;
  /** Persisted UI language (`en` or `es`); an unsupported value is ignored (app-i18n-en-es BR-02). */
  readonly preferredLanguage?: string;
  /** Persisted appearance (`system`, `light` or `dark`); an unsupported value is ignored (app-dark-mode BR-02). */
  readonly preferredTheme?: string;
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
