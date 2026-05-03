# API Design

**📍 Navigation**
- 🏠 [Documentation Index](../INDEX.md)
- 🏗️ [Design Decisions](./DESIGN_DECISIONS.md) - All approved design decisions
- 🏗️ [System Architecture](./SYSTEM_ARCHITECTURE.md) - Component overview
- 💾 [Database Schema](./DATABASE_SCHEMA.md) - Entity relationships
- 📦 [Storage Layer](./STORAGE_LAYER.md) - File storage abstraction
- 🔐 [Authentication](./AUTHENTICATION.md) - OAuth and JWT patterns
- 📚 [All Guides](../Guides/) - TDD, Docker, CI/CD, Startup

---

# PhotoGallery API Design

## Base URL

```
Development: http://localhost:5105/api
Production: https://api.photogallery.com/api
```

## Authentication

All authenticated endpoints require JWT Bearer token:

```
Authorization: Bearer {jwt_token}
```

Token obtained from `/api/auth/google-callback` after Google OAuth flow.

See [Authentication.md](./AUTHENTICATION.md) for complete flow.

## Response Format

```json
{
  "data": { /* response payload */ },
  "status": 200,
  "message": "Success",
  "errors": null,
  "timestamp": "2026-05-03T10:00:00Z"
}
```

## Error Handling

```json
{
  "data": null,
  "status": 400,
  "message": "Validation failed",
  "errors": [
    {
      "field": "email",
      "message": "Email is required"
    }
  ],
  "timestamp": "2026-05-03T10:00:00Z"
}
```

---

## Authenticated Endpoints (Admin)

All require `Authorization: Bearer {jwt}` header.

### Albums

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/albums` | List all albums (admin only) |
| `POST` | `/albums` | Create new album |
| `GET` | `/albums/{id}` | Get album details |
| `PUT` | `/albums/{id}` | Update album |
| `DELETE` | `/albums/{id}` | Delete album |

### Photos

| Method | Path | Description |
|--------|------|-------------|
| `POST` | `/photos/albums/{albumId}` | Upload photos to album |
| `GET` | `/photos/{id}/download` | Download photo by quality |
| `GET` | `/photos/compression-profiles` | Get available quality options |
| `GET` | `/photos/processing-status/{jobId}` | Check upload processing status |

### Access Codes

| Method | Path | Description |
|--------|------|-------------|
| `POST` | `/access-codes` | Generate access code for album |
| `GET` | `/access-codes/{albumId}` | List access codes for album |
| `PUT` | `/access-codes/{id}` | Update access code (expiration) |
| `DELETE` | `/access-codes/{id}` | Revoke access code |

### Authentication

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/auth/me` | Get current user info |
| `POST` | `/auth/logout` | Logout current user |
| `POST` | `/auth/refresh` | Refresh JWT token |

---

## Public Endpoints (No Authentication)

### Access Code Validation

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/code/{code}/validate` | Validate access code |
| `GET` | `/code/{code}/photos` | List photos for album (access code) |
| `GET` | `/code/{code}/photo/{photoId}` | Download photo (access code) |
| `GET` | `/code/{code}/compression-profiles` | Get quality options |

---

## Compression Profiles

Available quality levels for downloads:

```json
{
  "profiles": [
    {
      "name": "high",
      "quality": 50,
      "description": "High compression, smallest file"
    },
    {
      "name": "medium",
      "quality": 75,
      "description": "Balanced compression"
    },
    {
      "name": "low",
      "quality": 85,
      "description": "Low compression, larger file"
    },
    {
      "name": "raw",
      "quality": 100,
      "description": "Original quality, largest file"
    }
  ]
}
```

---

## Example Requests

### Upload Photos

```bash
curl -X POST http://localhost:5105/api/photos/albums/{albumId} \
  -H "Authorization: Bearer {token}" \
  -F "files=@photo1.jpg" \
  -F "files=@photo2.jpg"
```

Response:
```json
{
  "data": {
    "jobId": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
    "status": "queued",
    "estimatedTime": "30s"
  },
  "status": 201,
  "message": "Photos queued for processing"
}
```

### Download Photo

```bash
curl -X GET "http://localhost:5105/api/photos/{photoId}/download?quality=medium" \
  -H "Authorization: Bearer {token}" \
  -o photo.jpg
```

### Validate Access Code

```bash
curl -X GET http://localhost:5105/api/code/ABC123XYZ/validate
```

Response:
```json
{
  "data": {
    "albumId": "album-uuid",
    "albumTitle": "Summer 2026",
    "expiresAt": "2026-06-03T10:00:00Z",
    "isExpired": false
  },
  "status": 200,
  "message": "Access code valid"
}
```

---

## Design Principles

See [DESIGN_DECISIONS.md](./DESIGN_DECISIONS.md) for the reasoning behind these API patterns.

---

**Last Updated**: 2026-05-03  
**Status**: In Development  
**Related**: [Authentication](./AUTHENTICATION.md) • [Design Decisions](./DESIGN_DECISIONS.md)
