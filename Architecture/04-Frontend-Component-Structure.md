graph TB
    subgraph "Admin Dashboard"
        Sidebar["Sidebar Navigation<br/>- Dashboard<br/>- Albums<br/>- Settings"]
        Header["Header<br/>- User Menu<br/>- Logout"]
    end
    
    subgraph "Album Management Pages"
        AlbumList["Albums List Page<br/>- Table with albums<br/>- Create button<br/>- Edit/Delete actions"]
        AlbumForm["Album Create/Edit<br/>- Title input<br/>- Description textarea<br/>- Submit button"]
    end
    
    subgraph "Photo Management Pages"
        PhotoUpload["Photo Upload<br/>- Drag & drop zone<br/>- Progress bar<br/>- File list"]
        AccessCode["Access Code Form<br/>- Expiration type select<br/>- Days/Date picker<br/>- Generate button"]
    end
    
    subgraph "Services Layer"
        AuthSvc["AuthService<br/>- JWT token mgmt<br/>- Role checking<br/>- Login/logout"]
        AlbumSvc["AlbumService<br/>- GET /albums<br/>- POST /albums<br/>- PUT /albums/{id}<br/>- DELETE /albums/{id}"]
        PhotoSvc["PhotoService<br/>- POST /photos/upload<br/>- GET /photos"]
        CodeSvc["AccessCodeService<br/>- POST /access-codes<br/>- GET /validate"]
    end
    
    subgraph "HTTP Layer"
        Interceptor["HTTP Interceptor<br/>- Attach JWT token<br/>- Handle errors"]
        AuthGuard["Auth Guard<br/>- Check authentication<br/>- Redirect to login"]
        AdminGuard["Admin Guard<br/>- Check Admin role<br/>- Redirect if unauthorized"]
    end
    
    subgraph "Utilities"
        FormValidation["Reactive Forms<br/>- Validators<br/>- Error display"]
        Router["Angular Router<br/>- Navigation<br/>- Guards"]
    end
    
    subgraph "Visitor Pages"
        GalleryLogin["Login via Code<br/>- Enter access code<br/>- Validate"]
        Gallery["Photo Gallery<br/>- Grid of photos<br/>- Click to open"]
        PhotoModal["Photo Modal<br/>- Display photo<br/>- Quality selector<br/>- Download button"]
    end
    
    Header -->|calls| AuthSvc
    Header -->|uses| Router
    
    AlbumList -->|calls| AlbumSvc
    AlbumList -->|uses| FormValidation
    AlbumForm -->|calls| AlbumSvc
    AlbumForm -->|uses| FormValidation
    
    PhotoUpload -->|calls| PhotoSvc
    PhotoUpload -->|shows| FormValidation
    AccessCode -->|calls| CodeSvc
    AccessCode -->|uses| FormValidation
    
    AlbumSvc -->|uses| Interceptor
    PhotoSvc -->|uses| Interceptor
    CodeSvc -->|uses| Interceptor
    AuthSvc -->|uses| Interceptor
    
    AlbumList -->|uses| AuthGuard
    AlbumList -->|uses| AdminGuard
    AlbumForm -->|uses| AuthGuard
    AlbumForm -->|uses| AdminGuard
    PhotoUpload -->|uses| AuthGuard
    AccessCode -->|uses| AuthGuard
    
    Sidebar -->|links to| AlbumList
    Sidebar -->|links to| PhotoUpload
    Sidebar -->|links to| AccessCode
    
    GalleryLogin -->|calls| CodeSvc
    Gallery -->|calls| PhotoSvc
    Gallery -->|navigates to| PhotoModal
    PhotoModal -->|calls| PhotoSvc
    
    Gallery -->|skips| AuthGuard
    PhotoModal -->|skips| AuthGuard
    
    Router -->|enforces| AuthGuard
    Router -->|enforces| AdminGuard
    Router -->|navigates between| AlbumList
    Router -->|navigates between| AlbumForm
    Router -->|navigates between| PhotoUpload
    Router -->|navigates between| Gallery
    
    style Header fill:#e1f5ff
    style Sidebar fill:#e1f5ff
    
    style AlbumList fill:#c8e6c9
    style AlbumForm fill:#c8e6c9
    style PhotoUpload fill:#c8e6c9
    style AccessCode fill:#c8e6c9
    
    style AuthSvc fill:#ffe0b2
    style AlbumSvc fill:#ffe0b2
    style PhotoSvc fill:#ffe0b2
    style CodeSvc fill:#ffe0b2
    
    style Interceptor fill:#f8bbd0
    style AuthGuard fill:#f8bbd0
    style AdminGuard fill:#f8bbd0
    
    style GalleryLogin fill:#b2dfdb
    style Gallery fill:#b2dfdb
    style PhotoModal fill:#b2dfdb
