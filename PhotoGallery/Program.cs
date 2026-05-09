using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using PhotoGallery.Data;
using PhotoGallery.Data.Repositories;
using PhotoGallery.Interfaces;
using PhotoGallery.Middleware;
using PhotoGallery.Models;
using PhotoGallery.Services;
using PhotoGallery.Services.Processing;
using PhotoGallery.Services.Storage;
using Serilog;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

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

// Configure CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("DevelopmentPolicy", cors =>
    {
        cors.AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader();
    });
});

// Configure JWT Bearer authentication
var jwtKey = builder.Configuration["Authentication:Jwt:Key"];
if (!string.IsNullOrEmpty(jwtKey))
{
    builder.Services.AddAuthentication(options =>
        {
            options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = GoogleDefaults.AuthenticationScheme;
        })
        .AddCookie()
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
                ValidateIssuer = true,
                ValidIssuer = builder.Configuration["Authentication:Jwt:Issuer"],
                ValidateAudience = true,
                ValidAudience = builder.Configuration["Authentication:Jwt:Audience"],
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            };
        })
        .AddGoogle(google =>
        {
            google.ClientId = builder.Configuration["Google:ClientId"];
            google.ClientSecret = builder.Configuration["Google:ClientSecret"];
        });
}
else
{
    builder.Services.AddAuthentication(options =>
        {
            options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = GoogleDefaults.AuthenticationScheme;
        })
        .AddCookie()
        .AddGoogle(google =>
        {
            google.ClientId = builder.Configuration["Google:ClientId"];
            google.ClientSecret = builder.Configuration["Google:ClientSecret"];
        });
}

builder.Services.AddScoped<JwtTokenService>();
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
builder.Services.AddScoped<IProcessingQueueRepository, ProcessingQueueRepository>();
builder.Services.AddScoped<IProcessingQueueItemRepository, ProcessingQueueItemRepository>();
builder.Services.AddScoped<IAuditLogRepository, AuditLogRepository>();

// Register services
builder.Services.AddScoped<PhotoVersionUrlService>();
builder.Services.AddScoped<ZipDownloadService>();
builder.Services.AddScoped<GdprService>();

// Register image processing service as singleton (manages its own scopes for background worker)
builder.Services.AddSingleton<IImageProcessor, ImageProcessingService>();

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
app.UseCors("DevelopmentPolicy");

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
