using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PhotoGallery.Data;
using PhotoGallery.Enums;
using PhotoGallery.Models;
using PhotoGallery.Services;

namespace PhotoGallery.Controllers;

/// <summary>
/// Admin-only configuration + analytics surface (issue #70).
///
/// Endpoints:
///   GET    /api/admin/users                                — user list with roles, last login, counts
///   PUT    /api/admin/users/{userId}/roles                 — replace the user's role assignments
///   GET    /api/admin/stats/downloads-by-album             — download counts per album
///   GET    /api/admin/stats/top-downloaded-photos          — top N photos by download count
///   GET    /api/admin/users/{userId}/downloads             — every photo this user downloaded
///
/// Everything sits behind [Authorize(Roles = "Admin")] and the JWT
/// validation in <c>Authentication.DependencyInjection</c> drives the
/// role claim — no separate admin auth scheme.
/// </summary>
[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin")]
public class AdminController : ControllerBase
{
    private static readonly string[] AllowedRoles = { "Admin", "User" };

    private readonly ApplicationDbContext _ctx;
    private readonly UserManager<User> _userManager;
    private readonly ILogger<AdminController> _logger;

    public AdminController(
        ApplicationDbContext ctx,
        UserManager<User> userManager,
        ILogger<AdminController> logger)
    {
        _ctx = ctx;
        _userManager = userManager;
        _logger = logger;
    }

    /// <summary>
    /// Paginated, filterable, sortable user list for the Admin page. Returns
    /// the items plus total-count + page metadata so the SPA can render
    /// "Page 3 of 12 (250 users)" without a second round-trip.
    ///
    /// Query params:
    ///   search   — substring match against email + first/last name (case-insensitive)
    ///   sortBy   — one of: email, name, lastLogin, loginCount, downloads, created
    ///   sortDir  — 'asc' or 'desc' (default email asc, lastLogin desc)
    ///   page     — 1-based page number (default 1)
    ///   pageSize — items per page (default 25, max 200)
    /// </summary>
    [HttpGet("users")]
    public async Task<ActionResult<AdminUserPage>> GetUsers(
        [FromQuery] string? search = null,
        [FromQuery] string? sortBy = "email",
        [FromQuery] string? sortDir = "asc",
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25)
    {
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 200) pageSize = 25;

        var query = _ctx.Users.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            // Use ToLower() in EF translation rather than EF.Functions.Like
            // so the filter works identically on Sqlite (case-insensitive
            // FOLD via ToLower) and SqlServer (case-insensitive default
            // collation; ToLower is a no-op there but still correct).
            var sl = s.ToLower();
            query = query.Where(u =>
                (u.Email != null && u.Email.ToLower().Contains(sl)) ||
                (u.FirstName != null && u.FirstName.ToLower().Contains(sl)) ||
                (u.LastName != null && u.LastName.ToLower().Contains(sl)));
        }

        var descending = string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase);
        // Precompute the per-user album + download counts in two grouped
        // queries — joining inside ORDER BY would multi-count rows when the
        // user owns more than one album.
        var albumCounts = await _ctx.Albums
            .GroupBy(a => a.OwnerId)
            .Select(g => new { OwnerId = g.Key, Count = g.Count() })
            .ToListAsync();
        var downloadCounts = await _ctx.Downloads
            .Where(d => d.UserId != null)
            .GroupBy(d => d.UserId!)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToListAsync();
        var albumByOwner = albumCounts.ToDictionary(x => x.OwnerId, x => x.Count);
        var dlByUser = downloadCounts.ToDictionary(x => x.UserId, x => x.Count);

        // Pull all users into memory for sorting on derived columns
        // (album / download counts). The Admin user table is bounded by
        // total-signup count, which for v1 is small enough that this is
        // cheaper than building a SQL-side projection for the counts.
        var all = await query.ToListAsync();
        var withCounts = new List<(User U, int AlbumCount, int DownloadCount, IList<string> Roles)>();
        foreach (var u in all)
        {
            var roles = await _userManager.GetRolesAsync(u);
            withCounts.Add((u,
                albumByOwner.TryGetValue(u.Id, out var ac) ? ac : 0,
                dlByUser.TryGetValue(u.Id, out var dc) ? dc : 0,
                roles));
        }

        IEnumerable<(User U, int AlbumCount, int DownloadCount, IList<string> Roles)> sorted = (sortBy?.ToLowerInvariant()) switch
        {
            "name" => withCounts.OrderBy(x => (x.U.FirstName ?? "") + " " + (x.U.LastName ?? "")),
            "lastlogin" => withCounts.OrderBy(x => x.U.LastLoginAt ?? DateTime.MinValue),
            "logincount" => withCounts.OrderBy(x => x.U.LoginCount),
            "downloads" => withCounts.OrderBy(x => x.DownloadCount),
            "albums" => withCounts.OrderBy(x => x.AlbumCount),
            "created" => withCounts.OrderBy(x => x.U.CreatedDate),
            _ => withCounts.OrderBy(x => x.U.Email)
        };
        if (descending) sorted = sorted.Reverse();

        var ordered = sorted.ToList();
        var total = ordered.Count;
        var pageItems = ordered.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new AdminUserDto
            {
                Id = x.U.Id,
                Email = x.U.Email ?? string.Empty,
                FirstName = x.U.FirstName,
                LastName = x.U.LastName,
                Roles = x.Roles.ToList(),
                LastLoginAt = x.U.LastLoginAt,
                LoginCount = x.U.LoginCount,
                CreatedDate = x.U.CreatedDate,
                IsActive = x.U.IsActive,
                AlbumCount = x.AlbumCount,
                DownloadCount = x.DownloadCount
            })
            .ToList();

        return Ok(new AdminUserPage
        {
            Items = pageItems,
            TotalCount = total,
            Page = page,
            PageSize = pageSize,
            HasMore = page * pageSize < total
        });
    }

    /// <summary>
    /// Replace the set of roles assigned to a user. Idempotent — passing
    /// the same list twice is a no-op. Refuses to remove the last Admin
    /// (so an admin can't lock the system out by demoting themselves).
    /// </summary>
    [HttpPut("users/{userId}/roles")]
    public async Task<ActionResult<AdminUserDto>> SetUserRoles(string userId, [FromBody] SetRolesRequest body)
    {
        if (body?.Roles == null) return BadRequest("Roles array is required");

        var requested = body.Roles.Where(r => !string.IsNullOrWhiteSpace(r)).Distinct().ToList();
        var invalid = requested.Where(r => !AllowedRoles.Contains(r, StringComparer.OrdinalIgnoreCase)).ToList();
        if (invalid.Count > 0)
            return BadRequest($"Unknown role(s): {string.Join(", ", invalid)}. Allowed: {string.Join(", ", AllowedRoles)}");

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return NotFound("User not found");

        var current = (await _userManager.GetRolesAsync(user)).ToList();
        var toAdd = requested.Where(r => !current.Contains(r, StringComparer.OrdinalIgnoreCase)).ToList();
        var toRemove = current.Where(r => !requested.Contains(r, StringComparer.OrdinalIgnoreCase)).ToList();

        // Last-admin guard: if removing Admin from this user would leave the
        // system with zero admins, refuse the change. The very first admin
        // user can be created by AdminController itself by setting Admin on
        // someone else *first*, then demoting themselves.
        if (toRemove.Any(r => string.Equals(r, "Admin", StringComparison.OrdinalIgnoreCase)))
        {
            var adminUsers = await _userManager.GetUsersInRoleAsync("Admin");
            if (adminUsers.Count <= 1 && adminUsers.Any(a => a.Id == user.Id))
            {
                return Conflict(new { error = "Cannot remove Admin from the last administrator. Promote another user to Admin first." });
            }
        }

        if (toRemove.Count > 0)
        {
            var removeResult = await _userManager.RemoveFromRolesAsync(user, toRemove);
            if (!removeResult.Succeeded)
                return StatusCode(500, new { error = "Role removal failed", details = removeResult.Errors });
        }
        if (toAdd.Count > 0)
        {
            var addResult = await _userManager.AddToRolesAsync(user, toAdd);
            if (!addResult.Succeeded)
                return StatusCode(500, new { error = "Role assignment failed", details = addResult.Errors });
        }

        _logger.LogInformation(
            "Admin {Admin} updated roles for {UserId}: added=[{Added}] removed=[{Removed}]",
            User.Identity?.Name, userId, string.Join(",", toAdd), string.Join(",", toRemove));

        var roles = (await _userManager.GetRolesAsync(user)).ToList();
        return Ok(new AdminUserDto
        {
            Id = user.Id,
            Email = user.Email ?? string.Empty,
            FirstName = user.FirstName,
            LastName = user.LastName,
            Roles = roles,
            LastLoginAt = user.LastLoginAt,
            LoginCount = user.LoginCount,
            CreatedDate = user.CreatedDate,
            IsActive = user.IsActive
        });
    }

    /// <summary>
    /// Per-album download counts (descending). Useful for the admin to
    /// see which shoots are getting the most traffic.
    /// </summary>
    [HttpGet("stats/downloads-by-album")]
    public async Task<ActionResult<IEnumerable<AlbumDownloadStatDto>>> GetDownloadsByAlbum([FromQuery] int limit = 50)
    {
        if (limit <= 0 || limit > 500) limit = 50;
        var rows = await (
            from d in _ctx.Downloads
            join p in _ctx.Photos on d.PhotoId equals p.Id
            join a in _ctx.Albums on p.AlbumId equals a.Id
            group d by new { a.Id, a.Title } into g
            select new AlbumDownloadStatDto
            {
                AlbumId = g.Key.Id.ToString(),
                Title = g.Key.Title,
                DownloadCount = g.Count(),
                LastDownloadedAt = g.Max(d => (DateTime?)d.DownloadedAt)
            })
            .OrderByDescending(r => r.DownloadCount)
            .ThenByDescending(r => r.LastDownloadedAt ?? DateTime.MinValue)
            .Take(limit)
            .ToListAsync();
        return Ok(rows);
    }

    /// <summary>
    /// Top N most-downloaded photos (descending). Includes the album title
    /// so the UI can render a meaningful row without an extra round-trip.
    /// </summary>
    [HttpGet("stats/top-downloaded-photos")]
    public async Task<ActionResult<IEnumerable<TopPhotoDto>>> GetTopDownloadedPhotos([FromQuery] int limit = 20)
    {
        if (limit <= 0 || limit > 500) limit = 20;
        var rows = await (
            from d in _ctx.Downloads
            join p in _ctx.Photos on d.PhotoId equals p.Id
            join a in _ctx.Albums on p.AlbumId equals a.Id
            group d by new { p.Id, p.FileName, p.AlbumId, a.Title } into g
            select new TopPhotoDto
            {
                PhotoId = g.Key.Id.ToString(),
                FileName = g.Key.FileName,
                AlbumId = g.Key.AlbumId.ToString(),
                AlbumTitle = g.Key.Title,
                DownloadCount = g.Count(),
                LastDownloadedAt = g.Max(d => (DateTime?)d.DownloadedAt)
            })
            .OrderByDescending(r => r.DownloadCount)
            .Take(limit)
            .ToListAsync();
        return Ok(rows);
    }

    /// <summary>
    /// Every photo this user has downloaded — drives the "what did
    /// &lt;user&gt; pull out of their cart" drill-down. Filters by the new
    /// Download.UserId column (cart-checkout flow stamps it; legacy
    /// access-code flows leave it null and are excluded).
    /// </summary>
    [HttpGet("users/{userId}/downloads")]
    public async Task<ActionResult<IEnumerable<UserDownloadDto>>> GetUserDownloads(
        string userId,
        [FromQuery] int limit = 200)
    {
        if (limit <= 0 || limit > 1000) limit = 200;
        var rows = await (
            from d in _ctx.Downloads
            join p in _ctx.Photos on d.PhotoId equals p.Id
            join a in _ctx.Albums on p.AlbumId equals a.Id
            where d.UserId == userId
            orderby d.DownloadedAt descending
            select new UserDownloadDto
            {
                DownloadId = d.Id.ToString(),
                PhotoId = p.Id.ToString(),
                FileName = p.FileName,
                AlbumId = a.Id.ToString(),
                AlbumTitle = a.Title,
                Quality = d.Quality.ToString(),
                DownloadedAt = d.DownloadedAt
            }).Take(limit).ToListAsync();
        return Ok(rows);
    }

    /// <summary>
    /// Top-downloaded photos within a single album, sorted by download
    /// count (desc). Drives the "click an album to see its top photos"
    /// drill-down modal on the Admin stats tab. Each row includes a
    /// thumbnail URL so the modal can render visual rather than just
    /// filename text.
    /// </summary>
    [HttpGet("stats/album/{albumId:guid}/top-photos")]
    public async Task<ActionResult<IEnumerable<TopPhotoDto>>> GetAlbumTopDownloadedPhotos(
        Guid albumId,
        [FromQuery] int limit = 20)
    {
        if (limit <= 0 || limit > 500) limit = 20;
        var rows = await (
            from d in _ctx.Downloads
            join p in _ctx.Photos on d.PhotoId equals p.Id
            join a in _ctx.Albums on p.AlbumId equals a.Id
            where a.Id == albumId
            group d by new { p.Id, p.FileName, p.AlbumId, a.Title } into g
            select new TopPhotoDto
            {
                PhotoId = g.Key.Id.ToString(),
                FileName = g.Key.FileName,
                AlbumId = g.Key.AlbumId.ToString(),
                AlbumTitle = g.Key.Title,
                DownloadCount = g.Count(),
                LastDownloadedAt = g.Max(d => (DateTime?)d.DownloadedAt)
            })
            .OrderByDescending(r => r.DownloadCount)
            .Take(limit)
            .ToListAsync();
        return Ok(rows);
    }

    /// <summary>
    /// Per-access-code analytics: how many times the code has been entered
    /// into the URL (UserAccessLog rows), how many distinct IPs and
    /// User-Agents it's been served to, how many photos have been
    /// downloaded through it (Downloads rows where AccessCodeId matches),
    /// and the most recent access timestamp. Drives the Admin "code usage"
    /// table on the stats tab.
    /// </summary>
    [HttpGet("stats/access-codes")]
    public async Task<ActionResult<IEnumerable<AccessCodeStatDto>>> GetAccessCodeStats([FromQuery] int limit = 100)
    {
        if (limit <= 0 || limit > 500) limit = 100;

        var accessRows = await (
            from log in _ctx.Set<UserAccessLog>()
            group log by log.AccessCodeId into g
            select new
            {
                AccessCodeId = g.Key,
                AccessCount = g.Count(),
                DistinctIps = g.Select(l => l.IpAddress).Distinct().Count(),
                DistinctUserAgents = g.Select(l => l.UserAgent).Distinct().Count(),
                LastAccessedAt = g.Max(l => (DateTime?)l.AccessDate)
            }).ToListAsync();
        var accessByCode = accessRows.ToDictionary(r => r.AccessCodeId);

        var downloadRows = await _ctx.Downloads
            .Where(d => d.AccessCodeId != null)
            .GroupBy(d => d.AccessCodeId!.Value)
            .Select(g => new { AccessCodeId = g.Key, DownloadCount = g.Count() })
            .ToListAsync();
        var dlByCode = downloadRows.ToDictionary(r => r.AccessCodeId, r => r.DownloadCount);

        var codes = await (
            from c in _ctx.AccessCodes
            // Left-join the album so soft-deleted codes (AlbumId now null
            // via the SetNull cascade) still surface in admin analytics.
            // We fall back to c.DeletedAlbumTitle when the album row is gone.
            from a in _ctx.Albums.Where(a => a.Id == c.AlbumId).DefaultIfEmpty()
            select new
            {
                CodeId = c.Id,
                c.Code,
                AlbumId = a != null ? (Guid?)a.Id : null,
                AlbumTitle = a != null ? a.Title : c.DeletedAlbumTitle ?? "(album deleted)",
                c.CreatedDate,
                c.ExpirationDate,
                c.IsDeleted
            }).ToListAsync();

        var dtos = codes.Select(c =>
        {
            accessByCode.TryGetValue(c.CodeId, out var a);
            dlByCode.TryGetValue(c.CodeId, out var dl);
            return new AccessCodeStatDto
            {
                CodeId = c.CodeId.ToString(),
                Code = c.Code,
                AlbumId = c.AlbumId?.ToString() ?? string.Empty,
                AlbumTitle = c.AlbumTitle,
                IsDeleted = c.IsDeleted,
                AccessCount = a?.AccessCount ?? 0,
                DistinctIps = a?.DistinctIps ?? 0,
                DistinctUserAgents = a?.DistinctUserAgents ?? 0,
                PhotoDownloadCount = dl,
                LastAccessedAt = a?.LastAccessedAt,
                CreatedDate = c.CreatedDate,
                ExpirationDate = c.ExpirationDate
            };
        })
        .OrderByDescending(r => r.AccessCount)
        .ThenByDescending(r => r.LastAccessedAt ?? DateTime.MinValue)
        .Take(limit)
        .ToList();

        return Ok(dtos);
    }

    // ---------------------------------------------------------------------
    // Runtime settings — DB-backed key/value store editable from the admin
    // page. Resolution order: RuntimeSettings table -> IConfiguration ->
    // hard-coded default in the consumer.
    //
    // Changes persist immediately but most workers read their interval at
    // construction, so the UI flags those as "restart required". Workers
    // can opt into hot-reload by calling ISettingsResolver on each tick.
    // ---------------------------------------------------------------------

    [HttpGet("settings")]
    public async Task<ActionResult<IEnumerable<RuntimeSettingDto>>> GetSettings()
    {
        // Merge known catalogue defaults with persisted overrides so the
        // admin sees every editable setting, not just the ones an admin has
        // already changed.
        var catalogue = SettingsCatalogue.GetAll();
        var persisted = await _ctx.RuntimeSettings.AsNoTracking().ToListAsync();
        var byKey = persisted.ToDictionary(s => s.Key, StringComparer.OrdinalIgnoreCase);

        var cfg = HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        var dtos = catalogue.Select(c =>
        {
            byKey.TryGetValue(c.Key, out var p);
            return new RuntimeSettingDto
            {
                Key = c.Key,
                Category = c.Category,
                DataType = c.DataType,
                Description = c.Description,
                DefaultValue = c.DefaultValue,
                ConfiguredValue = cfg[c.Key] ?? c.DefaultValue,
                CurrentValue = p?.Value ?? cfg[c.Key] ?? c.DefaultValue,
                HasOverride = p != null,
                LastModifiedAt = p?.LastModifiedAt,
                LastModifiedBy = p?.LastModifiedBy,
                RestartRequired = c.RestartRequired
            };
        }).OrderBy(d => d.Category).ThenBy(d => d.Key).ToList();
        return Ok(dtos);
    }

    [HttpPut("settings/{key}")]
    public async Task<ActionResult<RuntimeSettingDto>> UpdateSetting(string key, [FromBody] UpdateSettingRequest body)
    {
        if (string.IsNullOrWhiteSpace(body?.Value))
            return BadRequest(new { error = "Value is required." });

        var entry = SettingsCatalogue.GetAll().FirstOrDefault(c => string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase));
        if (entry == null)
            return NotFound(new { error = $"Unknown setting '{key}'." });

        // Type-check before persisting so a typo doesn't crash the next
        // worker tick.
        if (!entry.IsValid(body.Value, out var err))
            return BadRequest(new { error = err });

        var existing = await _ctx.RuntimeSettings.FirstOrDefaultAsync(s => s.Key == entry.Key);
        if (existing == null)
        {
            existing = new RuntimeSetting
            {
                Id = Guid.NewGuid(),
                Key = entry.Key,
                Category = entry.Category,
                DataType = entry.DataType,
                Description = entry.Description
            };
            _ctx.RuntimeSettings.Add(existing);
        }
        existing.Value = body.Value;
        existing.LastModifiedAt = DateTime.UtcNow;
        existing.LastModifiedBy = User.Identity?.Name;
        await _ctx.SaveChangesAsync();

        _logger.LogInformation(
            "Admin {Admin} updated runtime setting {Key}={Value}",
            existing.LastModifiedBy, existing.Key, existing.Value);

        return Ok(new RuntimeSettingDto
        {
            Key = entry.Key,
            Category = entry.Category,
            DataType = entry.DataType,
            Description = entry.Description,
            DefaultValue = entry.DefaultValue,
            ConfiguredValue = HttpContext.RequestServices.GetRequiredService<IConfiguration>()[entry.Key] ?? entry.DefaultValue,
            CurrentValue = existing.Value,
            HasOverride = true,
            LastModifiedAt = existing.LastModifiedAt,
            LastModifiedBy = existing.LastModifiedBy,
            RestartRequired = entry.RestartRequired
        });
    }

    [HttpDelete("settings/{key}")]
    public async Task<IActionResult> ResetSetting(string key)
    {
        var existing = await _ctx.RuntimeSettings.FirstOrDefaultAsync(s => s.Key == key);
        if (existing != null)
        {
            _ctx.RuntimeSettings.Remove(existing);
            await _ctx.SaveChangesAsync();
            _logger.LogInformation("Admin {Admin} reset runtime setting {Key}", User.Identity?.Name, key);
        }
        return NoContent();
    }

    // ---------------------------------------------------------------------
    // Anonymous visitors — group UserAccessLog rows by (IpHash, UserAgent)
    // so the admin can see which uncatalogued browsers have used which
    // access codes. Authenticated rows are excluded (those visitors live
    // in the Users tab).
    // ---------------------------------------------------------------------

    [HttpGet("anonymous-visitors")]
    public async Task<ActionResult<IEnumerable<AnonymousVisitorDto>>> GetAnonymousVisitors([FromQuery] int limit = 200)
    {
        if (limit <= 0 || limit > 1000) limit = 200;

        // Pull every anonymous access-log row joined to the code it touched
        // (so we can list the codes the visitor has used). Then group in
        // memory by IP + UA — composite GROUP BY through EF on a string
        // column is supported on SqlServer but trips up on Sqlite at the
        // sizes we expect this table to be.
        var rows = await (
            from log in _ctx.Set<UserAccessLog>()
            where log.UserId == null
            join code in _ctx.AccessCodes on log.AccessCodeId equals code.Id
            from album in _ctx.Albums.Where(a => a.Id == code.AlbumId).DefaultIfEmpty()
            select new
            {
                log.IpAddress,
                log.UserAgent,
                log.AccessDate,
                CodeId = code.Id,
                code.Code,
                AlbumTitle = album != null ? album.Title : code.DeletedAlbumTitle ?? "(album deleted)"
            }).ToListAsync();

        var grouped = rows
            .GroupBy(r => new { IpAddress = r.IpAddress ?? "(unknown)", UserAgent = r.UserAgent ?? "(unknown)" })
            .Select(g => new AnonymousVisitorDto
            {
                IpAddress = g.Key.IpAddress,
                UserAgent = g.Key.UserAgent,
                AccessCount = g.Count(),
                FirstSeenAt = g.Min(r => r.AccessDate),
                LastSeenAt = g.Max(r => r.AccessDate),
                Codes = g.GroupBy(r => r.CodeId)
                    .Select(cg => new AnonymousVisitorCodeDto
                    {
                        CodeId = cg.Key.ToString(),
                        Code = cg.First().Code,
                        AlbumTitle = cg.First().AlbumTitle,
                        UseCount = cg.Count(),
                        LastUsedAt = cg.Max(r => r.AccessDate)
                    })
                    .OrderByDescending(c => c.LastUsedAt)
                    .ToList()
            })
            .OrderByDescending(v => v.LastSeenAt)
            .Take(limit)
            .ToList();

        return Ok(grouped);
    }

    // ---------------------------------------------------------------------
    // Service health — single snapshot endpoint that powers the admin
    // "Service Health" tab. Returns worker schedules, queue stats, and the
    // photo-counts the admin asked for.
    // ---------------------------------------------------------------------

    [HttpGet("service-health")]
    public async Task<ActionResult<ServiceHealthDto>> GetServiceHealth()
    {
        var photoStatuses = await _ctx.Photos
            .GroupBy(p => p.ProcessingStatus)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync();
        int countFor(PhotoProcessingStatus s) =>
            photoStatuses.FirstOrDefault(x => x.Status == s)?.Count ?? 0;

        var queueByStatus = await _ctx.ProcessingQueueItems
            .GroupBy(i => i.Status)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync();
        int queueFor(PhotoGallery.Models.ProcessingStatus s) =>
            queueByStatus.FirstOrDefault(x => x.Key == s)?.Count ?? 0;

        var queueByQuality = await _ctx.ProcessingQueueItems
            .Where(i => i.Status == PhotoGallery.Models.ProcessingStatus.Pending ||
                        i.Status == PhotoGallery.Models.ProcessingStatus.Processing)
            .GroupBy(i => i.Quality)
            .Select(g => new { Quality = g.Key.ToString(), Count = g.Count() })
            .ToListAsync();

        var registry = HttpContext.RequestServices.GetRequiredService<WorkerScheduleRegistry>();
        var workers = registry.Snapshot();

        return Ok(new ServiceHealthDto
        {
            GeneratedAt = DateTime.UtcNow,
            Photos = new PhotoCountsDto
            {
                Total = photoStatuses.Sum(x => x.Count),
                Uploading = countFor(PhotoProcessingStatus.Uploading),
                Pending = countFor(PhotoProcessingStatus.Pending),
                Processing = countFor(PhotoProcessingStatus.Processing),
                Complete = countFor(PhotoProcessingStatus.Complete),
                Failed = countFor(PhotoProcessingStatus.Failed)
            },
            Queue = new QueueCountsDto
            {
                Pending = queueFor(PhotoGallery.Models.ProcessingStatus.Pending),
                Processing = queueFor(PhotoGallery.Models.ProcessingStatus.Processing),
                Complete = queueFor(PhotoGallery.Models.ProcessingStatus.Complete),
                Error = queueFor(PhotoGallery.Models.ProcessingStatus.Error),
                ByQuality = queueByQuality.ToDictionary(x => x.Quality, x => x.Count)
            },
            Workers = workers
        });
    }

    [HttpPost("service-health/workers/{name}/trigger")]
    public IActionResult TriggerWorker(string name)
    {
        var registry = HttpContext.RequestServices.GetRequiredService<WorkerScheduleRegistry>();
        if (!registry.Trigger(name))
            return NotFound(new { error = $"Worker '{name}' is not registered or does not support manual trigger." });
        _logger.LogInformation("Admin {Admin} triggered worker {Worker} manually", User.Identity?.Name, name);
        return Accepted(new { worker = name, status = "Triggered. Next tick will fire immediately." });
    }
}

public class AdminUserDto
{
    public string Id { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public List<string> Roles { get; set; } = new();
    public DateTime? LastLoginAt { get; set; }
    public int LoginCount { get; set; }
    public DateTime CreatedDate { get; set; }
    public bool IsActive { get; set; }
    public int AlbumCount { get; set; }
    public int DownloadCount { get; set; }
}

public class AdminUserPage
{
    public List<AdminUserDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public bool HasMore { get; set; }
}

public class RuntimeSettingDto
{
    public string Key { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string DefaultValue { get; set; } = string.Empty;
    public string ConfiguredValue { get; set; } = string.Empty;
    public string CurrentValue { get; set; } = string.Empty;
    public bool HasOverride { get; set; }
    public bool RestartRequired { get; set; }
    public DateTime? LastModifiedAt { get; set; }
    public string? LastModifiedBy { get; set; }
}

public class UpdateSettingRequest
{
    public string Value { get; set; } = string.Empty;
}

public class AnonymousVisitorDto
{
    public string IpAddress { get; set; } = string.Empty;
    public string UserAgent { get; set; } = string.Empty;
    public int AccessCount { get; set; }
    public DateTime FirstSeenAt { get; set; }
    public DateTime LastSeenAt { get; set; }
    public List<AnonymousVisitorCodeDto> Codes { get; set; } = new();
}

public class AnonymousVisitorCodeDto
{
    public string CodeId { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string AlbumTitle { get; set; } = string.Empty;
    public int UseCount { get; set; }
    public DateTime LastUsedAt { get; set; }
}

public class ServiceHealthDto
{
    public DateTime GeneratedAt { get; set; }
    public PhotoCountsDto Photos { get; set; } = new();
    public QueueCountsDto Queue { get; set; } = new();
    public List<WorkerStatusDto> Workers { get; set; } = new();
}

public class PhotoCountsDto
{
    public int Total { get; set; }
    public int Uploading { get; set; }
    public int Pending { get; set; }
    public int Processing { get; set; }
    public int Complete { get; set; }
    public int Failed { get; set; }
}

public class QueueCountsDto
{
    public int Pending { get; set; }
    public int Processing { get; set; }
    public int Complete { get; set; }
    public int Error { get; set; }
    public Dictionary<string, int> ByQuality { get; set; } = new();
}

public class WorkerStatusDto
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public TimeSpan Interval { get; set; }
    public DateTime? LastRanAt { get; set; }
    public DateTime? NextRunAt { get; set; }
    public bool CanTrigger { get; set; }
}

public class SetRolesRequest
{
    public List<string> Roles { get; set; } = new();
}

public class AlbumDownloadStatDto
{
    public string AlbumId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public int DownloadCount { get; set; }
    public DateTime? LastDownloadedAt { get; set; }
}

public class TopPhotoDto
{
    public string PhotoId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string AlbumId { get; set; } = string.Empty;
    public string AlbumTitle { get; set; } = string.Empty;
    public int DownloadCount { get; set; }
    public DateTime? LastDownloadedAt { get; set; }
}

public class UserDownloadDto
{
    public string DownloadId { get; set; } = string.Empty;
    public string PhotoId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string AlbumId { get; set; } = string.Empty;
    public string AlbumTitle { get; set; } = string.Empty;
    public string Quality { get; set; } = string.Empty;
    public DateTime DownloadedAt { get; set; }
}

public class AccessCodeStatDto
{
    public string CodeId { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string AlbumId { get; set; } = string.Empty;
    public string AlbumTitle { get; set; } = string.Empty;
    public bool IsDeleted { get; set; }
    public int AccessCount { get; set; }
    public int DistinctIps { get; set; }
    public int DistinctUserAgents { get; set; }
    public int PhotoDownloadCount { get; set; }
    public DateTime? LastAccessedAt { get; set; }
    public DateTime CreatedDate { get; set; }
    public DateTime? ExpirationDate { get; set; }
}
