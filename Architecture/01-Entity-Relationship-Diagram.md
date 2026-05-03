```mermaid
erDiagram
    APPLICATION_USER ||--o{ ALBUM : owns
    APPLICATION_USER ||--o{ ACCESS_CODE : creates
    APPLICATION_USER ||--o{ PHOTO : uploads
    APPLICATION_USER ||--o{ USER_ACCESS_LOG : "accesses via"
    ALBUM ||--o{ PHOTO : contains
    ALBUM ||--o{ ACCESS_CODE : "has access codes"
    PHOTO ||--o{ PHOTO_VERSION : "has versions"
    ACCESS_CODE ||--o{ USER_ACCESS_LOG : "associated with"
    D:\repos\CleanArchitecture
    APPLICATION_USER {
        string Id PK
        string Email UK
        string ExternalProvider
        string ExternalId
        datetime LastLoginAt
    }
    
    ALBUM {
        guid Id PK
        string Title
        string Description
        string OwnerId FK
        datetime CreatedDate
        string CreatedBy
    }
    
    PHOTO {
        guid Id PK
        guid AlbumId FK
        string FileName
        string StorageKey
        datetime UploadDate
        string UploadedBy
        string Metadata
    }
    
    PHOTO_VERSION {
        guid Id PK
        guid PhotoId FK
        string Quality "High/Medium/Low/Raw"
        string StorageKey
        int FileSize
        datetime ProcessedDate
    }
    
    ACCESS_CODE {
        guid Id PK
        guid AlbumId FK
        string Code UK
        datetime ExpirationDate "nullable"
        datetime CreatedDate
        string CreatedBy
    }
    
    USER_ACCESS_LOG {
        string UserId FK
        guid AccessCodeId FK
        datetime AccessDate
    }
```