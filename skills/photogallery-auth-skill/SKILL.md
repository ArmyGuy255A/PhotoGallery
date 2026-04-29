---
name: photogallery-auth
description: |
  Authentication and Authorization expert for PhotoGallery. This skill covers JWT token management, role-based access control (RBAC), OAuth provider integration (Google, Facebook, Microsoft), claims-based authorization, token refresh strategies, and development testing bypass patterns. Use this whenever designing auth services, implementing OAuth flows, managing JWT tokens, handling user roles and permissions, or creating auth bypass mechanisms for testing. Explains how to abstract auth providers so they're swappable, how to integrate with Entity Framework for user persistence, and how to issue tokens for API authentication.
---

# PhotoGallery Authentication & Authorization Skill

## Overview

PhotoGallery's authentication system has three components:

1. **External Authentication** - OAuth providers (Google, Facebook, Microsoft)
2. **Internal Authorization** - Role-based access control (Admin, User, Visitor)
3. **API Token Management** - JWT tokens for API calls after OAuth login

**Key Design:** OAuth validates user identity, then we persist the user in our database and issue a JWT token for API authentication. This approach:
- ✅ Supports multiple OAuth providers without code changes (extensible)
- ✅ Stores user roles in our database (we control authorization)
- ✅ Issues JWT tokens for stateless API authentication
- ✅ Allows development bypass (DISABLE_AUTH=true for testing)

## The Three Auth Layers

### 1. External Authentication (OAuth Providers)

**Purpose:** Validate user identity with third-party provider (Google, Facebook, etc.)

**Providers PhotoGallery supports:**
- Google OAuth (primary, configured in appsettings)
- Facebook OAuth (future)
- Microsoft OAuth (future)

**Key Concept:** We never validate passwords. Users authenticate with their provider account.

#### How External Auth Works

```csharp
// User clicks "Login with Google"
// → User authenticates with Google (not our app)
// → Google returns token (we don't store this)
// → We verify the Google token
// → We create/update user in our database
// → We issue JWT token to user's browser
// → User uses JWT token for all API calls

[HttpGet("auth/google/callback")]
public async Task<IActionResult> GoogleCallback(string code, string state)
{
    // 1. Exchange code for Google token
    var googleToken = await _externalAuthService.ExchangeCodeAsync(code, "google");
    
    // 2. Extract user info from Google token
    var userClaims = await _tokenValidator.ValidateAsync(googleToken, "google");
    var googleId = userClaims["sub"]; // Google user ID
    var email = userClaims["email"];
    
    // 3. Check if user exists in our database
    var user = await _userRepository.GetByEmailAsync(email);
    if (user == null)
    {
        // First login: create user
        user = new User(email, googleId, "google");
        user.SetRole(DetermineUserRole(email)); // Admin if configured, else User
        await _userRepository.AddAsync(user);
    }
    else
    {
        // Returning user: update last login
        user.UpdateLastLogin();
    }
    
    await _userRepository.SaveChangesAsync();
    
    // 4. Issue JWT token to user
    var jwtToken = _jwtTokenService.GenerateToken(user);
    
    // 5. Return to client with token
    return Redirect($"/?token={jwtToken}");
}
```

### 2. Internal Authorization (Role-Based Access Control)

**Purpose:** Control what authenticated users can do based on their role

**PhotoGallery Roles:**
- **Admin** - Can create/edit/delete albums, upload photos, generate access codes
- **User** - Can view own albums, generate access codes (future)
- **Visitor** - No authenticated actions (see below for Visitor)

**Key Concept:** Roles are stored in our database, determined at login, added as claims to JWT token.

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

// Configure JWT validation in Program.cs
var jwtSettings = configuration.GetSection("Jwt");
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(jwtSettings["Secret"])),
        ValidateIssuer = true,
        ValidIssuer = jwtSettings["Issuer"],
        ValidateAudience = true,
        ValidAudience = jwtSettings["Audience"],
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero,
    };
});
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

// Register in Program.cs
builder.Services.AddScoped<GoogleTokenValidator>();
builder.Services.AddScoped<FacebookTokenValidator>();
builder.Services.AddScoped<TokenValidatorFactory>();
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

## Authentication Bypass for Development

**Problem:** Don't want to constantly login with Google during testing

**Solution:** DISABLE_AUTH environment variable creates test admin user

```csharp
// In Program.cs
if (configuration.GetValue<bool>("DISABLE_AUTH"))
{
    // Development: bypass OAuth, create test user
    builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = "Development";
        options.DefaultChallengeScheme = "Development";
    })
    .AddScheme<AuthenticationSchemeOptions, DevelopmentAuthHandler>(
        "Development", null);
}
else
{
    // Production: use JWT with Google OAuth
    // [JWT configuration from above]
}

// Custom auth handler for development
public class DevelopmentAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly IUserRepository _userRepository;
    
    public DevelopmentAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ISystemClock clock,
        IUserRepository userRepository)
        : base(options, logger, encoder, clock)
    {
        _userRepository = userRepository;
    }
    
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Create/retrieve test admin user
        var testUser = await _userRepository.GetByEmailAsync("testadmin@localhost");
        if (testUser == null)
        {
            testUser = new User("testadmin@localhost", "test-local-id", "development");
            testUser.SetRole(UserRole.Admin);
            await _userRepository.AddAsync(testUser);
            await _userRepository.SaveChangesAsync();
        }
        
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, testUser.Id.ToString()),
            new Claim(ClaimTypes.Email, testUser.Email),
            new Claim(ClaimTypes.Role, testUser.Role.ToString()),
        };
        
        var identity = new ClaimsIdentity(claims, "Development");
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, "Development");
        
        return AuthenticateResult.Success(ticket);
    }
}
```

**appsettings.Development.json:**
```json
{
  "DISABLE_AUTH": true,
  "Jwt": {
    "Secret": "development-secret-key-at-least-32-characters",
    "Issuer": "PhotoGallery",
    "Audience": "PhotoGalleryClients"
  }
}
```

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

## Configuration (appsettings.json)

```json
{
  "Auth": {
    "AdminEmail": "mrdieppa@gmail.com",
    "AllowedDomains": ["localhost:4200", "localhost:8443", "yourdomain.com"]
  },
  "Google": {
    "ClientId": "your-google-client-id.apps.googleusercontent.com",
    "ClientSecret": "your-google-client-secret",
    "RedirectUri": "https://localhost:8443/auth/google/callback"
  },
  "Facebook": {
    "AppId": "your-facebook-app-id",
    "AppSecret": "your-facebook-app-secret",
    "RedirectUri": "https://localhost:8443/auth/facebook/callback"
  },
  "Jwt": {
    "Secret": "your-secret-key-at-least-32-characters-long",
    "Issuer": "PhotoGallery",
    "Audience": "PhotoGalleryClients",
    "ExpirationMinutes": 1440
  }
}
```

## Common Patterns

### JWT in Angular

```typescript
// auth.service.ts
@Injectable({ providedIn: 'root' })
export class AuthService {
  private token$ = new BehaviorSubject<string | null>(
    localStorage.getItem('access_token')
  );
  
  login(provider: string) {
    // Redirect to backend OAuth endpoint
    window.location.href = `/auth/${provider}`;
  }
  
  handleCallback(token: string) {
    localStorage.setItem('access_token', token);
    this.token$.next(token);
  }
  
  getToken(): string | null {
    return this.token$.value;
  }
  
  logout() {
    localStorage.removeItem('access_token');
    this.token$.next(null);
  }
}

// HTTP interceptor - add token to requests
@Injectable()
export class AuthInterceptor implements HttpInterceptor {
  constructor(private auth: AuthService) {}
  
  intercept(req: HttpRequest<any>, next: HttpHandler) {
    const token = this.auth.getToken();
    if (token) {
      req = req.clone({
        setHeaders: { Authorization: `Bearer ${token}` }
      });
    }
    return next.handle(req);
  }
}
```

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

- [ ] External OAuth provider configured (Google, Facebook, etc.)
- [ ] User entity created with email, role, external ID
- [ ] JWT token generation working
- [ ] Token validation in Program.cs configured
- [ ] Roles (Admin, User) seeded correctly
- [ ] Admin user seeded with configured email
- [ ] Access codes working for unauthenticated visitors
- [ ] DISABLE_AUTH development bypass working
- [ ] Token refresh working
- [ ] Claims-based authorization configured (if using)
- [ ] CORS configured for OAuth redirect
- [ ] Expiration dates reasonable (24h for access, 7d for refresh)
- [ ] All secrets in configuration, not code

---

**Key Takeaway:** PhotoGallery's auth is extensible (new providers), stateless (JWT), and testable (development bypass). Users authenticate externally (OAuth), we persist them internally, and issue tokens for API access.
