# .gitignore Configuration - PhotoGallery Project

## Overview
A comprehensive `.gitignore` file has been created for the PhotoGallery project. It protects sensitive information while allowing essential files to be tracked.

## Critical Security Protections

### 🔒 Secrets & Configuration Files (TOP PRIORITY)
```
appsettings.json              # Contains Google OAuth secrets
appsettings.*.json            # Environment-specific configs
.env                          # Environment variables
.env.local                    # Local environment overrides
**/Properties/secrets.json    # ASP.NET Core user secrets
```

**Why this matters:** 
- `appsettings.json` contains your Google OAuth ClientSecret
- `.env` files contain API keys and sensitive configuration
- These MUST never be committed to version control

### Template Files (Reference for Setup)
```
!appsettings.*.template.json  # Included so developers know what config is needed
!.env.template                # Shows required environment variables
```

## What Gets Excluded

### .NET / ASP.NET Core
- `bin/`, `obj/` - Build output
- `.vs/` - Visual Studio cache
- `*.csproj.user` - IDE user settings
- `.rider/` - JetBrains Rider cache
- `*.DotSettings.user` - ReSharper settings

### Angular / Node.js Frontend
- `node_modules/` - Dependencies
- `dist/` - Build output
- `FE.PhotoGallery/node_modules/`
- `FE.PhotoGallery/dist/`

### Databases
- `*.db`, `*.sqlite`, `*.sqlite3` - SQLite databases
- `app.db` - Application database
- `identifier.sqlite` - Identity database

### IDE & Editors
- `.vscode/` - VS Code settings
- `.idea/` - JetBrains IDEs
- Sublime Text workspace files
- Vim swap files (`*.swp`)

### OS Files
- `.DS_Store` - macOS metadata
- `Thumbs.db` - Windows thumbnails
- `Desktop.ini` - Windows folder display

### Testing & Temporary
- Test artifacts (`test_*.py`, `e2e_*.py`)
- Screenshot files (`*.png`, `*.jpg`)
- Python cache (`__pycache__/`)
- Playwright test results

## Setup Instructions for Developers

### For First-Time Setup

1. **Create appsettings files from templates:**
   ```bash
   cp PhotoGallery/appsettings.Development.template.json PhotoGallery/appsettings.Development.json
   ```

2. **Add your Google OAuth secrets:**
   - Edit `appsettings.Development.json`
   - Add your Google ClientId and ClientSecret
   - Add your redirect URI

3. **Create .env file for frontend (if needed):**
   ```bash
   cp .env.template .env.local
   ```

### Never Commit These
- `appsettings.json` (main config file)
- `appsettings.Development.json` (with your secrets)
- `.env` files with real secrets
- API keys or credentials of any kind

## How Git Will Verify This Works

After adding this `.gitignore`:

```bash
# These should be ignored:
git status appsettings.json           # Should show nothing (not tracked)
git status .env                       # Should show nothing (not tracked)

# These should be tracked:
git status appsettings.template.json  # Should be tracked
git status .env.template              # Should be tracked
```

## For CI/CD Pipelines

Create environment-specific secrets:
- **Development:** Use local appsettings.Development.json (not committed)
- **Production:** Use environment variables or Azure Key Vault
- **CI/CD:** Inject secrets via GitHub Secrets or Azure DevOps

## Important Notes

1. **If secrets were accidentally committed:**
   ```bash
   git rm --cached PhotoGallery/appsettings.json
   git commit -m "Remove sensitive appsettings from history"
   git push
   ```

2. **The .gitignore should be committed** - It's a security best practice

3. **Always review before committing** - Use `git status` to verify no secrets are staged

## File Structure After Applying This .gitignore

```
PhotoGallery/
├── appsettings.json              (IGNORED - has secrets)
├── appsettings.Development.json  (IGNORED - has secrets)
├── appsettings.template.json     (TRACKED - shows config structure)
├── bin/                          (IGNORED - build output)
├── obj/                          (IGNORED - build output)
└── [other source files]          (TRACKED)

FE.PhotoGallery/
├── node_modules/                 (IGNORED)
├── dist/                         (IGNORED)
└── [other source files]          (TRACKED)

.env                              (IGNORED - has secrets)
.env.template                     (TRACKED - shows what's needed)
```

## Summary

✅ **Security:** Google OAuth secrets and API keys protected  
✅ **Clean Repo:** Build artifacts, dependencies, and IDE files excluded  
✅ **Developer Friendly:** Template files included for setup reference  
✅ **Best Practices:** Follows industry standards for .gitignore  

Your PhotoGallery project is now protected against accidental secret commits!
