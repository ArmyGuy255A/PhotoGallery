# PhotoGallery Docker Setup

This document explains how to run PhotoGallery locally using Docker Compose.

## Prerequisites

- Docker 20.10+
- Docker Compose 2.0+
- 4GB RAM minimum for local development

## Quick Start

### 1. Build and Start Services

```bash
cd PhotoGallery
docker-compose up -d
```

This will start:
- **PostgreSQL** (port 5432) - Database for Keycloak
- **Keycloak** (port 8080) - OpenID Connect provider
- **MinIO** (ports 9000/9001) - S3-compatible object storage
- **Backend API** (port 5105) - ASP.NET PhotoGallery API
- **Frontend** (port 4200) - Angular PhotoGallery UI

### 2. Access the Services

- **Frontend**: http://localhost:4300
- **Backend API**: http://localhost:5105
- **MinIO Console**: http://localhost:9001 (admin/minioadmin-password)
- **Keycloak Console**: http://localhost:8080 (admin/admin-password)

### 3. Login

Since `DISABLE_AUTH=true` in development:
- **Email**: testadmin@localhost
- **Password**: (ignored - auto-authenticated)

## Configuration

### Development Environment (.env.development)

The development environment has:
- `DISABLE_AUTH=true` - Bypasses authentication for testing
- `Storage__Provider=Minio` - Uses MinIO for file storage
- Auto-seeded test user: testadmin@localhost

### Production Environment (.env.production.template)

Copy and fill in actual values:
```bash
cp .env.production.template .env.production.local
# Edit .env.production.local with real credentials
```

## Using MinIO

### Access MinIO Console
- URL: http://localhost:9001
- Username: minioadmin
- Password: minioadmin-password

### Create bucket

1. Go to http://localhost:9001
2. Click "Create Bucket"
3. Name it "photogallery"

## Using Keycloak (Future)

When ready to replace Keycloak with Google OAuth in production:

1. Go to http://localhost:8080/admin
2. Admin credentials: admin/admin-password
3. Create realm, clients, users as needed

## Common Commands

### Stop Services
```bash
docker-compose down
```

### View Logs
```bash
docker-compose logs -f backend
docker-compose logs -f frontend
docker-compose logs -f minio
docker-compose logs -f keycloak
```

### Restart a Service
```bash
docker-compose restart backend
```

### Rebuild After Code Changes
```bash
docker-compose up -d --build
```

### Remove Volumes (Reset Database)
```bash
docker-compose down -v
```

## Troubleshooting

### Backend cannot connect to MinIO
- Check MinIO is running: `docker-compose logs minio`
- Verify bucket "photogallery" exists in MinIO console

### Frontend cannot connect to Backend
- Verify CORS is enabled on backend
- Check API_URL in frontend matches backend port
- Ensure DISABLE_AUTH=true for development

### Database errors
- Check PostgreSQL is healthy: `docker-compose ps`
- Verify connection string in .env.development

### Port conflicts
- If ports are already in use, modify docker-compose.yml:
  ```yaml
  ports:
    - "5432:5432"  # Change left number to another port
  ```

## Development Workflow

### Local Development (Without Docker)

For faster iteration, run services locally:

#### Backend
```bash
cd PhotoGallery
dotnet run
```

#### Frontend
```bash
cd FE.PhotoGallery
npm install
ng serve
```

#### MinIO (in Docker)
```bash
docker run -p 9000:9000 -p 9001:9001 minio/minio server /data
```

## Next Steps

- Configure Google OAuth credentials
- Setup CI/CD pipeline
- Create production Docker images
- Deploy to cloud platform
