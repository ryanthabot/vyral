# VyralBot Distribution Guide

## 📦 Portable Architecture

VyralBot now uses a **fully portable data structure**. All user data is stored in the local `appdata/` folder within the VyralBot directory, not in Windows AppData/Roaming.

## 📁 Directory Structure

```
VyralBot/
├── src/                         # Source code
├── docs/                        # Documentation
├── appdata/                     # ⚠️ USER DATA (git ignored)
│   ├── profiles/
│   │   └── {ProfileName}/
│   │       ├── profile.json
│   │       ├── data/
│   │       │   └── vyralbot.db
│   │       ├── twitch_credentials.json
│   │       └── kick_credentials.json
│   └── current_profile.txt
├── logs/                        # ⚠️ Application logs (git ignored)
├── .gitignore
├── README.md
└── DISTRIBUTION.md              # This file
```

## 🚀 Preparing for Distribution

### For GitHub/Public Release

1. **Delete user data:**
   ```powershell
   Remove-Item -Recurse -Force .\appdata\
   Remove-Item -Recurse -Force .\logs\
   Remove-Item -Force .\run-debug.log -ErrorAction SilentlyContinue
   ```

2. **Verify .gitignore:**
   - `appdata/` ✅
   - `logs/` ✅
   - `run-debug.log` ✅

3. **Commit and push:**
   ```powershell
   git add .
   git commit -m "Clean release ready for distribution"
   git push
   ```

### For Sharing with Friends (ZIP)

1. **Delete user data:**
   ```powershell
   Remove-Item -Recurse -Force .\appdata\
   Remove-Item -Recurse -Force .\logs\
   Remove-Item -Force .\run-debug.log -ErrorAction SilentlyContinue
   ```

2. **Create ZIP:**
   ```powershell
   Compress-Archive -Path .\* -DestinationPath ..\VyralBot-Clean.zip
   ```

3. **Share `VyralBot-Clean.zip`** - No personal data included!

## 📥 First Run Experience

When a user launches VyralBot with no `appdata/` folder:

1. ✅ `appdata/` folder is automatically created
2. ✅ Profile selector appears (first-time setup)
3. ✅ User creates their first profile
4. ✅ Platform authentication begins

**No manual setup required!**

## 🔄 Migrating from Old Versions

**Old Location (Windows AppData):**
```
C:\Users\{User}\AppData\Roaming\VyralBot\profiles\
```

**New Location (Portable):**
```
{VyralBot Directory}\appdata\profiles\
```

### Migration Steps

1. **Export profiles from old version:**
   - Open old VyralBot
   - Settings → Profile Management
   - Export each profile (creates `.zip`)

2. **Install new version** (delete `appdata/` for clean start)

3. **Import profiles:**
   - Open new VyralBot
   - Settings → Profile Management
   - Import profile ZIPs

**Note:** OAuth tokens are machine-specific (Windows DPAPI). You'll need to re-authenticate platforms after import.

## 🔒 Security Considerations

### What's Safe to Share

✅ **Source code** (`src/`, `docs/`)
✅ **Build artifacts** (compiled DLLs)
✅ **Documentation** (`.md` files)
✅ **Configuration** (non-sensitive settings)

### What to NEVER Share

❌ `appdata/profiles/*/twitch_credentials.json` (OAuth tokens)
❌ `appdata/profiles/*/kick_credentials.json` (OAuth tokens)
❌ `appdata/profiles/*/data/vyralbot.db` (personal data)
❌ `logs/` (may contain sensitive info)

### Credentials Security

- OAuth tokens stored in `appdata/` are encrypted using **Windows DPAPI**
- Encryption is **machine-specific** (can't be decrypted on another PC)
- Moving to new PC requires **re-authentication**
- Profile exports include encrypted tokens (still unusable on other machines)

## 🛠️ Developer Workflow

### Local Development

```powershell
# Run with your personal profiles
dotnet run --project src\VyralBot.UI\VyralBot.UI.csproj
```
Your `appdata/` folder contains your profiles - **don't commit it!**

### Clean Testing

```powershell
# Test fresh install experience
Remove-Item -Recurse -Force .\appdata\
dotnet run --project src\VyralBot.UI\VyralBot.UI.csproj
```

### Pre-Commit Checklist

- [ ] `appdata/` is `.gitignore`'d
- [ ] No personal data in commit
- [ ] Documentation updated
- [ ] Build succeeds: `dotnet build src\VyralBot.sln`

## 📊 Backup Strategy

### For Users

**Quick Backup:**
```powershell
# Backup entire appdata folder
Copy-Item -Recurse .\appdata\ .\backups\appdata-$(Get-Date -Format 'yyyy-MM-dd')\
```

**Profile-Specific Backup:**
- Use Settings → Profile Management → Export
- Creates timestamped `.zip` per profile

### For Developers

**Version Control:**
```powershell
# Create dev branch with test data
git checkout -b dev-testing
git add -f appdata/  # Force add ignored folder (ONLY FOR DEV BRANCH)
git commit -m "Dev test data"
# NEVER merge this branch to main!
```

## 🌐 Multi-User Setup

### Same PC, Multiple Users

Each Windows user gets their own:
```
C:\Users\{User1}\Desktop\VyralBot\appdata\
C:\Users\{User2}\Desktop\VyralBot\appdata\
```

### Shared Network Drive

**Portable mode works!**
```
\\NetworkDrive\VyralBot\appdata\
```
But OAuth tokens won't work across different machines (DPAPI limitation).

## 📝 FAQ

**Q: Can I move VyralBot to a USB drive?**
A: Yes! The `appdata/` folder moves with it. Re-authentication required on new PC.

**Q: What if I accidentally commit `appdata/`?**
A:
```powershell
# Remove from git, keep local file
git rm -r --cached appdata/
git commit -m "Remove accidental appdata commit"
git push --force
```

**Q: How do I share profiles with my team?**
A: Use Settings → Export Profile → Share the `.zip` file. They'll need to re-authenticate platforms.

**Q: Can I sync `appdata/` with cloud storage?**
A: Not recommended. OAuth tokens are machine-specific. Use Export/Import instead.

## 🎯 Distribution Checklist

Before releasing:

- [ ] Delete `appdata/` folder
- [ ] Delete `logs/` folder  
- [ ] Delete `run-debug.log`
- [ ] Verify `.gitignore` includes `appdata/`, `logs/`, `run-debug.log`
- [ ] Test fresh install: Run app, create profile, verify works
- [ ] Build succeeds with no errors
- [ ] Documentation updated
- [ ] Version number updated in `Directory.Build.props`
- [ ] Create GitHub release with clean ZIP

## 📞 Support

**Users experiencing issues?**
1. Check `logs/` folder for errors
2. Export profile for backup
3. Delete `appdata/` for fresh start
4. Re-import profile if needed

**Developers?**
See `docs/CONTRIBUTING.md` for development setup.
