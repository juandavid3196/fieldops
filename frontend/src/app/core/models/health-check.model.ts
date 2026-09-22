/** Response of the API `GET /health` endpoint. */
export interface HealthCheckResponse {
  readonly status: string;
  readonly totalDurationMs: number;
  readonly checks: readonly HealthCheckEntry[];
}

export interface HealthCheckEntry {
  readonly name: string;
  readonly status: string;
  readonly durationMs: number;
}
