# PhotoGallery Authentication Skill

A comprehensive guide to designing and implementing authentication and authorization for PhotoGallery's OAuth + JWT + Role-Based Access Control system.

## Quick Overview

PhotoGallery uses a **three-layer authentication system:**

1. **External Auth** (OAuth) - Users authenticate with Google/Facebook/Microsoft
2. **Internal Authorization** - We store roles in our database
3. **API Tokens** (JWT) - Users get a token for stateless API calls

**Key Concept:** OAuth validates who you are. Our database determines what you can do. JWT tokens enable API authentication.

## The Three Auth Layers

### Layer 1: External Authentication (OAuth)
```
User clicks "Login with Google"
    ↓
Google authenticates user (not our app)
    ↓
Google issues token to user
    ↓
We validate the Google token
    ↓
We create/update user in our database
    ↓
We issue JWT token to user
    ↓
User uses JWT for all API calls
```

**Benefit:** No passwords to manage. Extensible to Facebook, Microsoft, etc.

### Layer 2: Internal Authorization (Roles)
```
Admin can create albums, upload photos
User can view albums, generate codes
Visitor can access photos with access code
```

**Benefit:** We control permissions in our database, independent of OAuth provider.

### Layer 3: API Tokens (JWT)
```
Header:   {"alg":"HS256","typ":"JWT"}
Payload:  {"sub":"123", "email":"user@gmail.com", "role":"Admin", "exp":1234567890}
Signature: HMACSHA256(header.payload, secret_key)
```

**Benefit:** Stateless. Client stores token, includes in every request. No server session needed.

## File Organization

**Domain Layer (No Dependencies):**
```
Domain/
└── Interfaces/
    ├── IAuthService.cs          # Authenticate user, generate tokens
    ├── ITokenService.cs         # Generate/validate JWT tokens
    └── IExternalTokenValidator.cs  # Validate OAuth tokens
```

**Infrastructure Layer (Implements Domain):**
```
Infrastructure/
├── Authentication/
│   ├── GoogleTokenValidator.cs        # Validates Google tokens
│   ├── FacebookTokenValidator.cs      # Validates Facebook tokens (future)
│   ├── TokenValidatorFactory.cs       # Creates appropriate validator
│   ├── JwtTokenService.cs             # Generates/validates JWT
│   └── AuthService.cs                 # Orchestrates auth flow
└── Data/
    ├── Configurations/
    │   ├── UserConfiguration.cs       # EF Core user entity config
    │   └── RefreshTokenConfiguration.cs
    └── Repositories/
        ├── UserRepository.cs
        └── RefreshTokenRepository.cs
```

**Presentation Layer (Uses Infrastructure):**
```
Controllers/
├── AuthController.cs
│   ├── POST /auth/google/callback     # OAuth callback
│   ├── POST /auth/facebook/callback   # OAuth callback
│   └── POST /auth/refresh             # Refresh token
└── AlbumsController.cs
    ├── [Authorize(Roles="Admin")]     # Only admins
    ├── [Authorize]                    # Any authenticated
    └── [AllowAnonymous]               # Access code endpoint
```

## When to Use This Skill

**Design scenarios:**
- Designing OAuth provider integration
- Adding new roles or permissions
- Creating access code system
- Implementing token refresh
- Setting up development auth bypass

**Implementation scenarios:**
- Creating auth controller
- Setting up JWT validation
- Implementing user repository
- Adding role-based authorization
- Creating access code endpoints

**Testing scenarios:**
- Writing auth unit tests
- Testing role-based access
- Mocking token validators
- Testing token expiration

## Key Concepts

### OAuth Extensibility
New providers (Facebook, Microsoft) implemented without touching Google code:

```csharp
// Each provider: separate validator class implementing interface
// Factory creates appropriate validator
// All validators follow same contract

// To add Facebook: create FacebookTokenValidator, register it, done
```

### Role-Based Access Control (RBAC)
```csharp
[Authorize(Roles = "Admin")]        // Admin only
[Authorize(Roles = "Admin,User")]   // Admin or User
[Authorize]                         // Any authenticated
[AllowAnonymous]                    // Public
```

### Visitor Access via Access Codes
```csharp
// Public endpoint for access code-based access
// Access code has expiration (default 30 days)
// No authentication required
// But access code must be valid
```

### Development Bypass
```csharp
// DISABLE_AUTH=true in development
// Automatically logs in as testadmin@localhost (Admin role)
// No Google login needed during testing
```

## Related Skills

- **Clean Architecture** - How to layer your auth code (Domain/Infrastructure/Presentation)
- **PhotoGallery Architect** - Validates SOLID/DRY compliance of auth code
- **PhotoGallery Unit Testing** - How to test auth services and mocking validators

## Configuration

**appsettings.json:**
```json
{
  "Auth": {
    "AdminEmail": "mrdieppa@gmail.com"
  },
  "Google": {
    "ClientId": "xxx.apps.googleusercontent.com",
    "ClientSecret": "xxx",
    "RedirectUri": "https://localhost:8443/auth/google/callback"
  },
  "Jwt": {
    "Secret": "your-secret-at-least-32-chars",
    "Issuer": "PhotoGallery",
    "Audience": "PhotoGalleryClients",
    "ExpirationMinutes": 1440
  }
}
```

**appsettings.Development.json:**
```json
{
  "DISABLE_AUTH": true
}
```

## Next Steps

1. Read SKILL.md for complete patterns and examples
2. Use for designing user entity and roles
3. Reference when implementing OAuth callback
4. Reference when creating token validation
5. Reference when implementing role-based endpoints
6. Reference for access code patterns

---

For detailed code examples, patterns, and implementation guidance, see **SKILL.md**.
