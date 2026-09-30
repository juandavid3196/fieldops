/** Body of `POST /password-resets/validate` (BR-07). It carries no other data. */
export interface PasswordResetDetails {
  readonly email: string;
}
