# PhotoGallery - Professional Photo Sharing Platform

A modern web application for photographers to upload, organize, and securely share photos with clients using time-limited access codes.

## Features

### 🔐 Authentication & Authorization
- **Google OAuth 2.0** - Secure login with Google accounts
- **JWT Tokens** - API authentication for frontend and clients
- **Role-Based Access Control** - Admin and User roles
- **Development Bypass** - DISABLE_AUTH flag for testing
- **Future Support** - Extensible for Facebook, Microsoft OAuth

### 📸 Album Management
- Create and organize photo albums
- Album ownership and access control
- Bulk photo uploads with progress tracking
- Photo metadata and organization

### 🎁 Access Codes
- Generate unique access codes for albums
- Configurable expiration dates (default 30 days)
- Time-limited guest access without authentication
- Access logging and tracking

### 🖼️ Photo Processing
- **4 Quality Levels**:
  - High compression (50% quality)
  - Medium compression (75% quality)
  - Low compression (85% quality)
  - Raw/Original (100% quality)
- Background image processing queue
- Automatic multi-quality generation
- User-selected download quality

### 💾 Storage Abstraction
- **Development**: MinIO (S3-compatible local storage)
- **Production**: Azure Blob Storage
- Configurable via appsettings.json
- Easy provider switching

### 🧪 Testing
- Backend unit tests (xUnit)
- Frontend E2E tests (Playwright)
- CI/CD integration (GitHub Actions)

## Technology Stack

### Backend
- **Framework**: ASP.NET 9.0
- **Database**: SQLite (dev), SQL Server (prod)
- **ORM**: Entity Framework Core
- **Image Processing**: SixLabors.ImageSharp
- **Authentication**: Google OAuth 2.0, JWT, ASP.NET Identity
- **Storage**: MinIO/Azure SDKs

### Frontend
- **Framework**: Angular 19.2
- **Styling**: Bootstrap 5.3
- **HTTP**: RxJS with JWT interceptor
- **Testing**: Playwright

### Infrastructure
- **Containerization**: Docker & Docker Compose
- **CI/CD**: GitHub Actions
- **Development**: Dev containers (VS Code)

## Quick Start

### Prerequisites
- Docker & Docker Compose 2.0+
- OR: .NET 9 SDK + Node.js 20 LTS (for local development)

### Using Docker Compose (Recommended)

```bash
# Clone repository
git clone https://github.com/YOUR_ORG/PhotoGallery.git
cd PhotoGallery

# Start all services
docker-compose up -d

# Access services
# Frontend: http://localhost:4200
# Backend API: http://localhost:5105
# MinIO Console: http://localhost:9001 (admin/minioadmin-password)
```

### Local Development (Without Docker)

**Backend:**
```bash
cd PhotoGallery
dotnet run
# Backend runs on http://localhost:5105
```

**Frontend:**
```bash
cd FE.PhotoGallery
npm install
ng serve
# Frontend runs on http://localhost:4200
```

**MinIO (in Docker):**
```bash
docker run -p 9000:9000 -p 9001:9001 minio/minio server /data
```

## Development

### Project Structure

```
PhotoGallery/
├── PhotoGallery/                 # Backend ASP.NET project
│   ├── Controllers/              # API endpoints
│   ├── Models/                   # Domain entities
│   ├── Services/                 # Business logic
│   ├── Data/                     # EF Core context & migrations
│   └── Program.cs                # DI & middleware setup
├── PhotoGallery.Tests/           # Backend unit tests
├── FE.PhotoGallery/              # Frontend Angular project
│   ├── src/app/                  # Angular components & services
│   ├── e2e/                      # Playwright E2E tests
│   └── dist/                     # Built app (production)
├── Architecture/                 # Architecture diagrams & docs
├── docker-compose.yml            # Multi-service orchestration
├── DOCKER_SETUP.md               # Docker documentation
├── CI_CD_SETUP.md                # GitHub Actions setup guide
└── README.md                     # This file
```

### Environment Variables

**Development** (`.env.development`):
- `DISABLE_AUTH=true` - Bypass authentication for testing
- `Storage__Provider=Minio` - Use MinIO for file storage
- Auto-seeded test user: `testadmin@localhost`

**Production** (`.env.production.local`):
- `DISABLE_AUTH=false` - Enable authentication
- `Storage__Provider=Azure` - Use Azure Blob Storage
- Real Google OAuth credentials required

## API Endpoints

### Authenticated Routes (require JWT token)

```
GET    /api/albums                    # List user's albums
POST   /api/albums                    # Create new album (admin only)
GET    /api/albums/{id}               # Get album details
PUT    /api/albums/{id}               # Update album (owner only)
DELETE /api/albums/{id}               # Delete album (admin only)
GET    /api/albums/{id}/photos        # List photos in album
GET    /api/albums/{id}/access-codes  # List access codes (owner only)
POST   /api/albums/{id}/access-codes  # Create access code (admin only)
DELETE /api/albums/{id}/access-codes/{codeId}  # Delete code (admin only)
POST   /api/photos/albums/{id}        # Upload photos (multi-file)
GET    /api/photos/{id}/download      # Download photo by quality
```

### Public Routes (code-based access)

```
GET /api/code/{code}/validate         # Validate access code
GET /api/code/{code}/photos           # List album photos
GET /api/code/{code}/photo/{id}/download  # Download via code
```

### Authentication Routes

```
GET    /api/auth/login                # Initiate Google OAuth flow
GET    /api/auth/google-callback      # OAuth callback
POST   /api/auth/logout               # Logout
GET    /api/auth/me                   # Current user info
POST   /api/auth/refresh              # Refresh JWT token
```

## Testing

### Backend Unit Tests
```bash
cd PhotoGallery.Tests
dotnet test
# Output: Passed: 10, Failed: 0
```

### Frontend Unit Tests
```bash
cd FE.PhotoGallery
npm test
```

### E2E Tests (requires services running)
```bash
cd FE.PhotoGallery
npm run e2e
# Or with UI:
npm run e2e:ui
# Or headed (see browser):
npm run e2e:headed
```

## CI/CD Pipeline

GitHub Actions automatically:
1. Builds backend (.NET) and frontend (Angular)
2. Runs unit tests
3. Runs E2E tests (on PR)
4. Scans for vulnerabilities
5. Builds Docker images (on main branch)

View workflows: `.github/workflows/`

For setup instructions, see `CI_CD_SETUP.md`

## Database

### Migrations

Backend uses EF Core Code-First approach:

```bash
# Create migration
dotnet ef migrations add FeatureName -p PhotoGallery

# Update database
dotnet ef database update -p PhotoGallery
```

### Database Schema

- **Users** (AspNetUsers) - Identity users with OAuth metadata
- **Roles** (AspNetRoles) - Admin, User roles
- **Albums** - Photo collections owned by users
- **Photos** - Individual photos with upload metadata
- **PhotoVersions** - Multi-quality processed versions
- **AccessCodes** - Time-limited album access codes
- **UserAccessLogs** - Track guest access

## Configuration

### Backend Settings (appsettings.json)

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=app.db"
  },
  "Storage": {
    "Provider": "Minio",
    "Minio": {
      "Endpoint": "localhost:9000",
      "AccessKey": "minioadmin",
      "SecretKey": "minioadmin-password"
    }
  },
  "Authentication": {
    "Google": {
      "ClientId": "YOUR_CLIENT_ID",
      "ClientSecret": "YOUR_CLIENT_SECRET"
    }
  }
}
```

### Frontend Configuration (app.config.ts)

```typescript
export const appConfig: ApplicationConfig = {
  providers: [
    provideRouter(routes),
    provideHttpClient(
      withInterceptors([jwtInterceptor])
    )
  ]
};
```

## Deployment

### Docker Deployment

```bash
# Build and push images to registry
docker build -t your-registry/photogallery-backend:latest -f Dockerfile.backend .
docker build -t your-registry/photogallery-frontend:latest -f FE.PhotoGallery/Dockerfile .

# Deploy using docker-compose or Kubernetes
```

### Cloud Platforms

- **Azure Container Instances** - Simple containerized deployment
- **Kubernetes** - Multi-container orchestration
- **App Service** - Managed Azure platform
- **AWS ECS/Fargate** - AWS container services

For production deployment, ensure:
1. Real database connection
2. Azure Storage configuration
3. Google OAuth credentials
4. HTTPS certificates
5. Proper JWT secret

## Troubleshooting

### Backend Issues

**"Cannot connect to MinIO"**
- Check MinIO is running: `docker-compose ps minio`
- Verify bucket exists in MinIO console

**"DbContext disposal error"**
- This is fixed in Phase 5 via singleton registration
- ImageProcessingService creates new scopes per iteration

**Database migration errors**
- Delete `app.db` and restart to reset: `rm PhotoGallery/app.db`

### Frontend Issues

**"Cannot connect to API"**
- Check backend is running on port 5105
- Verify CORS is enabled on backend
- Check JWT interceptor is registered in app.config.ts

**"401 Unauthorized"**
- Token expired or invalid
- Clear localStorage: `localStorage.clear()`
- Refresh and re-login

### Docker Issues

See `DOCKER_SETUP.md` for common Docker troubleshooting

## Contributing

1. Create feature branch: `git checkout -b feature/your-feature`
2. Make changes with clear commit messages
3. Push to remote: `git push origin feature/your-feature`
4. Create Pull Request with description
5. Wait for CI/CD to pass
6. Code review approval
7. Merge to main

## Architecture & Design

PhotoGallery follows **Clean Architecture** principles:
- **Domain Layer** - Models, entities, business rules
- **Application Layer** - Services, repositories, DTOs
- **Infrastructure Layer** - EF Core, storage providers
- **Presentation Layer** - API controllers, Angular components

See `Architecture/` folder for detailed diagrams and documentation.

## Future Enhancements

- [ ] Facebook and Microsoft OAuth
- [ ] Bulk download as ZIP
- [ ] Watermarking
- [ ] Face recognition for auto-organization
- [ ] Social sharing integrations
- [ ] Payment integration for premium features
- [ ] Mobile app (React Native)
- [ ] CDN integration for performance

## License

[Your License Here]

## Support

For issues and questions:
1. Check troubleshooting sections
2. Review GitHub issues
3. Create new issue with details
4. Join community discussions

## Credits

Built with:
- ASP.NET Core team
- Angular team
- Docker community
- Open-source libraries

---

**Happy uploading! 📸**
