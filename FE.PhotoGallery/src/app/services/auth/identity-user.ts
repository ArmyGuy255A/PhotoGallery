export interface IdentityUser {
  sub: string;            // Subject (user ID)
  email: string;
  role: string[];         // Array of roles
  community: string;
  exp: number;            // Expiration time (Unix)
  iss: string;            // Issuer
  aud: string;            // Audience
}
