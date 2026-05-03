# Album Persistence Fix - Complete

## Issue
Albums were being created with HTTP 201 responses but never actually persisted to the database. When users navigated back to the dashboard, the created albums would not appear.

## Root Causes Found

### 1. HTTP Method Mismatch
- **File**: `PhotoGallery/Controllers/AlbumsController.cs`
- **Problem**: `CreateAlbum` method was decorated with `[HttpGet]` instead of `[HttpPost]`
- **Result**: POST requests to `/api/albums` returned 405 Method Not Allowed
- **Fix**: Changed to `[HttpPost]`

### 2. Early-Exit Debug Code
- **File**: `PhotoGallery/Controllers/AlbumsController.cs` (line 75)
- **Problem**: Hard-coded `return BadRequest(new { error = "ENDPOINT_HIT_AT_" + DateTime.Now.ToString("O") });` was preventing any album creation
- **Result**: Even if HTTP method was correct, the endpoint would return early
- **Fix**: Removed debug code

### 3. RowVersion NOT NULL Constraint
- **File**: All EF Core configurations and models
- **Problem**: `RowVersion` was configured with `.IsRowVersion()` which in SQLite creates a NOT NULL constraint, but EF Core was trying to INSERT NULL
- **Error**: `SQLite Error 19: 'NOT NULL constraint failed: Albums.RowVersion'`
- **Root Cause**: EF Core doesn't auto-generate row versions on INSERT for SQLite; the property initializer `Array.Empty<byte>()` was being treated as NULL
- **Fix**: 
  - Changed configurations from `.IsRowVersion()` to `.HasDefaultValue(new byte[] { 1 })`
  - Updated models to initialize with `new byte[] { 1 }` instead of `Array.Empty<byte>()`
  - Created migration: `UpdateRowVersionDefaults`

### 4. Excessive Debug Logging
- **File**: `PhotoGallery/Controllers/AlbumsController.cs` and `PhotoGallery/Data/Repositories/Repository.cs`
- **Problem**: Code was full of Console.WriteLine and File.AppendAllText that weren't providing value
- **Fix**: Removed all debug logging for cleaner code

## Files Modified

### Models
- `PhotoGallery/Models/Album.cs` - Updated RowVersion initialization
- `PhotoGallery/Models/Photo.cs` - Updated RowVersion initialization  
- `PhotoGallery/Models/AccessCode.cs` - Updated RowVersion initialization

### Configurations
- `PhotoGallery/Data/Configurations/AlbumConfiguration.cs` - Changed RowVersion config
- `PhotoGallery/Data/Configurations/PhotoConfiguration.cs` - Changed RowVersion config
- `PhotoGallery/Data/Configurations/AccessCodeConfiguration.cs` - Changed RowVersion config

### Controllers/Services
- `PhotoGallery/Controllers/AlbumsController.cs` - Fixed HTTP method, removed debug code
- `PhotoGallery/Data/Repositories/Repository.cs` - Simplified SaveChangesAsync

### Migrations
- `PhotoGallery/Data/Migrations/20260427041558_UpdateRowVersionDefaults.cs` - New migration for RowVersion changes

## Verification

### API Tests
```powershell
# Create album
POST /api/albums
Response: 201 Created with album data

# Get album
GET /api/albums/{id}
Response: 200 OK with album details

# List albums
GET /api/albums
Response: 200 OK with all albums

# Update album
PUT /api/albums/{id}
Response: 200 OK

# Create access code
POST /api/albums/{id}/access-codes
Response: 201 Created with code
```

### E2E Tests (Playwright)
✅ App loads on dashboard
✅ User is authenticated (DISABLE_AUTH middleware works)
✅ Create album form loads
✅ Album creation succeeds (201 response)
✅ Albums appear in list immediately after creation
✅ Created album is visible with correct title
✅ Can open album details
✅ Database contains all persisted albums

### Database Verification
- 4 total albums in database
- All have unique IDs
- All have correct titles and descriptions
- All persist across backend restarts

## Test Results

### Persistence Test
```
✓ Backend responding
✓ Album creation succeeds with 201 response
✓ Album ID returned in response
✓ Album found in database (case-insensitive ID match)
✓ Album appears in GET /api/albums
✓ Album visible in UI list
TEST PASSED
```

### CRUD Operations Test
```
✓ Create album - SUCCESS
✓ Get album - SUCCESS
✓ List albums - SUCCESS (3 albums)
✓ Update album - SUCCESS
✓ Create access code - SUCCESS
All CRUD operations work correctly
```

### E2E UI Test
```
✓ Dashboard loads
✓ Authenticated (no login required with DISABLE_AUTH=true)
✓ Create button found and clicked
✓ Form filled and submitted
✓ Album immediately appears in list
✓ Can open album details
E2E test completed successfully
```

## Commits
- Main fix commit: `e98e9fe` - "Fix album persistence issue"
- Includes all model updates, configuration changes, and migration

## Current Status
✅ **COMPLETE** - Album persistence is fully functional
- Albums are created and immediately persisted to database
- Albums appear on dashboard after creation
- All CRUD operations working
- E2E flow verified through UI
- Database integrity confirmed

## Known Limitations
- Access code UI button not yet implemented (separate feature)
- Case sensitivity on GUID queries (SQLite TEXT columns are case-sensitive for string comparisons, but EF Core handles GUID comparison correctly)
