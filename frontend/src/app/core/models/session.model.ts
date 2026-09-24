/** Session body returned by `POST /sessions` and `GET /sessions/current`. */
export interface Session {
  readonly user: SessionUser;
  readonly organization: SessionOrganization;
  readonly role: SessionRole;
}

export interface SessionUser {
  readonly id: string;
  readonly firstName: string;
  readonly lastName: string;
  readonly email: string;
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
