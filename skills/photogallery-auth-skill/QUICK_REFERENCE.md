# PhotoGallery Auth Skill - Quick Reference

One-page cheat sheet for authentication implementation.

## The Three Auth Layers

| Layer | What | Where | Example |
|-------|------|-------|---------|
| **External** | Validate with OAuth provider | Controllers/Auth | GoogleTokenValidator |
| **Internal** | Store roles in our DB | User entity, claims | User.Role = Admin |
| **API** | Issue JWT tokens | JwtTokenService | `Bearer eyJhbGc...` |

## Auth Flow Diagram

```
User                    Backend               Google
  │                       │                      │
  ├─ Click "Login" ───────────────────────────→ │
  │                       │                      │
  │                       │ ← Token ────────────┤
  │                       │                      │
  │ ← Redirect w/ token ──┤                      │
  │                       │                      │
  ├─ API call + JWT ──────┤                      │
  │  Authorization: Bearer JWT                  │
  │                       │                      │
  │ ← Response ───────────┤                      │
```

## Quick Code Patterns

### 1. External Auth (OAuth Callback)

```csharp
[HttpGet("auth/google/callback")]
public async Task<IActionResult> GoogleCallback(string code)
{
    // Validate Google token
    var googleToken = await ExchangeCodeAsync(code);
    var validator = new GoogleTokenValidator(config);
    var claims = await validator.ValidateAsync(googleToken);
    
    // Create/update user in DB
    var user = await _userRepo.GetByEmailAsync(claims["email"]);
    if (user == null)
    {
        user = new User(claims["email"], claims["sub"], "google");
        user.SetRole(DetermineRole(claims["email"]));
        await _userRepo.AddAsync(user);
    }
    
    // Issue JWT
    var jwt = _tokenService.GenerateToken(user);
    
    // Return to client
    return Redirect($"/?token={jwt}");
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

### 5. Development Bypass

```json
// appsettings.Development.json
{ "DISABLE_AUTH": true }
```

```csharp
// Program.cs
if (config.GetValue<bool>("DISABLE_AUTH"))
{
    builder.Services
        .AddScheme<AuthenticationSchemeOptions, DevelopmentAuthHandler>(
            "Development", null);
}
```

Custom handler logs in testadmin@localhost automatically (no Google login).

### 6. Add New OAuth Provider

```csharp
// 1. Create validator
public class FacebookTokenValidator : IExternalTokenValidator
{
    public async Task<Dictionary<string, string>> ValidateAsync(string token)
    {
        // Verify with Facebook API
        // Return claims
    }
}

// 2. Register in Program.cs
builder.Services.AddScoped<FacebookTokenValidator>();

// 3. Update factory
public IExternalTokenValidator CreateValidator(string provider)
{
    return provider.ToLower() switch
    {
        "google" => google,
        "facebook" => facebook,  // NEW
        _ => throw new InvalidOperationException()
    };
}

// 4. Done! No changes to existing code
```

## Configuration Checklist

```json
{
  "Auth": {
    "AdminEmail": "mrdieppa@gmail.com"  // Seeded with Admin role
  },
  "Google": {
    "ClientId": "xxx.apps.googleusercontent.com",
    "ClientSecret": "xxx",
    "RedirectUri": "https://localhost:8443/auth/google/callback"
  },
  "Jwt": {
    "Secret": "at-least-32-chars-long",
    "Issuer": "PhotoGallery",
    "Audience": "PhotoGalleryClients",
    "ExpirationMinutes": 1440  // 24 hours
  }
}
```

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
| Store JWT in localStorage | Use secure httpOnly cookies if possible |
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
// Angular example
const token = localStorage.getItem('access_token');
const headers = new HttpHeaders({
  'Authorization': `Bearer ${token}`
});

this.http.get('/api/albums', { headers });
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

## Refresh Token Flow

```
1. Access token expires
2. Client sends refresh token to /auth/refresh
3. Server validates refresh token
4. Server issues new access token
5. Client uses new token for subsequent requests
6. (Optional) Rotate refresh token
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
