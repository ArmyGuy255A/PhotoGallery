# Database Schema

**📍 Navigation**
- 🏠 [Documentation Index](../INDEX.md)
- 🏗️ [Design Decisions](./DESIGN_DECISIONS.md) - All approved design decisions
- 🏗️ [System Architecture](./SYSTEM_ARCHITECTURE.md) - Component overview
- 🔌 [API Design](./API_DESIGN.md) - REST endpoint patterns
- 📦 [Storage Layer](./STORAGE_LAYER.md) - File storage abstraction
- 🔐 [Authentication](./AUTHENTICATION.md) - OAuth and JWT patterns
- 📚 [All Guides](../Guides/) - TDD, Docker, CI/CD, Startup

---

# PhotoGallery Database Schema

## Entity Relationship Diagram

```mermaid
erDiagram
    User ||--o{ Album : creates
    User ||--o{ AccessCode : grants
    Album ||--o{ Photo : contains
    Album ||--o{ AccessCode : "uses"
    Photo ||--o{ PhotoVersion : generates
    Photo ||--o{ ProcessingQueue : queues
    User ||--o{ UserAccessLog : logs

    User {
        guid Id PK
        string Email UK "Unique"
        string GoogleId
        string[] Roles
        datetime CreatedAt
        datetime UpdatedAt
    }
    
    Album {
        guid Id PK
        string Title
        string Description
        guid CreatorId FK
        int PhotoCount
        datetime CreatedAt
        datetime UpdatedAt
    }
    
    Photo {
        guid Id PK
        guid AlbumId FK
        string FileName
        string StorageKey
        int FileSizeBytes
        datetime UploadedAt
    }
    
    PhotoVersion {
        guid Id PK
        guid PhotoId FK
        string Quality
        string StorageKey
        int FileSizeBytes
        datetime CreatedAt
    }
    
    ProcessingQueue {
        guid Id PK
        guid PhotoId FK
        string Status
        int RetryCount
        string ErrorMessage
        datetime QueuedAt
        datetime ProcessedAt
    }
    
    AccessCode {
        guid Id PK
        guid AlbumId FK
        string Code UK
        datetime ExpiresAt
        int AccessCount
        datetime CreatedAt
    }
    
    UserAccessLog {
        guid Id PK
        guid UserId FK
        guid AlbumId FK
        datetime AccessedAt
    }
```

## Detailed Entity Definitions

See [DESIGN_DECISIONS.md](./DESIGN_DECISIONS.md) for context on how data is structured.

---

**Last Updated**: 2026-05-03  
**Status**: Reference document (see System Architecture for full details)  
**Related**: [Design Decisions](./DESIGN_DECISIONS.md) • [API Design](./API_DESIGN.md)
