---
name: photogallery-auth
description: |
  Authentication and Authorization expert for PhotoGallery. Covers JWT token management, role-based access control (RBAC), OAuth provider integration via Google Identity Services (GIS) popup, claims-based authorization, the two-token model (IdpToken + AppToken), token refresh strategies, and the DISABLE_AUTH bypass for the Test environment. Use this whenever designing auth services, implementing the GIS popup flow, managing JWT tokens, adding a new IDP (Microsoft, Facebook, Apple), handling user roles, or creating auth bypass for Playwright. Explains how the backend lives in `Authentication/Authentication.csproj` (a cross-cutting sub-project), how the frontend `services/auth/` folder is laid out, and how new providers plug in via `IExternalTokenValidator` (backend) + `IdentityProvider` (frontend) without modifying existing code.

  This skill delegates to copilot-dev-team plugin meta-skills: `identity-and-jwt` (JWT issuance/validation), `app-jwt-claims` (claim shape — Administrator/User roles, refresh policy), `identity-providers-recipe` (Google + EntraID + KeyCloak wiring), `aspnet-identity-custom-provider` (ASP.NET Identity custom store), `keycloak-local-dev` (local KeyCloak), and `secret-hygiene` (every secret-touching step). Auto-trigger these when their conditions match. Plugin meta-skills are canonical — prefer them on conflict.

  Companion skill: `clean-architecture-guide` — its **Cross-Cutting Concerns** rule is why Authentication and Configuration each get their own sub-project rather than living inside the web app. Consult it whenever you're tempted to inline auth code into PhotoGallery.csproj.
---

# PhotoGallery Authentication & Authorization Skill

## When to Use This Skill

Use this skill when you are:

- Adding or modifying any backend code under `Authentication/` or `Configuration/` sub-projects
- Touching the frontend `services/auth/` folder, `LoginComponent`, or anything that consumes JWT
- Wiring a new IDP (Microsoft, Facebook, Apple, Entra)
- Designing the JWT claim shape, token lifetimes, or the refresh strategy
- Implementing or fixing the `POST /api/auth/external-login` endpoint
- Configuring `DISABLE_AUTH` for the Test environment / Playwright
- Reviewing a PR that registers auth services in `Program.cs` (look for `AddAuthenticationServices` / `AddConfigurationServices` rather than inline `AddAuthentication().AddJwtBearer(...)` chains)

## Overview

PhotoGallery's authentication system has three components:

1. **External Authentication** - OAuth via Google Identity Services (GIS) popup; provider-agnostic abstraction (`IExternalTokenValidator` backend, `IdentityProvider` frontend)
2. **Internal Authorization** - Role-based access control (Admin, User, Visitor)
3. **API Token Management** - JWT (the **AppToken**) for stateless API calls after external login

**Key Design:** The frontend obtains an IDP id_token via the provider's client SDK (e.g. `google.accounts.id` popup), POSTs it to `/api/auth/external-login`, and the backend validates it, upserts a `User`, and returns the AppToken. This approach:

- ✅ Supports multiple OAuth providers without modifying existing code (open/closed)
- ✅ Lives in dedicated cross-cutting sub-projects (`Authentication`, `Configuration`) — see the **clean-architecture-guide** skill's *Cross-Cutting Concerns* rule
- ✅ Stores user roles in our database (we control authorization)
- ✅ Issues JWT tokens for stateless API authentication
- ✅ Allows DISABLE_AUTH bypass for the **Test** environment (Playwright). Real Google login is used in **Development**.

## Project Structure (Backend Sub-Projects)

The **clean-architecture-guide** skill mandates that cross-cutting concerns live in their own projects so they can be reused, tested, and replaced without touching the web app. PhotoGallery applies that rule:

```
src/
├── Authentication/                    ← cross-cutting concern (own .csproj)
│   ├── Authentication.csproj
│   ├── Interfaces/
│   │   └── IExternalTokenValidator.cs
│   ├── Classes/
│   │   ├── GoogleTokenValidator.cs
│   │   ├── TokenValidatorFactory.cs
│   │   ├── JwtTokenService.cs
│   │   ├── JwtHelper.cs
│   │   ├── IdpTokenInfo.cs
│   │   ├── ExternalUserInfo.cs
│   │   └── ExternalAuthProvider.cs    (enum)
│   └── DependencyInjection.cs         ← AddAuthenticationServices(settings)
│
├── Configuration/                     ← cross-cutting concern (own .csproj)
│   ├── Configuration.csproj
│   ├── ConfigurationSettings.cs       ← typed POCO bound from appsettings
│   └── DependencyInjection.cs         ← AddConfigurationServices(IConfiguration)
│
└── PhotoGallery/                      ← web app (references both)
    ├── PhotoGallery.csproj            ← <ProjectReference Include=".../Authentication.csproj"/>
    ├── Program.cs                     ← calls AddConfigurationServices + AddAuthenticationServices
    ├── Controllers/
    │   └── AuthController.cs          ← POST /api/auth/external-login
    └── Services/
        └── ExternalAuthService.cs     ← uses UserManager<User>; bridges Authentication ↔ domain User
```

> **Why is `ExternalAuthService` in PhotoGallery and not in `Authentication/`?** It depends on `UserManager<User>` and the `User` domain entity, which are PhotoGallery-specific. The `Authentication` sub-project must remain free of domain coupling so it stays reusable. `ExternalAuthService` is the *glue* between the cross-cutting validators and the domain.

**One-way dependency** is compile-enforced: `PhotoGallery → Authentication → (nothing PhotoGallery-specific)`. You cannot accidentally introduce a back-reference because `Authentication.csproj` does not reference `PhotoGallery.csproj`.

## Plugin Meta-Skills

Authentication has many moving parts; the `copilot-dev-team` plugin breaks them into focused meta-skills. This skill stays PhotoGallery-specific (which IDPs we use, which roles, where session state lives); it defers to the plugin meta-skills for the underlying mechanics.

| Phase / situation | MUST consult | Consider |
| --- | --- | --- |
| JWT issuance / validation / refresh | `identity-and-jwt` | — |
| Defining/parsing JWT claim shape | `app-jwt-claims` | — |
| Wiring Google / EntraID / KeyCloak as IDP | `identity-providers-recipe` | — |
| Custom ASP.NET Identity user/role store | `aspnet-identity-custom-provider` | — |
| Local KeyCloak for development | `keycloak-local-dev` | — |
| Any step writing secrets / connection strings / signing keys | `secret-hygiene` | — |

## The Three Auth Layers

### 1. External Authentication (OAuth Providers)

**Purpose:** Validate user identity with third-party provider (Google, Facebook, etc.)

**Providers PhotoGallery supports:**
- Google OAuth (primary, configured in appsettings)
- Facebook OAuth (future)
- Microsoft OAuth (future)

**Key Concept:** We never validate passwords. Users authenticate with their provider account.

#### How External Auth Works (GIS Popup Flow)

```text
Browser                                    Backend                       Google
   │                                          │                             │
   ├─ user clicks "Sign in with Google" ─────►│                             │
   │   (rendered via google.accounts.id)      │                             │
   │                                          │                             │
   ├─ google.accounts.id popup ────────────────────────────────────────────►│
   │                                          │                             │
   │◄────────────────────── id_token (JWT signed by Google) ────────────────┤
   │                                          │                             │
   ├─ POST /api/auth/external-login ─────────►│                             │
   │   { provider: "google", idToken: "…" }   │                             │
   │                                          │ TokenValidatorFactory       │
   │                                          │   → GoogleTokenValidator    │
   │                                          │   → ExternalAuthService     │
   │                                          │       (UserManager upsert)  │
   │                                          │   → JwtTokenService         │
   │◄─────────── { idpToken, appToken } ──────┤                             │
   │                                          │                             │
   │ localStorage: idpToken + appToken        │                             │
   │                                          │                             │
   ├─ subsequent /api/* calls + Bearer appToken ►│                          │
```

```csharp
// PhotoGallery/Controllers/AuthController.cs
[HttpPost("external-login")]
[AllowAnonymous]
public async Task<IActionResult> ExternalLogin([FromBody] ExternalLoginRequest req)
{
    // 1. Validate the IDP token via the provider-agnostic factory
    var validator = _validatorFactory.CreateValidator(req.Provider);  // Authentication sub-project
    var idpInfo   = await validator.ValidateAsync(req.IdToken);       // returns IdpTokenInfo

    // 2. Upsert the User (this lives in PhotoGallery — it touches the domain)
    var user = await _externalAuthService.GetOrCreateUserAsync(idpInfo);

    // 3. Issue our AppToken (JWT) — Authentication sub-project
    var appToken = _jwtTokenService.GenerateTokenForUser(user);

    return Ok(new ExternalLoginResponse
    {
        IdpToken = req.IdToken,   // echoed back for refresh-by-replay
        AppToken = appToken
    });
}
```

### 2. Internal Authorization (Role-Based Access Control)

**Purpose:** Control what authenticated users can do based on their role

**PhotoGallery Roles:**
- **Admin** - Can create/edit/delete albums, upload photos, generate access codes
- **User** - Can view own albums, generate access codes (future)
- **Visitor** - No authenticated actions (see below for Visitor)

**Key Concept:** Roles are stored in our database, determined at login, added as claims to JWT token.

→ **Consult:** `app-jwt-claims` for claim shape definition and role parsing strategies.

#### How Role-Based Access Works

```csharp
// In User entity - set role at creation or update
public class User : Entity
{
    public string Email { get; private set; }
    public string ExternalId { get; private set; }
    public string ExternalProvider { get; private set; }  // "google", "facebook", etc.
    public UserRole Role { get; private set; }
    
    public User(string email, string externalId, string provider)
    {
        Email = email;
        ExternalId = externalId;
        ExternalProvider = provider;
        Role = UserRole.User;  // Default
    }
    
    public void SetRole(UserRole role)
    {
        Role = role;
    }
}

public enum UserRole
{
    Admin = 1,
    User = 2,
    Visitor = 3  // Not used for authenticated users
}

// In JWT token generation - include role as claim
public class JwtTokenService
{
    private readonly IConfiguration _config;
    
    public JwtTokenService(IConfiguration config)
    {
        _config = config;
    }
    
    public string GenerateToken(User user)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role.ToString()),  // Admin or User
        };
        
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Secret"]));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        
        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"],
            audience: _config["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddHours(24),
            signingCredentials: creds);
        
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

// In controllers - use [Authorize(Roles="Admin")] for access control
[HttpPost("albums")]
[Authorize(Roles = "Admin")]  // Only Admin role
public async Task<IActionResult> CreateAlbum(CreateAlbumRequest request)
{
    var album = new Album(request.Title, request.Description);
    await _albumRepository.AddAsync(album);
    await _albumRepository.SaveChangesAsync();
    return Ok(new { album.Id });
}

[HttpGet("albums")]
[Authorize(Roles = "Admin,User")]  // Admin or User
public async Task<IActionResult> ListAlbums()
{
    var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    var albums = await _albumRepository.GetUserAlbumsAsync(int.Parse(userId));
    return Ok(albums);
}
```

### 3. API Token Management (JWT)

**Purpose:** Stateless authentication for API calls after OAuth login

**Flow:**
1. User logs in with OAuth → we issue JWT token
2. Browser stores JWT in localStorage
3. Browser includes JWT in Authorization header for API calls
4. API validates JWT and extracts claims

→ **Consult:** `identity-and-jwt` for JWT issuance, validation, token lifetime, and refresh strategies.

#### JWT Token Structure

```
Header:   {"alg":"HS256","typ":"JWT"}
Payload:  {"sub":"123", "email":"user@gmail.com", "role":"Admin", "exp":1234567890}
Signature: HMACSHA256(header.payload, secret_key)
```

#### Generating Tokens

```csharp
public class JwtTokenService : ITokenService
{
    private readonly IConfiguration _config;
    
    public JwtTokenService(IConfiguration config)
    {
        _config = config;
    }
    
    public string GenerateToken(User user)
    {
        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(_config["Jwt:Secret"]));
        
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role.ToString()),
            new Claim("ExternalProvider", user.ExternalProvider),
        };
        
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        
        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"],
            audience: _config["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddHours(24),
            signingCredentials: creds);
        
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
    
    public string GenerateRefreshToken()
    {
        var randomNumber = new byte[64];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(randomNumber);
            return Convert.ToBase64String(randomNumber);
        }
    }
}

// Configure JWT validation in Program.cs — DO NOT inline AddAuthentication().AddJwtBearer(...)
// here. The Authentication sub-project owns that wiring; Program.cs just composes:
//
//   var settings = builder.Services.AddConfigurationServices(builder.Configuration);
//   builder.Services.AddAuthenticationServices(settings);
//
// AddAuthenticationServices internally:
//   - registers IExternalTokenValidator implementations + TokenValidatorFactory
//   - registers JwtTokenService / JwtHelper
//   - calls AddAuthentication(...).AddJwtBearer(...) using ConfigurationSettings.Jwt
//   - in Test env (DISABLE_AUTH=true) swaps in the development scheme instead
//
// See: Authentication/DependencyInjection.cs
```

#### Token Refresh Strategy

```csharp
// Store refresh token in database
public class RefreshToken : Entity
{
    public int UserId { get; set; }
    public string Token { get; set; }
    public DateTime ExpiresAt { get; set; }
    public bool IsRevoked { get; set; }
}

// Endpoint to refresh token
[HttpPost("auth/refresh")]
public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenRequest request)
{
    var refreshToken = await _refreshTokenRepository.GetAsync(request.RefreshToken);
    
    if (refreshToken == null || refreshToken.IsRevoked || refreshToken.ExpiresAt < DateTime.UtcNow)
        return Unauthorized("Invalid refresh token");
    
    var user = await _userRepository.GetByIdAsync(refreshToken.UserId);
    var newJwtToken = _tokenService.GenerateToken(user);
    
    // Optionally: rotate refresh token
    var newRefreshToken = _tokenService.GenerateRefreshToken();
    refreshToken.Token = newRefreshToken;
    refreshToken.ExpiresAt = DateTime.UtcNow.AddDays(7);
    
    await _refreshTokenRepository.UpdateAsync(refreshToken);
    
    return Ok(new
    {
        accessToken = newJwtToken,
        refreshToken = newRefreshToken,
        expiresIn = 3600  // 1 hour in seconds
    });
}
```

## OAuth Provider Integration (Extensible)

**Design:** Each OAuth provider is a separate class, registered via interface

**Goal:** Add Facebook or Microsoft without changing existing Google code

→ **Consult:** `identity-providers-recipe` for Google, EntraID, and KeyCloak wiring patterns.

### Provider Pattern

```csharp
// Domain interface - no dependencies
public interface IExternalTokenValidator
{
    Task<Dictionary<string, string>> ValidateAsync(string token);
}

// Google implementation
public class GoogleTokenValidator : IExternalTokenValidator
{
    private readonly GoogleJsonWebSignature.ValidationSettings _settings;
    
    public GoogleTokenValidator(IConfiguration config)
    {
        _settings = new GoogleJsonWebSignature.ValidationSettings
        {
            Audience = new[] { config["Google:ClientId"] }
        };
    }
    
    public async Task<Dictionary<string, string>> ValidateAsync(string token)
    {
        try
        {
            var payload = await GoogleJsonWebSignature.ValidateAsync(token, _settings);
            return new Dictionary<string, string>
            {
                ["sub"] = payload.Subject,
                ["email"] = payload.Email,
                ["name"] = payload.Name,
                ["picture"] = payload.Picture,
            };
        }
        catch
        {
            throw new InvalidOperationException("Invalid Google token");
        }
    }
}

// Facebook implementation (future)
public class FacebookTokenValidator : IExternalTokenValidator
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _config;
    
    public FacebookTokenValidator(HttpClient httpClient, IConfiguration config)
    {
        _httpClient = httpClient;
        _config = config;
    }
    
    public async Task<Dictionary<string, string>> ValidateAsync(string token)
    {
        // Verify token with Facebook API
        var response = await _httpClient.GetAsync(
            $"https://graph.facebook.com/debug_token?input_token={token}&access_token={_config["Facebook:AppToken"]}");
        
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("Invalid Facebook token");
        
        var content = await response.Content.ReadAsStringAsync();
        var data = JsonDocument.Parse(content).RootElement;
        
        // Extract user info
        var userResponse = await _httpClient.GetAsync(
            $"https://graph.facebook.com/me?access_token={token}&fields=id,email,name");
        
        var userData = JsonDocument.Parse(await userResponse.Content.ReadAsStringAsync()).RootElement;
        
        return new Dictionary<string, string>
        {
            ["sub"] = userData.GetProperty("id").GetString(),
            ["email"] = userData.GetProperty("email").GetString(),
            ["name"] = userData.GetProperty("name").GetString(),
        };
    }
}

// Factory for creating validators
public class TokenValidatorFactory
{
    private readonly IServiceProvider _serviceProvider;
    
    public TokenValidatorFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }
    
    public IExternalTokenValidator CreateValidator(string provider)
    {
        return provider.ToLower() switch
        {
            "google" => _serviceProvider.GetRequiredService<GoogleTokenValidator>(),
            "facebook" => _serviceProvider.GetRequiredService<FacebookTokenValidator>(),
            _ => throw new InvalidOperationException($"Unknown provider: {provider}")
        };
    }
}

// Register inside Authentication/DependencyInjection.cs's AddAuthenticationServices(...)
// (NOT in PhotoGallery's Program.cs — that's the whole point of the sub-project)
services.AddScoped<GoogleTokenValidator>();
services.AddScoped<FacebookTokenValidator>();
services.AddScoped<TokenValidatorFactory>();
```

## Access Code Authentication (Visitor Access)

**Purpose:** Allow unauthenticated visitors to access photos with time-limited access code

**Pattern:** Access code is like a one-time password + expiration

```csharp
// Domain entity
public class AccessCode : Entity
{
    public int AlbumId { get; private set; }
    public string Code { get; private set; }
    public DateTime CreatedDate { get; private set; }
    public DateTime? ExpiresAt { get; private set; }
    
    public AccessCode(int albumId, string code, DateTime? expiresAt = null)
    {
        AlbumId = albumId;
        Code = code;
        CreatedDate = DateTime.UtcNow;
        ExpiresAt = expiresAt ?? DateTime.UtcNow.AddDays(30);  // Default 30 days
    }
    
    public bool IsExpired => ExpiresAt != null && DateTime.UtcNow > ExpiresAt;
    
    public bool IsValid => !IsExpired;
}

// Unauthenticated endpoint for visitors
[HttpGet("code/{accessCode}/album")]
[AllowAnonymous]
public async Task<IActionResult> GetAlbumByAccessCode(string accessCode)
{
    var code = await _accessCodeRepository.GetByCodeAsync(accessCode);
    
    if (code == null || code.IsExpired)
        return NotFound("Invalid or expired access code");
    
    var album = await _albumRepository.GetByIdAsync(code.AlbumId);
    return Ok(new AlbumResponseDto(album));
}

[HttpGet("code/{accessCode}/photos")]
[AllowAnonymous]
public async Task<IActionResult> GetPhotosByAccessCode(string accessCode)
{
    var code = await _accessCodeRepository.GetByCodeAsync(accessCode);
    
    if (code == null || code.IsExpired)
        return Unauthorized("Invalid or expired access code");
    
    var photos = await _photoRepository.GetByAlbumIdAsync(code.AlbumId);
    return Ok(photos.Select(p => new PhotoResponseDto(p)));
}
```

## Authentication Bypass for the Test Environment

**Problem:** Playwright e2e tests can't drive the real Google popup. We need a deterministic test admin.

**Solution:** `DISABLE_AUTH=true` in the **Test** environment (`appsettings.Test.json`) tells `AddAuthenticationServices` to register a `DevelopmentAuthHandler` instead of JWT bearer. **Development uses real Google login** — only Test bypasses.

```csharp
// Authentication/DependencyInjection.cs (sketch)
public static IServiceCollection AddAuthenticationServices(
    this IServiceCollection services,
    ConfigurationSettings settings)
{
    services.AddScoped<GoogleTokenValidator>();
    services.AddScoped<TokenValidatorFactory>();
    services.AddScoped<JwtTokenService>();

    if (settings.DisableAuth)
    {
        services.AddAuthentication("Development")
                .AddScheme<AuthenticationSchemeOptions, DevelopmentAuthHandler>("Development", null);
    }
    else
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(opts =>
                {
                    opts.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey            = new SymmetricSecurityKey(
                            Encoding.UTF8.GetBytes(settings.Jwt.Secret)),  // → secret-hygiene
                        ValidateIssuer    = true, ValidIssuer   = settings.Jwt.Issuer,
                        ValidateAudience  = true, ValidAudience = settings.Jwt.Audience,
                        ValidateLifetime  = true,
                        ClockSkew         = TimeSpan.Zero,
                    };
                });
    }
    return services;
}
```

```csharp
// Custom auth handler used only when DISABLE_AUTH=true (Test env / Playwright)
public class DevelopmentAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly UserManager<User> _userManager;

    public DevelopmentAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        UserManager<User> userManager)
        : base(options, logger, encoder)
    {
        _userManager = userManager;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var testUser = await _userManager.FindByEmailAsync("testadmin@localhost")
                       ?? await CreateTestAdminAsync();

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, testUser.Id.ToString()),
            new Claim(ClaimTypes.Email, testUser.Email!),
            new Claim(ClaimTypes.Role, "Admin"),
        };

        var identity  = new ClaimsIdentity(claims, "Development");
        var principal = new ClaimsPrincipal(identity);
        var ticket    = new AuthenticationTicket(principal, "Development");
        return AuthenticateResult.Success(ticket);
    }
}
```

**appsettings.Test.json** (Playwright runs against this profile):
```json
{
  "DisableAuth": true,
  "Jwt": {
    "Secret": "test-secret-key-at-least-32-characters-for-hmacsha256",
    "Issuer": "PhotoGallery",
    "Audience": "PhotoGalleryClients",
    "ExpirationMinutes": 1440
  }
}
```

> **Development uses real Google.** The dev workflow is: configure `Google:ClientId` in user-secrets, run the app, click the GIS button, sign in. There is no `DisableAuth` in `appsettings.Development.json`.

## Claims-Based Authorization (Advanced)

Beyond simple roles, you can use fine-grained claims for more control

```csharp
// Add custom claims to token
var claims = new[]
{
    new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
    new Claim(ClaimTypes.Email, user.Email),
    new Claim(ClaimTypes.Role, user.Role.ToString()),
    new Claim("CanUploadPhotos", user.Role == UserRole.Admin ? "true" : "false"),
    new Claim("CanCreateAlbums", user.Role == UserRole.Admin ? "true" : "false"),
};

// Check claims in code
[HttpPost("albums/{id}/photos")]
[Authorize]
public async Task<IActionResult> UploadPhotos(int id)
{
    if (!User.HasClaim("CanUploadPhotos", "true"))
        return Forbid("You don't have permission to upload photos");
    
    // Process upload...
}

// Or use policy-based authorization
// In Program.cs:
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("CanUploadPhotos", policy =>
        policy.RequireClaim("CanUploadPhotos", "true"))
    .AddPolicy("CanCreateAlbums", policy =>
        policy.RequireRole("Admin"));

// In controller:
[Authorize(Policy = "CanUploadPhotos")]
public async Task<IActionResult> UploadPhotos(int id)
{
    // Process upload...
}
```

## User Persistence with Entity Framework

→ **Consult:** `aspnet-identity-custom-provider` if customizing user/role store beyond standard EF Core mapping.

**User Entity:**
```csharp
public class User : Entity
{
    public string Email { get; private set; }
    public string ExternalId { get; private set; }
    public string ExternalProvider { get; private set; }  // "google", "facebook", etc.
    public UserRole Role { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }
    public DateTime? LastLoginAt { get; private set; }
    
    // Collections
    private List<RefreshToken> _refreshTokens = new();
    public IReadOnlyList<RefreshToken> RefreshTokens => _refreshTokens.AsReadOnly();
    
    public User(string email, string externalId, string provider)
    {
        Email = email;
        ExternalId = externalId;
        ExternalProvider = provider;
        Role = UserRole.User;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }
    
    public void SetRole(UserRole role)
    {
        Role = role;
        UpdatedAt = DateTime.UtcNow;
    }
    
    public void UpdateLastLogin()
    {
        LastLoginAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }
    
    public void AddRefreshToken(string token, DateTime expiresAt)
    {
        _refreshTokens.Add(new RefreshToken
        {
            Token = token,
            ExpiresAt = expiresAt,
            IsRevoked = false,
        });
    }
}

// Entity Configuration
public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.HasKey(x => x.Id);
        
        builder.Property(x => x.Email)
            .IsRequired()
            .HasMaxLength(255);
        
        builder.Property(x => x.ExternalId)
            .IsRequired()
            .HasMaxLength(255);
        
        builder.Property(x => x.ExternalProvider)
            .IsRequired()
            .HasMaxLength(50);
        
        builder.HasIndex(x => x.Email).IsUnique();
        
        builder.HasMany(x => x.RefreshTokens)
            .WithOne(x => x.User)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

// Database seeding for admin user
public static void SeedAdminUser(this ModelBuilder modelBuilder, IConfiguration config)
{
    var adminEmail = config["Auth:AdminEmail"]; // e.g., "mrdieppa@gmail.com"
    if (!string.IsNullOrEmpty(adminEmail))
    {
        var adminUser = new User(adminEmail, $"google-{Guid.NewGuid()}", "google");
        adminUser.SetRole(UserRole.Admin);
        
        modelBuilder.Entity<User>().HasData(adminUser);
    }
}
```

## Auth Service Layer

```csharp
// Domain interface
public interface IAuthService
{
    Task<User> AuthenticateExternalAsync(string provider, string token);
    Task<string> GenerateAccessTokenAsync(User user);
    Task<RefreshTokenResult> RefreshAccessTokenAsync(string refreshToken);
}

// Infrastructure implementation
public class AuthService : IAuthService
{
    private readonly IUserRepository _userRepository;
    private readonly ITokenService _tokenService;
    private readonly TokenValidatorFactory _validatorFactory;
    
    public AuthService(
        IUserRepository userRepository,
        ITokenService tokenService,
        TokenValidatorFactory validatorFactory)
    {
        _userRepository = userRepository;
        _tokenService = tokenService;
        _validatorFactory = validatorFactory;
    }
    
    public async Task<User> AuthenticateExternalAsync(string provider, string token)
    {
        // Validate token with provider
        var validator = _validatorFactory.CreateValidator(provider);
        var claims = await validator.ValidateAsync(token);
        
        var externalId = claims["sub"];
        var email = claims["email"];
        
        // Get or create user
        var user = await _userRepository.GetByEmailAsync(email);
        if (user == null)
        {
            user = new User(email, externalId, provider);
            await _userRepository.AddAsync(user);
        }
        else
        {
            user.UpdateLastLogin();
            await _userRepository.UpdateAsync(user);
        }
        
        await _userRepository.SaveChangesAsync();
        return user;
    }
    
    public async Task<string> GenerateAccessTokenAsync(User user)
    {
        return _tokenService.GenerateToken(user);
    }
    
    public async Task<RefreshTokenResult> RefreshAccessTokenAsync(string refreshToken)
    {
        // Validate and refresh
        var token = await _userRepository.GetRefreshTokenAsync(refreshToken);
        
        if (token == null || token.IsRevoked || token.ExpiresAt < DateTime.UtcNow)
            throw new InvalidOperationException("Invalid refresh token");
        
        var user = await _userRepository.GetByIdAsync(token.UserId);
        var newAccessToken = _tokenService.GenerateToken(user);
        
        return new RefreshTokenResult
        {
            AccessToken = newAccessToken,
            ExpiresIn = 3600,
        };
    }
}
```

## Configuration (typed `ConfigurationSettings`, not magic strings)

The **Configuration** sub-project owns a typed POCO bound from `appsettings.*.json`. Code never reads `IConfiguration["Jwt:Secret"]` directly — it injects `ConfigurationSettings` (or a sub-section like `JwtSettings`) and reads strongly-typed properties.

→ **Consult:** `secret-hygiene` for every secret, client credential, and signing key in this configuration.

```csharp
// Configuration/ConfigurationSettings.cs
public sealed class ConfigurationSettings
{
    public bool DisableAuth { get; set; }
    public AuthSettings     Auth     { get; set; } = new();
    public GoogleSettings   Google   { get; set; } = new();
    public JwtSettings      Jwt      { get; set; } = new();
}

public sealed class AuthSettings   { public string AdminEmail { get; set; } = ""; }
public sealed class GoogleSettings { public string ClientId { get; set; } = ""; }
public sealed class JwtSettings
{
    public string Secret   { get; set; } = "";
    public string Issuer   { get; set; } = "";
    public string Audience { get; set; } = "";
    public int    ExpirationMinutes { get; set; } = 1440;
}

// Configuration/DependencyInjection.cs
public static ConfigurationSettings AddConfigurationServices(
    this IServiceCollection services, IConfiguration config)
{
    var settings = config.Get<ConfigurationSettings>() ?? new();
    services.AddSingleton(settings);
    services.AddSingleton(settings.Jwt);
    services.AddSingleton(settings.Google);
    return settings;   // returned so Program.cs can pass it to AddAuthenticationServices
}
```

```jsonc
// appsettings.json (or env-specific overrides)
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

> Notice there is no `ClientSecret` and no `RedirectUri` for Google. The GIS popup flow is **client-initiated** — only the public ClientId is needed on the backend (to set the validator's `Audience`). The browser handles the OAuth dance with Google directly via the `google.accounts.id` SDK.

## Frontend Auth Architecture

The Angular frontend mirrors VerdantIQ's **Google Identity Services (GIS) popup** pattern. Auth concerns live in a dedicated folder so providers are pluggable and the rest of the app depends only on the abstraction.

### Folder layout

```
src/app/services/auth/
├── identity-provider.ts              ← interface IdentityProvider (provider abstraction)
├── identity-provider-type.ts         ← enum IdentityProviderType { Google, Microsoft, ... }
├── identity-user.ts                  ← typed JWT payload (sub, email, role, exp, ...)
├── auth.service.ts                   ← AuthService (two-token model + provider Map)
└── providers/
    └── google-auth.service.ts        ← GoogleAuthService implements IdentityProvider
                                        (wraps google.accounts.id, ux_mode: 'popup')
```

### index.html

```html
<!-- Loads the GIS SDK; window.google.accounts.id becomes available -->
<script src="https://accounts.google.com/gsi/client" async defer></script>
```

### The Two-Token Model

After a successful external login the frontend stores **two** tokens:

| Token        | Issuer  | Format       | Stored As (localStorage) | Used For                                                                 |
| ------------ | ------- | ------------ | ------------------------ | ------------------------------------------------------------------------ |
| **IdpToken** | Google  | Google id_token (JWT) | `idp_token`     | Refreshing the AppToken — replayed to `/api/auth/external-login`         |
| **AppToken** | Our API | JWT (HS256)  | `app_token`              | Every `/api/*` request: `Authorization: Bearer <appToken>`              |

When the AppToken expires, `AuthService` re-POSTs the **stored IdpToken** to `/api/auth/external-login` to obtain a fresh AppToken. No refresh-token table is needed on the backend; the IDP id_token *is* the refresh credential. (When the IdpToken itself expires, the user re-runs the GIS popup.)

### Provider abstraction

```typescript
// services/auth/identity-provider.ts
export interface IdentityProvider {
  initialize(): Promise<void>;
  renderButton(containerId: string): void;
  signOut(): Promise<void>;
  /** Stream of raw IDP id_tokens emitted when the user completes the popup. */
  readonly idTokens$: Observable<string>;
}
```

```typescript
// services/auth/auth.service.ts (sketch)
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly providers = new Map<IdentityProviderType, IdentityProvider>([
    [IdentityProviderType.Google, inject(GoogleAuthService)],
    // [IdentityProviderType.Microsoft, inject(MicrosoftAuthService)],   ← future
  ]);

  renderProviderButton(type: IdentityProviderType, containerId: string): void {
    this.providers.get(type)!.renderButton(containerId);
  }

  /** Called when a provider emits an id_token; exchanges it for our AppToken. */
  private async exchangeForAppToken(provider: string, idToken: string): Promise<void> {
    const res = await firstValueFrom(this.http.post<ExternalLoginResponse>(
      '/api/auth/external-login', { provider, idToken }));
    localStorage.setItem('idp_token', res.idpToken);
    localStorage.setItem('app_token', res.appToken);
  }
}
```

### LoginComponent uses the official Google button

```typescript
ngAfterViewInit(): void {
  this.authService.renderProviderButton(
    IdentityProviderType.Google,
    'google-signin-container',   // <div id="google-signin-container"></div>
  );
}
```

Google renders its own pixel-perfect, branded button into that container. Do not hand-roll a "Sign in with Google" button — Google's brand guidelines require their renderer.

## Adding a New IDP

The whole point of the sub-project + provider-Map design is that adding Microsoft / Facebook / Apple is a **4-step recipe with zero changes to existing code**.

### Backend (2 changes, both inside `Authentication/`)

1. **New validator** in `Authentication/Classes/MicrosoftTokenValidator.cs`:
   ```csharp
   public class MicrosoftTokenValidator : IExternalTokenValidator
   {
       public async Task<IdpTokenInfo> ValidateAsync(string token) { /* validate id_token */ }
   }
   ```
2. **One switch case** in `Authentication/Classes/TokenValidatorFactory.cs`:
   ```csharp
   return provider switch
   {
       ExternalAuthProvider.Google    => _sp.GetRequiredService<GoogleTokenValidator>(),
       ExternalAuthProvider.Microsoft => _sp.GetRequiredService<MicrosoftTokenValidator>(),  // NEW
       _ => throw new NotSupportedException($"Unknown provider: {provider}")
   };
   ```
   Also register the new validator in `AddAuthenticationServices`. Done. `ExternalAuthService`, `AuthController`, `JwtTokenService`, `Program.cs` — none of them change.

### Frontend (2 changes, both inside `services/auth/`)

3. **New provider** in `services/auth/providers/microsoft-auth.service.ts`:
   ```typescript
   @Injectable({ providedIn: 'root' })
   export class MicrosoftAuthService implements IdentityProvider { /* MSAL popup wrapper */ }
   ```
4. **One Map entry** in `AuthService`:
   ```typescript
   [IdentityProviderType.Microsoft, inject(MicrosoftAuthService)],   // NEW
   ```
   `LoginComponent` then calls `renderProviderButton(IdentityProviderType.Microsoft, '...')`. Nothing else changes.

> If a step requires touching `AuthController`, `ExternalAuthService`, or `JwtTokenService`, you've broken the abstraction — stop and reconsider.

## Common Patterns

### JWT in Angular (HTTP interceptor)

> The full provider/AuthService design is in **Frontend Auth Architecture** above. This section just shows how the AppToken is attached to outgoing requests.

```typescript
// services/auth/auth.service.ts — minimal token accessor
@Injectable({ providedIn: 'root' })
export class AuthService {
  getAppToken(): string | null { return localStorage.getItem('app_token'); }
  signOut(): void {
    localStorage.removeItem('idp_token');
    localStorage.removeItem('app_token');
    // Also call the active IdentityProvider's signOut() to clear GIS state.
  }
}

// HTTP interceptor — attaches the AppToken to every /api/* request
@Injectable()
export class AuthInterceptor implements HttpInterceptor {
  constructor(private auth: AuthService) {}
  intercept(req: HttpRequest<unknown>, next: HttpHandler) {
    const token = this.auth.getAppToken();
    if (token) {
      req = req.clone({ setHeaders: { Authorization: `Bearer ${token}` } });
    }
    return next.handle(req);
  }
}
```

> **Do not** redirect to a backend `/auth/{provider}` endpoint — that's the old server-driven OAuth flow PhotoGallery no longer uses. Login is initiated client-side via the Google button rendered by `AuthService.renderProviderButton(...)`.

### Role Checking in Angular

```typescript
// auth.guard.ts
@Injectable({ providedIn: 'root' })
export class AdminGuard implements CanActivate {
  constructor(
    private auth: AuthService,
    private router: Router
  ) {}
  
  canActivate(): boolean {
    const role = this.auth.getUserRole();
    if (role === 'Admin') {
      return true;
    }
    this.router.navigate(['/unauthorized']);
    return false;
  }
}

// In routing:
{
  path: 'admin',
  component: AdminComponent,
  canActivate: [AdminGuard]
}
```

## Testing Auth

```csharp
[TestClass]
public class AuthServiceTests
{
    private AuthService _authService;
    private Mock<IUserRepository> _userRepositoryMock;
    private Mock<ITokenService> _tokenServiceMock;
    private Mock<TokenValidatorFactory> _validatorFactoryMock;
    
    [TestInitialize]
    public void Setup()
    {
        _userRepositoryMock = new Mock<IUserRepository>();
        _tokenServiceMock = new Mock<ITokenService>();
        _validatorFactoryMock = new Mock<TokenValidatorFactory>();
        
        _authService = new AuthService(
            _userRepositoryMock.Object,
            _tokenServiceMock.Object,
            _validatorFactoryMock.Object);
    }
    
    [TestMethod]
    public async Task AuthenticateExternalAsync_NewUser_CreatesUserAndReturnsIt()
    {
        // Arrange
        var provider = "google";
        var token = "valid-google-token";
        var validator = new Mock<IExternalTokenValidator>();
        
        validator.Setup(x => x.ValidateAsync(token))
            .ReturnsAsync(new Dictionary<string, string>
            {
                { "sub", "google-123" },
                { "email", "user@gmail.com" }
            });
        
        _validatorFactoryMock.Setup(x => x.CreateValidator(provider))
            .Returns(validator.Object);
        
        _userRepositoryMock.Setup(x => x.GetByEmailAsync(It.IsAny<string>()))
            .ReturnsAsync((User)null);
        
        // Act
        var user = await _authService.AuthenticateExternalAsync(provider, token);
        
        // Assert
        Assert.IsNotNull(user);
        Assert.AreEqual("user@gmail.com", user.Email);
        _userRepositoryMock.Verify(x => x.AddAsync(It.IsAny<User>()), Times.Once);
    }
}
```

## Security Best Practices

✅ **Do:**
- Store JWT secret in configuration (not code)
- Use HTTPS for all auth endpoints
- Set token expiration to reasonable value (1-24 hours)
- Rotate refresh tokens on use
- Validate token signature
- Use secure random for access codes
- Hash refresh tokens if storing in DB (optional but recommended)
- Validate OAuth state parameter to prevent CSRF

❌ **Don't:**
- Store passwords (use OAuth only)
- Log sensitive tokens
- Include sensitive data in JWT
- Hard-code OAuth credentials
- Use symmetric encryption for sensitive data (use HTTPS)
- Allow unlimited token lifetime
- Forget to validate audience and issuer in JWT
- Store refresh tokens in localStorage (use secure cookies if possible)

## Checklist for Auth Implementation

- [ ] `Authentication/Authentication.csproj` exists and contains validators + JwtTokenService + `AddAuthenticationServices`
- [ ] `Configuration/Configuration.csproj` exists with typed `ConfigurationSettings` + `AddConfigurationServices`
- [ ] `PhotoGallery.csproj` has `<ProjectReference>` entries for both sub-projects
- [ ] `Program.cs` calls `AddConfigurationServices` then `AddAuthenticationServices(settings)` — **no inline `AddJwtBearer(...)` chain**
- [ ] `POST /api/auth/external-login` accepts `{ provider, idToken }` and returns `{ idpToken, appToken }`
- [ ] `ExternalAuthService` (in PhotoGallery) bridges validators ↔ `UserManager<User>`
- [ ] Frontend `services/auth/` folder exists with `identity-provider.ts`, `auth.service.ts`, `providers/google-auth.service.ts`
- [ ] `index.html` loads the GIS SDK script
- [ ] `LoginComponent` uses `renderProviderButton(IdentityProviderType.Google, '<container-id>')` — no hand-rolled Google button
- [ ] Two tokens (idp_token + app_token) stored in localStorage; HTTP interceptor sends `Authorization: Bearer <appToken>`
- [ ] Refresh path: AuthService re-POSTs the stored IdpToken to `/api/auth/external-login` when AppToken expires
- [ ] `appsettings.Test.json` sets `DisableAuth: true`; **Development uses real Google login**
- [ ] Admin user seeded with `Auth.AdminEmail`
- [ ] Access codes still work for unauthenticated visitors (`[AllowAnonymous]`)
- [ ] All secrets (Jwt:Secret, Google:ClientId via user-secrets/KeyVault) — **never committed**

---

**Key Takeaway:** PhotoGallery's auth is extensible (new providers), stateless (JWT), and testable (development bypass). Users authenticate externally (OAuth), we persist them internally, and issue tokens for API access.

## Cross-cutting plugin skills (always-on)

- `scratch-discipline` — auth probes / OIDC test apps in `.copilot/scratch/<task-id>/`.
- `secret-hygiene` — never commit signing keys, client secrets, or tokens. The `secret-scan` hook pre-checks writes.
- `commit-conventions` — canonical commit-message format.
- `branch-strategy-u-prefix` — `u/<actor>/<type>/<scope>` branches only.
- `copilot-memory-update` — record durable auth decisions (IDP set, claim shape, lifetimes).
