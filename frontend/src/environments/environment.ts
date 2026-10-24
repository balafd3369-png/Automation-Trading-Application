/** Default runtime tuning (overridden per build configuration). */
export const environment = {
  production: false,
  /** Full pressure board poll interval (milliseconds). */
  boardPollMs: 6000,
  /** Match backend SystemIdentity:RequireAuthentication for hosted friend testing. */
  requireAuth: true,
};
