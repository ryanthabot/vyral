# Kick OAuth Service

## Overview
PKCE-based OAuth authentication for Kick using KickLib's `KickOAuthGenerator`.

## Source Files

### KickLibOAuthService
- **File:** `src/VyralBot.Integrations/Kick/KickLibOAuthService.cs` (~178 lines)
- **Namespace:** `VyralBot.Integrations.Kick`
- **Class:** `KickLibOAuthService`
- **Constructor:** `ILogger<KickLibOAuthService>`
- **Uses:** `KickOAuthGenerator` from KickLib NuGet package
- **Private fields:** `string? _codeVerifier` (stored between auth URL generation and token exchange)
- **Public methods:**
  - `GenerateAuthorizationUrl(string clientId, string redirectUri)` — generates PKCE code verifier + challenge, returns authorization URL, stores verifier for later use. Requested scopes: `user:read`, `channel:read`, `chat:write`, `events:subscribe`, `moderation:ban` (ban/timeout), `moderation:chat_message:manage` (message delete — added Wave 9; tokens granted before it must re-auth once to delete messages), `kicks:read`
  - `ExchangeCodeForTokenAsync(string code, string clientId, string clientSecret, string redirectUri)` — exchanges authorization code for access/refresh tokens using stored PKCE verifier
  - `RefreshTokenAsync(string refreshToken, string clientId, string clientSecret)` — refreshes expired access tokens (Kick preserves the originally granted scopes)

## OAuth Flow
```
1. KickLibOAuthService.GenerateAuthorizationUrl()
   → Generate PKCE code_verifier + code_challenge
   → Return Kick OAuth URL with code_challenge
   → Store code_verifier
2. User authorizes in browser → redirect with auth code
3. KickLibOAuthService.ExchangeCodeForTokenAsync(code)
   → Send code + code_verifier to Kick token endpoint
   → Receive access_token + refresh_token
4. Tokens saved to Platform entity
5. KickLibOAuthService.RefreshTokenAsync() when tokens expire
```

## All Files
| File | Description |
|------|-------------|
| `src/VyralBot.Integrations/Kick/KickLibOAuthService.cs` | PKCE OAuth implementation |
