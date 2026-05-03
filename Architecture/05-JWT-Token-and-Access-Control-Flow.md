graph LR
    subgraph "Admin Access (Authenticated)"
        AdminBrowser["Admin Browser<br/>JWT in localStorage"]
        Login["1. Login Page<br/>(Google OAuth)"]
        GoogleOAuth["2. Google OAuth<br/>Consent Screen"]
        Callback["3. OAuth Callback<br/>Extract JWT"]
        Token["4. Store JWT<br/>in localStorage"]
        Dashboard["5. Admin Dashboard<br/>Albums/Photos/Codes"]
    end
    
    subgraph "HTTP Layer"
        Interceptor["HTTP Interceptor<br/>Attach JWT to all requests<br/>Authorization: Bearer {JWT}"]
    end
    
    subgraph "Backend Processing"
        Middleware["Auth Middleware<br/>Verify JWT signature<br/>Extract claims"]
        Claims["JWT Claims<br/>- sub (user id)<br/>- email<br/>- role (Admin)<br/>- exp (expiration)"]
        AuthCheck["Check Authorization<br/>[Authorize(Roles='Admin')]"]
        Process["Process Request<br/>Return response"]
    end
    
    subgraph "Visitor Access (Code-Based)"
        VisitorBrowser["Client Browser<br/>No authentication"]
        EnterCode["1. Enter Access Code<br/>Form"]
        ValidateCode["2. Validate Code<br/>Check expiration"]
        VisitorToken["3. Generate Visitor JWT<br/>Claims include access_code"]
        ViewGallery["4. View Photo Gallery<br/>Download with code"]
    end
    
    subgraph "Token Expiration"
        CheckExp["Check exp claim"]
        ExpiredPath["Token Expired<br/>Redirect to login"]
        ValidPath["Token Valid<br/>Continue"]
    end
    
    AdminBrowser -->|user clicks Login| Login
    Login -->|redirect to| GoogleOAuth
    GoogleOAuth -->|user approves| Callback
    Callback -->|JWT issued| Token
    Token -->|stored| AdminBrowser
    
    AdminBrowser -->|makes API request| Interceptor
    Interceptor -->|attaches JWT| Dashboard
    Dashboard -->|requests to backend| Middleware
    
    Middleware -->|validates| Claims
    Claims -->|extracts role| AuthCheck
    AuthCheck -->|Admin role present| Process
    Process -->|response| Dashboard
    
    VisitorBrowser -->|enters code| EnterCode
    EnterCode -->|POST /code/validate| ValidateCode
    ValidateCode -->|code is valid| VisitorToken
    VisitorToken -->|stored in browser| VisitorBrowser
    VisitorBrowser -->|API requests| Interceptor
    Interceptor -->|attaches visitor JWT| ViewGallery
    ViewGallery -->|download request| Middleware
    Middleware -->|validates code claim| Process
    Process -->|file download| ViewGallery
    
    Dashboard -->|API call| Interceptor
    Interceptor -->|includes JWT| Middleware
    Middleware -->|validate JWT| CheckExp
    CheckExp -->|not expired| ValidPath
    CheckExp -->|expired| ExpiredPath
    ExpiredPath -->|redirect| Login
    ValidPath -->|process| AuthCheck
    
    style AdminBrowser fill:#c8e6c9
    style Login fill:#c8e6c9
    style GoogleOAuth fill:#fff9c4
    style Token fill:#c8e6c9
    style Dashboard fill:#c8e6c9
    
    style Interceptor fill:#b3e5fc
    
    style Middleware fill:#ffccbc
    style Claims fill:#ffccbc
    style AuthCheck fill:#ffccbc
    style Process fill:#ffccbc
    
    style VisitorBrowser fill:#f8bbd0
    style EnterCode fill:#f8bbd0
    style ValidateCode fill:#ffccbc
    style VisitorToken fill:#f8bbd0
    style ViewGallery fill:#f8bbd0
    
    style CheckExp fill:#ffe0b2
    style ExpiredPath fill:#ef9a9a
    style ValidPath fill:#c8e6c9
