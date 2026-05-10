using Authentication;
using Authentication.Services;
using Configuration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PhotoGallery.Data;
using PhotoGallery.Data.Repositories;
using PhotoGallery.Interfaces;
using PhotoGallery.Middleware;
using PhotoGallery.Models;
using PhotoGallery.Services;
using PhotoGallery.Services.Email;
using PhotoGallery.Services.Processing;
using PhotoGallery.Services.Storage;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Bind strongly-typed configuration first so downstream registrations can use it.
// Reference: clean-architecture-guide skill — "Cross-Cutting Concerns Live in Sub-Projects"
builder.Services.AddConfigurationServices(builder.Configuration, out var settings);

// Configure Serilog
builder.Host.UseSerilog((context, configuration) =>
    configuration.ReadFrom.Configuration(context.Configuration));

// Configure listening URLs (defaults to 5105, or use ASPNETCORE_URLS env var if set)
var urls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? "http://localhost:5105";
builder.WebHost.UseUrls(urls);

// Add services to the container.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ??
                       throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    options.UseSqlite(connectionString, sqliteOptions =>
    {
        sqliteOptions.CommandTimeout(5);
    });
    options.UseQueryTrackingBehavior(QueryTrackingBehavior.TrackAll);
});
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

// Configure CORS — AllowFrontendDev is scoped to the SPA origin from
// ConfigurationSettings.Frontend.Url (env var Frontend__Url) and permits
// credentials so future cookie-based flows Just Work. AllowAnyOrigin is
// avoided because it's incompatible with AllowCredentials and weakens
// prod parity. Mirrors VerdantIQ's named-policy pattern.
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontendDev", cors =>
    {
        cors.WithOrigins(settings.Frontend.Url)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

// Configure JWT Bearer authentication via the Authentication sub-project.
// JwtTokenService and the JwtBearer scheme are both registered here.
// Reference: photogallery-auth-skill — JWT issuance + validation
builder.Services.AddAuthenticationServices(settings);

// Cookie + Google OAuth schemes are still wired here because they're used by the
// legacy server-side OAuth callback flow (AuthController.Login + GoogleCallback).
// Once the frontend fully switches to the GIS popup → /api/auth/external-login flow,
// these can be removed.
builder.Services.AddAuthentication()
    .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddGoogle(google =>
    {
        google.ClientId = settings.Google.ClientId;
        google.ClientSecret = settings.Google.ClientSecret;
    });

builder.Services.AddScoped<IExternalAuthService, ExternalAuthService>();

// Register storage provider based on configuration
builder.Services.AddSingleton<IStorageProvider>(sp =>
{
    try
    {
        return StorageProviderFactory.Create(builder.Configuration, sp);
    }
    catch (Exception ex)
    {
        var logger = sp.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "Failed to create storage provider. Check your storage configuration.");
        throw;
    }
});

// Register repositories
builder.Services.AddScoped<ApplicationDbContextInitializer>();
builder.Services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
builder.Services.AddScoped<IAlbumRepository, AlbumRepository>();
builder.Services.AddScoped<IPhotoRepository, PhotoRepository>();
builder.Services.AddScoped<IPhotoVersionUrlRepository, PhotoVersionUrlRepository>();
builder.Services.AddScoped<IAccessCodeRepository, AccessCodeRepository>();
builder.Services.AddScoped<IUserCartRepository, UserCartRepository>();
builder.Services.AddScoped<IProcessingQueueRepository, ProcessingQueueRepository>();
builder.Services.AddScoped<IProcessingQueueItemRepository, ProcessingQueueItemRepository>();
builder.Services.AddScoped<IAuditLogRepository, AuditLogRepository>();

// Register services
builder.Services.AddScoped<PhotoVersionUrlService>();
builder.Services.AddScoped<ICartZipService, CartZipService>();
builder.Services.AddSingleton<WatermarkService>();
builder.Services.AddScoped<IWatermarkTextResolver, WatermarkTextResolver>();
builder.Services.AddScoped<IWatermarkBackfillService, WatermarkBackfillService>();
builder.Services.AddScoped<GdprService>();
builder.Services.AddScoped<IAuditLogRepository, AuditLogRepository>();
builder.Services.AddScoped<GdprService>();

// Register image processing service as singleton (manages its own scopes for background worker)
builder.Services.AddSingleton<IImageProcessor, ImageProcessingService>();

// Register email service via configuration-driven factory (mock for dev/tests, Azure for prod).
var emailProvider = builder.Configuration["Email:Provider"] ?? "mock";
if (emailProvider.Equals("azure", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<IEmailService, AzureCommunicationEmailService>();
}
else
{
    builder.Services.AddSingleton<IEmailService, MockEmailService>();
}

// Register consistency checker for validating photo processing completion
builder.Services.AddScoped<PhotoConsistencyChecker>();

// Register storage/DB consistency reconciliation (D007).
// Scoped because the per-cycle StorageConsistencyWorker resolves it per tick
// and the admin endpoint resolves it per request. The internal SemaphoreSlim
// is per-instance which is sufficient for the single-process deployment model.
builder.Services.AddScoped<StorageConsistencyService>();

// Register background services for photo processing and URL refresh
builder.Services.AddHostedService<PhotoProcessingWorker>();
builder.Services.AddHostedService<PhotoVersionUrlRefreshWorker>();
builder.Services.AddHostedService<StorageConsistencyWorker>();

builder.Services.AddDefaultIdentity<User>(options => options.SignIn.RequireConfirmedAccount = true)
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>();

// Restore JwtBearer as the default authentication scheme. AddDefaultIdentity
// (above) calls AddIdentityCookies() which silently overrides the
// DefaultAuthenticateScheme + DefaultChallengeScheme set by
// AddAuthenticationServices to its own cookie scheme. Without this, [Authorize]
// on API controllers would resolve against the (empty) Identity cookie
// principal — producing 403 on protected endpoints even when a valid Bearer
// JWT carrying the right roles is in the Authorization header.
//
// Symptom of the regression: GET /api/albums/{id}/access-codes returns 403
// despite OnTokenValidated logging roles=[Admin]. The JwtBearer scheme
// authenticated the token but [Authorize(Roles="Admin")] used the
// IdentityApplicationScheme (now empty) for its principal lookup.
builder.Services.Configure<AuthenticationOptions>(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
});

builder.Services.AddControllersWithViews();

var app = builder.Build();

// Initialize database
try
{
    await app.InitializeDatabaseAsync();
}
catch (Exception ex)
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    logger.LogError(ex, "Database initialization failed. The application will start anyway.");
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

// Enable CORS (before authentication)
app.UseCors("AllowFrontendDev");

// Required by Google Identity Services popup flow:
// the popup uses window.postMessage back to the opener, which browsers
// block when the opener's Cross-Origin-Opener-Policy is "same-origin".
// "same-origin-allow-popups" keeps cross-origin isolation for the rest
// of the page while permitting the GIS popup to communicate.
// See Documentation/Architecture/AUTHENTICATION.md → COOP for GIS popup.
app.Use(async (context, next) =>
{
    context.Response.Headers["Cross-Origin-Opener-Policy"] = "same-origin-allow-popups";
    await next();
});

// Add DISABLE_AUTH middleware (before UseAuthentication)
app.UseMiddleware<DisableAuthMiddleware>();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
        name: "default",
        pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.MapRazorPages()
    .WithStaticAssets();

// Start image processing worker
using (var scope = app.Services.CreateScope())
{
    var imageProcessor = scope.ServiceProvider.GetRequiredService<IImageProcessor>();
    await imageProcessor.StartProcessingWorkerAsync(app.Lifetime.ApplicationStopping);
}

app.Run();
