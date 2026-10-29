/** Session body returned by `POST /sessions` and `GET /sessions/current`. */
export interface Session {
  readonly user: SessionUser;
  readonly organization: SessionOrganization;
  readonly role: SessionRole;
  /** `true` when the organization is a read-only demo; decided by the API, used only for display. */
  readonly demoReadOnly?: boolean;
}

export interface SessionUser {
  readonly id: string;
  readonly firstName: string;
  readonly lastName: string;
  readonly email: string;
  /** ISO timestamp of the profile photo, `null` without one (account-settings BR-08). */
  readonly photoUpdatedAt: string | null;
  /** Persisted UI language (`en` or `es`); an unsupported value is ignored (app-i18n-en-es BR-02). */
  readonly preferredLanguage?: string;
  /** Persisted appearance (`system`, `light` or `dark`); an unsupported value is ignored (app-dark-mode BR-02). */
  readonly preferredTheme?: string;
}

export interface SessionOrganization {
  readonly id: string;
  readonly name: string;
}

/** Role of the membership, for display only. */
export interface SessionRole {
  readonly code: string;
  readonly name: string;
}

/** Body of `POST /sessions`. It never carries an organization or other identifier. */
export interface SignInRequest {
  readonly email: string;
  readonly password: string;
  readonly rememberMe: boolean;
}
