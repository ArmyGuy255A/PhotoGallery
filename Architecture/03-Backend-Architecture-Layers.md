graph TB
    subgraph "Presentation Layer (API Controllers)"
        AuthCtrl["AuthController<br/>- Google Callback<br/>- Logout"]
        AlbumsCtrl["AlbumsController<br/>- GET /albums<br/>- POST /albums<br/>- PUT /albums/{id}<br/>- DELETE /albums/{id}"]
        PhotosCtrl["PhotosController<br/>- POST /photos/upload<br/>- GET /photos/{id}"]
        AccessCtrl["AccessCodesController<br/>- POST /access-codes<br/>- GET /code/{code}"]
    end
    
    subgraph "Business Logic Layer (Services)"
        AuthSvc["AuthService<br/>- Google OAuth callback<br/>- JWT token generation<br/>- Role assignment"]
        AlbumSvc["AlbumService<br/>- Create album<br/>- List user albums<br/>- Update/delete"]
        PhotoSvc["PhotoService<br/>- Upload photo<br/>- Queue processing<br/>- Get versions"]
        AccessSvc["AccessCodeService<br/>- Generate code<br/>- Validate code<br/>- Check expiration"]
        ImgSvc["ImageProcessingService<br/>- Compress images<br/>- Generate versions<br/>- Queue processing"]
    end
    
    subgraph "Data Access Layer (Repositories)"
        UserRepo["IUserRepository"]
        AlbumRepo["IAlbumRepository"]
        PhotoRepo["IPhotoRepository"]
        AccessRepo["IAccessCodeRepository"]
        VersionRepo["IPhotoVersionRepository"]
    end
    
    subgraph "Domain Layer (Entities)"
        User["User Entity<br/>- Email<br/>- ExternalId<br/>- Role"]
        Album["Album Entity<br/>- Title<br/>- Description<br/>- OwnerId<br/>- CreatedDate"]
        Photo["Photo Entity<br/>- FileName<br/>- StorageKey<br/>- UploadDate"]
        Version["PhotoVersion Entity<br/>- Quality<br/>- StorageKey<br/>- FileSize"]
        Code["AccessCode Entity<br/>- Code<br/>- ExpirationDate<br/>- AlbumId"]
    end
    
    subgraph "Infrastructure"
        EF["Entity Framework Core<br/>+ SQLite/PostgreSQL"]
        Storage["IStorageProvider<br/>- Minio (dev)<br/>- Azure (prod)"]
        Config["Configuration<br/>appsettings.json<br/>Environment-based"]
    end
    
    AuthCtrl -->|delegates to| AuthSvc
    AlbumsCtrl -->|delegates to| AlbumSvc
    PhotosCtrl -->|delegates to| PhotoSvc
    AccessCtrl -->|delegates to| AccessSvc
    
    AlbumSvc -->|queries| AlbumRepo
    PhotoSvc -->|queries| PhotoRepo
    AccessSvc -->|queries| AccessRepo
    AuthSvc -->|queries| UserRepo
    ImgSvc -->|updates| PhotoRepo
    ImgSvc -->|creates| VersionRepo
    
    AlbumRepo -->|reads/writes| Album
    PhotoRepo -->|reads/writes| Photo
    AccessRepo -->|reads/writes| Code
    UserRepo -->|reads/writes| User
    VersionRepo -->|reads/writes| Version
    
    PhotoSvc -->|calls| ImgSvc
    PhotoSvc -->|stores files| Storage
    PhotoSvc -->|queues| ImgSvc
    ImgSvc -->|retrieves files| Storage
    ImgSvc -->|stores files| Storage
    
    AlbumRepo -->|ORM| EF
    PhotoRepo -->|ORM| EF
    AccessRepo -->|ORM| EF
    UserRepo -->|ORM| EF
    VersionRepo -->|ORM| EF
    
    AuthSvc -->|reads| Config
    PhotoSvc -->|reads| Config
    Storage -->|reads| Config
    
    style AuthCtrl fill:#e1f5ff
    style AlbumsCtrl fill:#e1f5ff
    style PhotosCtrl fill:#e1f5ff
    style AccessCtrl fill:#e1f5ff
    
    style AuthSvc fill:#c8e6c9
    style AlbumSvc fill:#c8e6c9
    style PhotoSvc fill:#c8e6c9
    style AccessSvc fill:#c8e6c9
    style ImgSvc fill:#c8e6c9
    
    style UserRepo fill:#ffe0b2
    style AlbumRepo fill:#ffe0b2
    style PhotoRepo fill:#ffe0b2
    style AccessRepo fill:#ffe0b2
    style VersionRepo fill:#ffe0b2
    
    style User fill:#f8bbd0
    style Album fill:#f8bbd0
    style Photo fill:#f8bbd0
    style Version fill:#f8bbd0
    style Code fill:#f8bbd0
    
    style EF fill:#b2dfdb
    style Storage fill:#b2dfdb
    style Config fill:#b2dfdb
