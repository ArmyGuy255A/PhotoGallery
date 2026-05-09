# Authentication

**📍 Navigation**
- 🏠 [Documentation Index](../INDEX.md)
- 🏗️ [Design Decisions](./DESIGN_DECISIONS.md) - All approved design decisions (D001: Authentication)
- 🏗️ [System Architecture](./SYSTEM_ARCHITECTURE.md) - Component overview
- 💾 [Database Schema](./DATABASE_SCHEMA.md) - Entity relationships
- 🔌 [API Design](./API_DESIGN.md) - REST endpoint patterns
- 📦 [Storage Layer](./STORAGE_LAYER.md) - File storage abstraction
- 📚 [All Guides](../Guides/) - TDD, Docker, CI/CD, Startup

---

# Authentication & Authorization

## Overview

PhotoGallery uses OAuth 2.0 with Google for authentication, combined with local role-based authorization using JWT tokens.

**See [DESIGN_DECISIONS.md](./DESIGN_DECISIONS.md) - D001: Microservice Authentication with Google OAuth + Local Database**

## Authentication Flow

```mermaid
sequenceDiagram
    User->>Frontend: Click "Login with Google"
    Frontend->>GoogleOAuth: Redirect to Google consent
    User->>GoogleOAuth: Approve access
    GoogleOAuth->>Backend: Redirect to /api/auth/google-callback<br/>with authorization code
    Backend->>GoogleOAuth: Exchange code for ID token
    GoogleOAuth-->>Backend: ID token + user email
    Backend->>Database: Find or create user by email
    Database-->>Backend: User + roles
    Backend->>Backend: Generate JWT token<br/>with email + roles claims
    Backend-->>Frontend: Return JWT token
    Frontend->>Frontend: Store JWT in localStorage
```

## JWT Token Structure

```json
{
  "header": {
    "alg": "HS256",
    "typ": "JWT"
  },
  "payload": {
    "sub": "user@example.com",
    "email": "user@example.com",
    "role": ["Admin", "User"],
    "iat": 1234567890,
    "exp": 1234571490
  },
  "signature": "HMACSHA256(...)"
}
```

## Token Validation Flow

```mermaid
sequenceDiagram
    Client->>Backend: GET /api/albums<br/>with Authorization: Bearer {token}
    Backend->>Backend: Validate JWT signature
    Backend->>Backend: Check token expiry
    Backend->>Backend: Extract claims (email, role)
    alt Token valid
        Backend->>Database: Process request
        Backend-->>Client: 200 OK
    else Token invalid
        Backend-->>Client: 401 Unauthorized
    end
```

## Role-Based Access Control

### Roles

```
Admin:
  - Create/edit/delete albums
  - Upload photos
  - Generate access codes
  - View all user activity

User:
  - View own albums
  - View shared albums (via access code)
  - Download photos
```

### Authorization Example

```csharp
[ApiController]
[Route("api/albums")]
public class AlbumsController
{
    [Authorize]  // Any authenticated user
    [HttpGet]
    public async Task<IActionResult> GetAlbums()
    {
        // Returns only user's albums (filtered in service)
    }
    
    [Authorize(Roles = "Admin")]  // Admin only
    [HttpPost]
    public async Task<IActionResult> CreateAlbum(CreateAlbumRequest request)
    {
        // Creates album
    }
}
```

## API Endpoints

### Public (No Authentication)

| Method | Path | Description |
|--------|------|-------------|
| `POST` | `/api/auth/google-callback` | Handle Google OAuth redirect |

### Authenticated

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/api/auth/me` | Get current user info |
| `POST` | `/api/auth/logout` | Logout (frontend discards token) |
| `POST` | `/api/auth/refresh` | Refresh expiring token |

## Configuration

### Development (with DISABLE_AUTH)

```json
{
  "Authentication": {
    "DisableAuth": true
  }
}
```

Or via environment variable:
```bash
$env:DISABLE_AUTH = "true"
```

When disabled, backend creates a test user automatically:
```csharp
// Middleware creates test user
var testUser = new User
{
    Email = "testadmin@localhost",
    GoogleId = "test",
    Roles = new[] { "Admin", "User" }
};
```

### Development (with Google OAuth)

```json
{
  "Authentication": {
    "Google": {
      "ClientId": "...-app.googleusercontent.com",
      "ClientSecret": "...",
      "RedirectUri": "http://localhost:5105/api/auth/google-callback"
    }
  }
}
```

### Production (Google OAuth required)

```json
{
  "Authentication": {
    "Google": {
      "ClientId": "...-app.googleusercontent.com",
      "ClientSecret": "${GOOGLE_CLIENT_SECRET}",
      "RedirectUri": "https://api.photogallery.com/api/auth/google-callback"
    },
    "Jwt": {
      "Secret": "${JWT_SECRET}",
      "Issuer": "PhotoGallery",
      "Audience": "PhotoGalleryClient",
      "ExpiryMinutes": 60
    }
  }
}
```

## Google Cloud Console Setup

The OAuth 2.0 Client used by both frontend (GIS popup) and backend (token exchange) **must** have correct **Authorized JavaScript origins** and **Authorized redirect URIs** registered in the Google Cloud Console — otherwise sign-in fails with one of:

| Symptom | Cause | Fix |
|---------|-------|-----|
| `Error 400: invalid_request` — *Missing required parameter: client_id* | `Google:ClientId` not set in backend config (FE fetches it from `/api/config/public` at runtime) | Set `Google__ClientId` env var or `Google:ClientId` in `appsettings.Development.json`, restart backend |
| `Error 401: invalid_client` — *no registered origin* | The browser's origin (e.g. `http://localhost:4300`) is not in the OAuth client's *Authorized JavaScript origins* | Add the origin in Google Cloud Console (steps below); wait ~30s for propagation |
| `Error 400: redirect_uri_mismatch` | Backend exchanged a code with a `redirect_uri` not in the OAuth client's *Authorized redirect URIs* | Add the exact redirect URI; values must match scheme + host + port + path |

### Configuring the OAuth client

1. Open [console.cloud.google.com](https://console.cloud.google.com/) → **APIs & Services** → **Credentials**
2. Click your OAuth 2.0 Client ID (the value in `Google:ClientId`)
3. Under **Authorized JavaScript origins**, add the origins your SPA will load from. For a typical local dev setup:
   - `http://localhost:4300` (Angular `ng serve`)
   - `http://localhost:5105` (backend, if it ever serves the SPA itself)
   - Plus any production / staging origins (`https://yourdomain.com`)
4. Under **Authorized redirect URIs**, add the backend callback URL(s):
   - `http://localhost:5105/api/auth/google-callback` (local dev)
   - Plus production redirect URI
5. **Save**. Google takes up to ~30 seconds to propagate the new origins; if sign-in still fails, wait and retry before assuming a code bug.

### Still seeing `Error 401: invalid_client — no registered origin`?

Even with the right origin saved, exact-string matching trips people up. Verify all of these:

- [ ] You're editing the **same OAuth client** whose ID the backend serves at `GET /api/config/public`. Run:
  ```powershell
  curl http://localhost:5105/api/config/public
  ```
  The `googleClientId` returned must match the **Client ID** shown on the OAuth client edit page in Google Console. If they don't match, you're configuring the wrong client.
- [ ] The registered origin is **exactly** `http://localhost:4300` — no trailing slash, no path, lowercase scheme/host.
- [ ] Scheme matches: `http://` (not `https://`) for `localhost`. Mixing schemes is a common silent failure.
- [ ] Port matches what `ng serve` actually bound to. If you see `Port 4300 is already in use` and accept a fallback (e.g. `4301`), the popup will fail because that port isn't registered.
- [ ] You're testing in a **fresh browser tab / incognito** — Google's GIS SDK aggressively caches the previous origin/client config in some cases.
- [ ] In the browser DevTools console, `GoogleAuthService` logs `[GIS init] origin=… clientId=…` on first sign-in attempt. The `origin` MUST be one of the registered Authorized JavaScript origins.

If everything above checks out and the error persists, wait 5 minutes (rare propagation delay) before assuming a deeper bug.

### Why the FE needs JavaScript origins, not just redirect URIs

PhotoGallery uses the **Google Identity Services (GIS) popup flow** in the browser (see `FE.PhotoGallery/src/app/services/auth/providers/google-auth.service.ts`). GIS validates the calling origin against the OAuth client's *Authorized JavaScript origins* list before opening the consent popup. Redirect URIs only apply to server-side authorization-code flows (the legacy `LoginController.GoogleCallback` path).

### COOP for the GIS popup

The GIS popup posts the credential back to the opener tab via `window.postMessage`. Browsers block that when the opener's `Cross-Origin-Opener-Policy` is `same-origin` (the default for many dev servers and for cross-origin-isolated production sites). Symptom in DevTools:

```
client:381 Cross-Origin-Opener-Policy policy would block the window.postMessage call.
```

PhotoGallery sets `Cross-Origin-Opener-Policy: same-origin-allow-popups` in two places to keep dev and prod parity:

- **Dev server** — `FE.PhotoGallery/angular.json` → `serve.options.headers`
- **Backend** — `PhotoGallery/Program.cs` middleware (runs before the static file pipeline so every response carries the header)

Additionally, `GoogleAuthService` enables `use_fedcm_for_prompt: true` so modern Chrome uses the FedCM API (no postMessage at all) — defense in depth for environments where the COOP header can't be controlled.

## Frontend Integration

### Login

```typescript
// Angular component
export class LoginComponent {
  login() {
    // Redirect to Google OAuth
    window.location.href = `/api/auth/google-callback?...`;
  }
}
```

### Token Storage

```typescript
// In auth service
export class AuthService {
  login(token: string) {
    localStorage.setItem('jwt_token', token);
    this.currentUser = this.extractUserFromToken(token);
  }
  
  logout() {
    localStorage.removeItem('jwt_token');
  }
  
  getToken(): string | null {
    return localStorage.getItem('jwt_token');
  }
}
```

### API Calls with Token

```typescript
// In HTTP interceptor
export class AuthInterceptor implements HttpInterceptor {
  intercept(
    req: HttpRequest<any>,
    next: HttpHandler
  ): Observable<HttpEvent<any>> {
    const token = this.authService.getToken();
    if (token) {
      req = req.clone({
        setHeaders: {
          Authorization: `Bearer ${token}`
        }
      });
    }
    return next.handle(req);
  }
}
```

## Testing Authentication

```csharp
[Fact]
public async Task GetAlbums_WithValidToken_Returns200()
{
    // Arrange
    var user = new User { Email = "test@example.com", Roles = new[] { "Admin" } };
    var token = jwtTokenService.GenerateToken(user);
    
    client.DefaultRequestHeaders.Authorization = 
        new AuthenticationHeaderValue("Bearer", token);
    
    // Act
    var response = await client.GetAsync("/api/albums");
    
    // Assert
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
}

[Fact]
public async Task GetAlbums_WithoutToken_Returns401()
{
    // Act
    var response = await client.GetAsync("/api/albums");
    
    // Assert
    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
}

[Fact]
public async Task CreateAlbum_WithUserRole_Returns403()
{
    // Arrange
    var user = new User { Email = "test@example.com", Roles = new[] { "User" } };
    var token = jwtTokenService.GenerateToken(user);
    
    client.DefaultRequestHeaders.Authorization = 
        new AuthenticationHeaderValue("Bearer", token);
    
    // Act
    var response = await client.PostAsync("/api/albums", ...);
    
    // Assert
    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
}
```

## Future Extensions

### Additional OAuth Providers

To add Facebook, Microsoft, etc., create additional auth providers:

```csharp
public interface IExternalAuthProvider
{
    Task<ExternalAuthResult> AuthenticateAsync(string code);
}

// Add implementations
public class FacebookAuthProvider : IExternalAuthProvider { ... }
public class MicrosoftAuthProvider : IExternalAuthProvider { ... }

// Route to correct provider
var provider = providerType switch
{
    "google" => new GoogleAuthProvider(...),
    "facebook" => new FacebookAuthProvider(...),
    "microsoft" => new MicrosoftAuthProvider(...),
};
```

### API Key Authentication

For future service-to-service communication:

```csharp
public interface IApiKeyValidator
{
    Task<bool> ValidateAsync(string apiKey);
}

// Usage in middleware or custom authorization handler
```

---

## Related Documentation

- 🏗️ [Design Decisions](./DESIGN_DECISIONS.md) - D001 explains auth design
- 🏗️ [System Architecture](./SYSTEM_ARCHITECTURE.md) - Auth flow diagram
- 🔌 [API Design](./API_DESIGN.md) - Protected endpoints
- 📚 [Guides](../Guides/) - Setup and configuration

---

**Last Updated**: 2026-05-03  
**Current Providers**: Google OAuth  
**Status**: Production ready  
**Related Decision**: [D001](./DESIGN_DECISIONS.md)
