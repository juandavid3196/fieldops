/** BR-16: where a signed-in user lands when there is no return URL. Technicians open Today's jobs. */
export function landingPath(roleCode: string | null | undefined): string {
  return roleCode === 'technician' ? '/today' : '/overview';
}
