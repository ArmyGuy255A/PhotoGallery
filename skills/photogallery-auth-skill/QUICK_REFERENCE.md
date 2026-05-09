# PhotoGallery Auth Skill - Quick Reference

One-page cheat sheet for authentication implementation.

## The Three Auth Layers

| Layer | What | Where (sub-project) | Example |
|-------|------|---------------------|---------|
| **External** | Validate id_token from IDP | `Authentication/Classes/` | GoogleTokenValidator |
| **Internal** | Persist + role assignment | `PhotoGallery/Services/ExternalAuthService.cs` | `UserManager<User>` upsert |
| **API** | Issue + validate JWT (AppToken) | `Authentication/Classes/JwtTokenService.cs` | `Bearer eyJhbGc...` |

> The `Authentication` and `Configuration` sub-projects are **cross-cutting concerns** — see the `clean-architecture-guide` skill's *Cross-Cutting Concerns* rule. `PhotoGallery.csproj` references them; they reference nothing PhotoGallery-specific.

## Auth Flow Diagram (GIS popup)

```
Browser                                Backend                       Google
  │                                       │                             │
  ├─ click rendered Google button ───────►│                             │
  │   (google.accounts.id popup opens)    │                             │
  │                                       │                             │
  │◄────────── id_token (Google JWT) ───────────────────────────────────┤
  │                                       │                             │
  ├─ POST /api/auth/external-login ──────►│                             │
  │   { provider: "google", idToken }     │ TokenValidatorFactory       │
  │                                       │   → GoogleTokenValidator    │
  │                                       │   → ExternalAuthService     │
  │                                       │       (UserManager upsert)  │
  │                                       │   → JwtTokenService         │
  │◄───────── { idpToken, appToken } ─────┤                             │
  │                                       │                             │
  │ localStorage: idp_token + app_token   │                             │
  │                                       │                             │
  ├─ /api/* + Authorization: Bearer <appToken> ►                        │
```

**Refresh:** AppToken expired → AuthService re-POSTs stored `idp_token` to `/api/auth/external-login` → fresh AppToken. No refresh-token table needed. When `idp_token` itself expires → user re-runs the GIS popup.

## Two-Token Model

| Token        | Issued By | Stored As                | Purpose                                                                    |
| ------------ | --------- | ------------------------ | -------------------------------------------------------------------------- |
| **IdpToken** | Google    | `localStorage.idp_token` | Refresh credential — replayed to `/api/auth/external-login` for new AppToken |
| **AppToken** | Our API   | `localStorage.app_token` | `Authorization: Bearer <appToken>` on every `/api/*` request               |

When AppToken expires: re-POST stored IdpToken → new AppToken. When IdpToken expires: user re-runs the GIS popup.

## Quick Code Patterns

### 1. External Login (POST /api/auth/external-login)

```csharp
// PhotoGallery/Controllers/AuthController.cs
[HttpPost("external-login")]
[AllowAnonymous]
public async Task<IActionResult> ExternalLogin([FromBody] ExternalLoginRequest req)
{
    var validator = _validatorFactory.CreateValidator(req.Provider);   // Authentication sub-project
    var idpInfo   = await validator.ValidateAsync(req.IdToken);        // IdpTokenInfo
    var user      = await _externalAuthService.GetOrCreateUserAsync(idpInfo);
    var appToken  = _jwtTokenService.GenerateTokenForUser(user);
    return Ok(new ExternalLoginResponse { IdpToken = req.IdToken, AppToken = appToken });
}
```

```typescript
// services/auth/auth.service.ts — frontend side
async exchange(provider: string, idToken: string) {
  const res = await firstValueFrom(this.http.post<ExternalLoginResponse>(
    '/api/auth/external-login', { provider, idToken }));
  localStorage.setItem('idp_token', res.idpToken);
  localStorage.setItem('app_token', res.appToken);
}
```

### 2. JWT Token Generation

```csharp
var claims = new[]
{
    new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
    new Claim(ClaimTypes.Email, user.Email),
    new Claim(ClaimTypes.Role, user.Role.ToString()),
};

var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

var token = new JwtSecurityToken(
    issuer: issuer,
    audience: audience,
    claims: claims,
    expires: DateTime.UtcNow.AddHours(24),
    signingCredentials: creds);

return new JwtSecurityTokenHandler().WriteToken(token);
```

### 3. Role-Based Authorization

```csharp
// Admin only
[Authorize(Roles = "Admin")]
[HttpPost("albums")]
public async Task<IActionResult> CreateAlbum(CreateAlbumRequest req) { }

// Authenticated users
[Authorize]
[HttpGet("albums")]
public async Task<IActionResult> ListAlbums() { }

// Unauthenticated visitors
[AllowAnonymous]
[HttpGet("code/{code}/photos")]
public async Task<IActionResult> GetPhotosByCode(string code) { }
```

### 4. Access Codes (Visitor Access)

```csharp
// Generate code: 30-day expiration by default
var code = new AccessCode(albumId, GenerateRandomCode(), 
    DateTime.UtcNow.AddDays(30));

// Validate code: check expiration
[AllowAnonymous]
[HttpGet("code/{code}/album")]
public async Task<IActionResult> GetAlbumByCode(string code)
{
    var accessCode = await _codeRepo.GetByCodeAsync(code);
    if (accessCode == null || accessCode.IsExpired)
        return Unauthorized("Invalid or expired code");
    
    return Ok(album);
}
```

### 5. DISABLE_AUTH (Test environment only — Playwright)

```jsonc
// appsettings.Test.json — Development still uses real Google login
{ "DisableAuth": true }
```

`AddAuthenticationServices(settings)` (in `Authentication/DependencyInjection.cs`) reads `settings.DisableAuth` and registers `DevelopmentAuthHandler` instead of JwtBearer. The handler logs in `testadmin@localhost` automatically.

### 6. Add a New IDP (4-step recipe)

The sub-project + provider-Map design means **zero changes** to existing code. Only 4 additions.

**Backend** (inside `Authentication/`):

```csharp
// 1. New validator: Authentication/Classes/MicrosoftTokenValidator.cs
public class MicrosoftTokenValidator : IExternalTokenValidator
{
    public async Task<IdpTokenInfo> ValidateAsync(string token) { /* validate id_token */ }
}

// 2. One switch case in Authentication/Classes/TokenValidatorFactory.cs
return provider switch
{
    ExternalAuthProvider.Google    => _sp.GetRequiredService<GoogleTokenValidator>(),
    ExternalAuthProvider.Microsoft => _sp.GetRequiredService<MicrosoftTokenValidator>(),  // NEW
    _ => throw new NotSupportedException()
};
// Also register the new validator in AddAuthenticationServices.
```

**Frontend** (inside `services/auth/`):

```typescript
// 3. New provider: services/auth/providers/microsoft-auth.service.ts
@Injectable({ providedIn: 'root' })
export class MicrosoftAuthService implements IdentityProvider { /* MSAL popup wrapper */ }

// 4. One Map entry in services/auth/auth.service.ts
private readonly providers = new Map<IdentityProviderType, IdentityProvider>([
  [IdentityProviderType.Google,    inject(GoogleAuthService)],
  [IdentityProviderType.Microsoft, inject(MicrosoftAuthService)],   // NEW
]);
```

> If a step touches `AuthController`, `ExternalAuthService`, `JwtTokenService`, `Program.cs`, or `LoginComponent` — you've broken the abstraction. Stop and reconsider.

## Configuration Checklist

Bind a typed POCO from the **Configuration** sub-project — never read `IConfiguration["..."]` directly in code.

```csharp
// Configuration/ConfigurationSettings.cs (typed, no magic strings at call sites)
public sealed class ConfigurationSettings
{
    public bool             DisableAuth { get; set; }
    public AuthSettings     Auth        { get; set; } = new();
    public GoogleSettings   Google      { get; set; } = new();
    public JwtSettings      Jwt         { get; set; } = new();
}
public sealed class AuthSettings   { public string AdminEmail { get; set; } = ""; }
public sealed class GoogleSettings { public string ClientId   { get; set; } = ""; }
public sealed class JwtSettings
{
    public string Secret   { get; set; } = "";
    public string Issuer   { get; set; } = "";
    public string Audience { get; set; } = "";
    public int    ExpirationMinutes { get; set; } = 1440;
}
```

```csharp
// Program.cs — pure composition, no inline AddJwtBearer chain
var settings = builder.Services.AddConfigurationServices(builder.Configuration);
builder.Services.AddAuthenticationServices(settings);
```

```jsonc
// appsettings.json
{
  "DisableAuth": false,
  "Auth":   { "AdminEmail": "mrdieppa@gmail.com" },
  "Google": { "ClientId":   "xxx.apps.googleusercontent.com" },
  "Jwt": {
    "Secret":            "at-least-32-chars-via-user-secrets-or-keyvault",
    "Issuer":            "PhotoGallery",
    "Audience":          "PhotoGalleryClients",
    "ExpirationMinutes": 1440
  }
}
```

> No `Google:ClientSecret` and no `Google:RedirectUri` — the GIS popup flow is **client-initiated**. The backend only needs the public `ClientId` (used as the validator's `Audience`).

## User Entity

```csharp
public class User : Entity
{
    public string Email { get; private set; }           // Unique
    public string ExternalId { get; private set; }      // From OAuth
    public string ExternalProvider { get; private set; } // "google", "facebook"
    public UserRole Role { get; private set; }          // Admin or User
    public DateTime LastLoginAt { get; private set; }
    
    public void SetRole(UserRole role) => Role = role;
    public void UpdateLastLogin() => LastLoginAt = DateTime.UtcNow;
}

public enum UserRole { Admin = 1, User = 2, Visitor = 3 }
```

## JWT Token Structure

```
eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.
eyJzdWIiOiIxMjMiLCJlbWFpbCI6InVzZXJAZ21haWwuY29tIiwicm9sZSI6IkFkbWluIiwiZXhwIjoxMjM0NTY3ODkwfQ.
SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c

[Header]        [Payload]                           [Signature]
{alg,typ}      {sub,email,role,exp}                HMACSHA256(header.payload, secret)
```

## Testing Auth

```csharp
// Mock validator
var validatorMock = new Mock<IExternalTokenValidator>();
validatorMock.Setup(x => x.ValidateAsync(It.IsAny<string>()))
    .ReturnsAsync(new Dictionary<string, string>
    {
        { "sub", "google-123" },
        { "email", "test@gmail.com" }
    });

// Test: new user creation
var user = await authService.AuthenticateExternalAsync("google", "token");
Assert.IsNotNull(user);
Assert.AreEqual(UserRole.User, user.Role);
```

## Common Mistakes to Avoid

| ❌ Mistake | ✅ Correct |
|-----------|-----------|
| Hardcode OAuth secrets | Use configuration (appsettings) |
| Store passwords | OAuth only, never passwords |
| Forget token expiration | Set to 24h for access, 7d for refresh |
| Allow unlimited role changes | Only admin can change roles (or not at all) |
| Log sensitive tokens | Don't log JWTs or OAuth tokens |
| Forget to validate audience | Check audience in token validation |
| Store JWT in localStorage | Acceptable here (XSS mitigated by CSP); use httpOnly cookies if your threat model requires it |
| Inline `AddAuthentication().AddJwtBearer(...)` in Program.cs | Use `AddAuthenticationServices(settings)` from the Authentication sub-project |
| Read `IConfiguration["Jwt:Secret"]` at call sites | Inject typed `ConfigurationSettings` / `JwtSettings` from the Configuration sub-project |
| Hand-roll a "Sign in with Google" button | Render Google's branded button via `authService.renderProviderButton(...)` |
| Same secret for all environments | Use unique secret per environment |

## Claims in JWT

```csharp
// Standard claims
ClaimTypes.NameIdentifier  // User ID
ClaimTypes.Email           // Email
ClaimTypes.Role            // Role (Admin, User)

// Custom claims
"ExternalProvider"         // Which OAuth provider
"CanUploadPhotos"          // Fine-grained permission
"CanCreateAlbums"          // Fine-grained permission

// Extracted in controller
var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
var role = User.FindFirst(ClaimTypes.Role)?.Value;
var hasPermission = User.HasClaim("CanUploadPhotos", "true");
```

## API Request with JWT

```typescript
// Angular HTTP interceptor (services/auth/...)
const token = localStorage.getItem('app_token');
req = req.clone({ setHeaders: { Authorization: `Bearer ${token}` } });

// Login is NEVER `window.location.href = '/auth/google'`. That's the old server-driven
// flow. Instead, render Google's branded button:
this.authService.renderProviderButton(IdentityProviderType.Google, 'google-signin-container');
```

```csharp
// Server extracts claim from JWT
[Authorize(Roles = "Admin")]
[HttpPost("albums")]
public async Task<IActionResult> CreateAlbum()
{
    var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    // User is already authenticated and authorized
}
```

## Refresh Flow (no refresh-token table)

```
1. AppToken expires
2. AuthService re-POSTs stored idp_token to /api/auth/external-login
3. Backend re-validates idp_token via TokenValidatorFactory
4. Backend issues fresh AppToken
5. localStorage.app_token updated
6. (When idp_token itself expires) → user re-runs GIS popup
```

## Access Code Validation Flow

```
1. Admin generates access code for album (expires in 30 days)
2. Code sent to client (e.g., mrdieppa-12345)
3. Client uses code: GET /code/mrdieppa-12345/photos
4. Server validates:
   - Code exists
   - Code not expired
   - Album exists
5. Return photos without authentication
```

---

For complete code examples and implementation guidance, see **SKILL.md**.
