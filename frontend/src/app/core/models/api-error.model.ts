export type ApiErrorKind =
  | 'network'
  | 'bad-request'
  | 'validation'
  | 'unauthorized'
  | 'forbidden'
  | 'not-found'
  | 'conflict'
  | 'rate-limited'
  | 'unavailable'
  | 'server'
  | 'unknown';

/**
 * Normalized API failure that is safe to display. It never carries backend
 * exception details; `traceId` is kept only as a support reference.
 */
export interface ApiError {
  readonly kind: ApiErrorKind;
  /** HTTP status code, or 0 when the request never reached the server. */
  readonly status: number;
  /** User-friendly message. */
  readonly message: string;
  /** Field-level validation messages keyed by field name. Empty when not applicable. */
  readonly fieldErrors: Readonly<Record<string, readonly string[]>>;
  readonly traceId?: string;
  /**
   * Seconds to wait before retrying, from the `Retry-After` header of a `429`
   * response. `undefined` for other statuses or when the header is absent or
   * not a non-negative integer of seconds.
   */
  readonly retryAfterSeconds?: number;
}

export function isApiError(value: unknown): value is ApiError {
  return (
    typeof value === 'object' &&
    value !== null &&
    'kind' in value &&
    'status' in value &&
    'message' in value &&
    'fieldErrors' in value
  );
}
