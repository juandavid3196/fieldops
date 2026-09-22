/**
 * RFC 9457 problem details as produced by ASP.NET Core
 * (`application/problem+json`).
 */
export interface ProblemDetails {
  readonly type?: string;
  readonly title?: string;
  readonly status?: number;
  readonly detail?: string;
  readonly instance?: string;
  readonly traceId?: string;
  readonly [extension: string]: unknown;
}

/** ASP.NET Core `ValidationProblemDetails`: field name to error messages. */
export interface ValidationProblemDetails extends ProblemDetails {
  readonly errors: Readonly<Record<string, readonly string[]>>;
}
