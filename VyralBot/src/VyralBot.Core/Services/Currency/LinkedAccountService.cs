using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using VyralBot.Data;
using VyralBot.Data.Entities;

namespace VyralBot.Core.Services.Currency;

/// <summary>
/// Service for managing cross-platform account linking and unified viewer identity.
/// </summary>
public class LinkedAccountService : ILinkedAccountService
{
    private readonly ILogger<LinkedAccountService> _logger;
    private readonly IDataService _dataService;
    private readonly ICurrencyService _currencyService;
    private readonly ConcurrentDictionary<string, PendingLink> _pendingLinks = new();

    private record PendingLink(string FromViewerId, string TargetUsername, string TargetPlatform, string Code, DateTime ExpiresAt);

    public LinkedAccountService(
        ILogger<LinkedAccountService> logger,
        IDataService dataService,
        ICurrencyService currencyService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        _currencyService = currencyService ?? throw new ArgumentNullException(nameof(currencyService));
    }

    public async Task<string?> GetViewerIdAsync(string username, string platform)
    {
        return await _dataService.GetViewerIdAsync(username, platform);
    }

    public async Task<IEnumerable<LinkedAccount>> GetLinkedAccountsAsync(string viewerId)
    {
        return await _dataService.GetLinkedAccountsByViewerIdAsync(viewerId);
    }

    public async Task<string> GetOrCreateViewerIdAsync(string username, string platform)
    {
        var existing = await _dataService.GetLinkedAccountAsync(username, platform);
        if (existing != null)
        {
            return existing.ViewerId;
        }

        var viewerId = Guid.NewGuid().ToString();
        var account = new LinkedAccount
        {
            ViewerId = viewerId,
            Username = username,
            Platform = platform,
            IsVerified = true,
            VerifiedAt = DateTime.UtcNow
        };

        await _dataService.SaveLinkedAccountAsync(account);
        _logger.LogInformation("Created linked account for {Username} ({Platform}) with ViewerId {ViewerId}",
            username, platform, viewerId);

        return viewerId;
    }

    public async Task<bool> InitiateLinkAsync(string fromUsername, string fromPlatform, string targetUsername, string targetPlatform)
    {
        var fromViewerId = await GetOrCreateViewerIdAsync(fromUsername, fromPlatform);

        // Check if target already linked to someone else
        var existingTarget = await _dataService.GetLinkedAccountAsync(targetUsername, targetPlatform);
        if (existingTarget != null && existingTarget.ViewerId != fromViewerId)
        {
            _logger.LogWarning("Cannot link {Target} ({TargetPlatform}) — already linked to another viewer",
                targetUsername, targetPlatform);
            return false;
        }

        // Generate 6-digit verification code
        var code = Random.Shared.Next(100000, 999999).ToString();
        var key = $"{targetUsername}:{targetPlatform}".ToLowerInvariant();

        // Prune expired pendings so abandoned link attempts don't accumulate
        // on this singleton service — until now they were only removed when
        // the target user actually attempted verification.
        foreach (var pair in _pendingLinks)
        {
            if (DateTime.UtcNow > pair.Value.ExpiresAt)
            {
                _pendingLinks.TryRemove(pair.Key, out _);
            }
        }

        _pendingLinks[key] = new PendingLink(fromViewerId, targetUsername, targetPlatform, code, DateTime.UtcNow.AddMinutes(5));

        _logger.LogInformation("Link initiated from {From} ({FromPlatform}) to {Target} ({TargetPlatform}), code: {Code}",
            fromUsername, fromPlatform, targetUsername, targetPlatform, code);

        return true;
    }

    public async Task<bool> VerifyLinkAsync(string username, string platform, string verificationCode)
    {
        var key = $"{username}:{platform}".ToLowerInvariant();

        if (!_pendingLinks.TryRemove(key, out var pending))
        {
            return false;
        }

        if (DateTime.UtcNow > pending.ExpiresAt)
        {
            _logger.LogWarning("Link verification expired for {Username} ({Platform})", username, platform);
            return false;
        }

        if (pending.Code != verificationCode)
        {
            _logger.LogWarning("Invalid verification code for {Username} ({Platform})", username, platform);
            return false;
        }

        var account = new LinkedAccount
        {
            ViewerId = pending.FromViewerId,
            Username = username,
            Platform = platform,
            IsVerified = true,
            VerifiedAt = DateTime.UtcNow
        };

        await _dataService.SaveLinkedAccountAsync(account);
        _logger.LogInformation("Linked {Username} ({Platform}) to ViewerId {ViewerId}",
            username, platform, pending.FromViewerId);

        return true;
    }

    public async Task<bool> UnlinkAccountAsync(string username, string platform)
    {
        var account = await _dataService.GetLinkedAccountAsync(username, platform);
        if (account == null)
        {
            return false;
        }

        await _dataService.DeleteLinkedAccountAsync(account.Id);

        // Detach the per-platform status row too, so the account drops out of
        // aggregates (odds boosts, goal sums, unified balance) immediately —
        // matching the !delink path. Keeping its follow/sub history is fine;
        // the row simply returns to the unlinked pool (null ViewerId).
        var status = await _dataService.GetViewerPlatformStatusAsync(username, platform);
        if (status != null && status.ViewerId != null)
        {
            status.ViewerId = null;
            await _dataService.SaveViewerPlatformStatusAsync(status);
        }

        _logger.LogInformation("Unlinked {Username} ({Platform})", username, platform);
        return true;
    }

    public async Task<IEnumerable<(string Username, string Platform)>> ResolveIdentitiesAsync(string username, string platform)
    {
        var settings = await _currencyService.GetSettingsAsync();

        if (settings?.UnifiedBalanceEnabled != true)
        {
            return [(username, platform)];
        }

        var viewerId = await _dataService.GetViewerIdAsync(username, platform);
        if (viewerId == null)
        {
            return [(username, platform)];
        }

        var links = await _dataService.GetLinkedAccountsByViewerIdAsync(viewerId);
        return links.Select(l => (l.Username, l.Platform));
    }
}
