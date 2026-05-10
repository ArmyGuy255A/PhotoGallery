import { AuthService } from './auth.service';

/**
 * Focused unit tests for AuthService.extractRoles — the helper that decides
 * how a decoded AppToken JWT payload maps onto User.roles. Covers the three
 * shapes produced over the project's history:
 *   - short-form ``role`` (current backend, post-PR-A)
 *   - plural ``roles`` (some external IdPs)
 *   - legacy long-URI ``http://schemas.microsoft.com/ws/2008/06/identity/claims/role``
 *     (pre-PR-A admin tokens still in localStorage)
 */
describe('AuthService.extractRoles', () => {
  const longUri = 'http://schemas.microsoft.com/ws/2008/06/identity/claims/role';

  it('returns ["Admin"] for short-form `role` string claim', () => {
    expect(AuthService.extractRoles({ role: 'Admin' })).toEqual(['Admin']);
  });

  it('returns roles from short-form `role` array claim', () => {
    expect(AuthService.extractRoles({ role: ['Admin', 'User'] })).toEqual(['Admin', 'User']);
  });

  it('returns ["Admin"] for plural `roles` string claim', () => {
    expect(AuthService.extractRoles({ roles: 'Admin' })).toEqual(['Admin']);
  });

  it('returns roles from plural `roles` array claim', () => {
    expect(AuthService.extractRoles({ roles: ['Admin', 'User'] })).toEqual(['Admin', 'User']);
  });

  it('returns ["Admin"] for legacy long-URI ClaimTypes.Role string claim', () => {
    expect(AuthService.extractRoles({ [longUri]: 'Admin' })).toEqual(['Admin']);
  });

  it('returns roles from legacy long-URI ClaimTypes.Role array claim', () => {
    expect(AuthService.extractRoles({ [longUri]: ['Admin', 'User'] })).toEqual(['Admin', 'User']);
  });

  it('prefers short `role` when both short and long URI are present', () => {
    // Defensive: should a token ever carry both, the canonical short form wins.
    const decoded = { role: 'Admin', [longUri]: 'User' };
    expect(AuthService.extractRoles(decoded)).toEqual(['Admin']);
  });

  it('returns [] for a payload with no role claim', () => {
    expect(AuthService.extractRoles({ sub: 'user-123', email: 'u@x.com' })).toEqual([]);
  });

  it('returns [] for null/undefined payloads', () => {
    expect(AuthService.extractRoles(null)).toEqual([]);
    expect(AuthService.extractRoles(undefined)).toEqual([]);
  });

  it('filters non-string entries out of an array claim', () => {
    expect(AuthService.extractRoles({ role: ['Admin', 42, null, 'User'] as unknown[] } as any))
      .toEqual(['Admin', 'User']);
  });
});
