/** BR-16: the dashboard reloads every 60 seconds while the tab is visible. */
export const REFRESH_INTERVAL_MS = 60_000;

/** "Updated just now" below one minute, else "Updated <n> min ago". */
export function agoLabel(updatedAt: number | null, now: number): string {
  if (updatedAt === null) {
    return '';
  }
  const minutes = Math.floor(Math.max(0, now - updatedAt) / REFRESH_INTERVAL_MS);
  return minutes < 1 ? 'Updated just now' : `Updated ${minutes} min ago`;
}

export interface RefreshState {
  readonly visible: boolean;
  readonly loading: boolean;
  /** Start of the last load attempt, or `null` when none ran yet. */
  readonly lastAttemptAt: number | null;
  readonly now: number;
}

/** The 1-second tick starts an automatic refresh once 60 s passed since the last attempt. */
export function shouldAutoRefresh(state: RefreshState): boolean {
  return (
    state.visible &&
    !state.loading &&
    state.lastAttemptAt !== null &&
    state.now - state.lastAttemptAt >= REFRESH_INTERVAL_MS
  );
}

/** A tab hidden for more than 60 s reloads at once when it becomes visible again. */
export function hiddenTooLong(hiddenAt: number | null, now: number): boolean {
  return hiddenAt !== null && now - hiddenAt > REFRESH_INTERVAL_MS;
}
