/**
 * Build-time configuration. Values are bundled into public JavaScript,
 * so never place secrets, tokens or connection strings here.
 */
export interface Environment {
  readonly production: boolean;
  /** Absolute or same-origin base URL of the FieldOps API, without trailing slash. */
  readonly apiBaseUrl: string;
  /** Public support mailbox used by the Help & support center `mailto:` action. */
  readonly supportEmail: string;
}
