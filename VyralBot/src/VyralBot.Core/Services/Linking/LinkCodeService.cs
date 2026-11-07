using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using VyralBot.Core.Services.Currency;
using VyralBot.Core.Services.EventBus;
using VyralBot.Core.Services.EventBus.Events;
using VyralBot.Data;
using VyralBot.Data.Entities;

namespace VyralBot.Core.Services.Linking;

/// <summary>
/// Default ILinkCodeService: generates 6-digit link codes (expiring, single-use)
/// and redeems them by merging the redeemer's platform accounts into the code
/// creator's unified viewer identity.
/// </summary>
public class LinkCodeService : ILinkCodeService
{
    private const string PlatformEnabledKeyFormat = "Linking.Platforms.{0}";
    private const string ExpiryMinutesKey = "Linking.CodeExpiryMinutes";
    private const int DefaultExpiryMinutes = 5;
    private const string LinkPurpose = "Link";
    private const string DelinkPurpose = "Delink";
    private static readonly TimeSpan CleanupGrace = TimeSpan.FromHours(24);

    private readonly ILogger<LinkCodeService> _logger;
    private readonly IDataService _dataService;
    private readonly ILinkedAccountService _linkedAccounts;
    private readonly ICurrencyService? _currencyService;
    private readonly IEventBus _eventBus;

    public LinkCodeService(
        ILogger<LinkCodeService> logger,
        IDataService dataService,
        ILinkedAccountService linkedAccounts,
        IEventBus eventBus,
        ICurrencyService? currencyService = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        _linkedAccounts = linkedAccounts ?? throw new ArgumentNullException(nameof(linkedAccounts));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _currencyService = currencyService;
    }

    public async Task<(string Code, DateTime ExpiresAt)> GenerateCodeAsync(string username, string platform)
    {
        var enabledRaw = await _dataService.GetConfigValueAsync(string.Format(PlatformEnabledKeyFormat, platform));
        if (!IsLinkingEnabled(enabledRaw))
        {
            throw new InvalidOperationException($"Linking is disabled for {platform}");
        }

        var expiryMinutes = ParsePositiveInt(await _dataService.GetConfigValueAsync(ExpiryMinutesKey), DefaultExpiryMinutes);
        var expiresAt = DateTime.UtcNow.AddMinutes(expiryMinutes);

        var viewerId = await _linkedAccounts.GetOrCreateViewerIdAsync(username, platform);

        // Any previous unredeemed code for this viewer dies the moment a new one is issued.
        await _dataService.InvalidateLinkCodesForViewerAsync(viewerId);

        var code = await GenerateUniqueCodeAsync();

        await _dataService.SaveLinkCodeAsync(new LinkCode
        {
            Code = code,
            ViewerId = viewerId,
            CreatedByUsername = username,
            CreatedByPlatform = platform,
            ExpiresAt = expiresAt
        });

        _logger.LogInformation("Generated link code for {Username} ({Platform}), expires at {ExpiresAt:O}",
            username, platform, expiresAt);

        return (code, expiresAt);
    }

    public async Task<LinkRedeemResult> RedeemCodeAsync(string code, string username, string platform)
    {
        // Normalize: trim and keep digits only (generated codes are 6-digit strings).
        var normalized = new string((code ?? string.Empty).Trim().Where(char.IsDigit).ToArray());

        var linkCode = normalized.Length == 0 ? null : await _dataService.GetLinkCodeAsync(normalized);
        if (linkCode == null)
        {
            return new LinkRedeemResult(false, "Invalid or expired code. Request a new one with !link", null, Array.Empty<string>());
        }

        // Only link-purpose codes redeem through this path — a delink (removal)
        // code must go back through !delink on the platform being removed.
        if (!string.Equals(linkCode.Purpose, LinkPurpose, StringComparison.OrdinalIgnoreCase))
        {
            return new LinkRedeemResult(false,
                LinkMessages.DelinkCodeUsedAsLink.Replace("{code}", normalized),
                null, Array.Empty<string>());
        }

        if (linkCode.RedeemedAt != null)
        {
            return new LinkRedeemResult(false, "That code was already used.", null, Array.Empty<string>());
        }

        if (linkCode.ExpiresAt < DateTime.UtcNow)
        {
            return new LinkRedeemResult(false, "That code has expired — request a new one with !link.", null, Array.Empty<string>());
        }

        // The target identity's rows drive the early no-op and Guard 1 below.
        var targetRows = (await _dataService.GetLinkedAccountsByViewerIdAsync(linkCode.ViewerId)).ToList();

        // Already a member: the redeemer's own (username, platform) is one of the
        // rows inside the code's identity — this also covers the code creator
        // re-typing their own code. Success-style no-op: nothing to merge,
        // nothing to save, no code consumption.
        if (targetRows.Any(row => IsSameAccount(row.Username, row.Platform, username, platform)))
        {
            var currentPlatforms = targetRows
                .Select(row => row.Platform)
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            return new LinkRedeemResult(true, LinkMessages.AlreadyInGroup, linkCode.ViewerId, currentPlatforms);
        }

        // Guard 1: one account per platform, no exemptions — every row in the
        // target identity (creator included) is subject to the rule. A row on
        // the redeemer's platform is a genuine clash; the redeemer's own row
        // cannot appear here because the early no-op above already returned.
        var targetClash = targetRows.FirstOrDefault(row =>
            string.Equals(row.Platform, platform, StringComparison.OrdinalIgnoreCase));
        if (targetClash != null)
        {
            return new LinkRedeemResult(false,
                LinkMessages.AlreadyHasPlatformAccount
                    .Replace("{platform}", platform)
                    .Replace("{name}", targetClash.Username),
                null, Array.Empty<string>());
        }

        // Merge: point the redeemer's accounts at the code's unified viewer identity.
        var redeemerViewerId = await _linkedAccounts.GetOrCreateViewerIdAsync(username, platform);
        if (redeemerViewerId != linkCode.ViewerId)
        {
            var redeemerRows = (await _dataService.GetLinkedAccountsByViewerIdAsync(redeemerViewerId)).ToList();

            // Guard 2: refuse merges that would leave two groups' accounts on the
            // same platform inside one identity. The platform being redeemed right
            // now is excluded — the redeemer's typing account becomes the new row
            // for that platform, and Guard 1 already ensured the target lacks it.
            var mergeClash = redeemerRows.FirstOrDefault(row =>
                !string.Equals(row.Platform, platform, StringComparison.OrdinalIgnoreCase) &&
                targetRows.Any(t =>
                    string.Equals(t.Platform, row.Platform, StringComparison.OrdinalIgnoreCase)));
            if (mergeClash != null)
            {
                return new LinkRedeemResult(false,
                    LinkMessages.MergeConflict.Replace("{platform}", mergeClash.Platform),
                    null, Array.Empty<string>());
            }

            foreach (var row in redeemerRows)
            {
                // Defensive: a redeemer row equal to the creator's platform+username
                // pair would already live under the code's ViewerId — it is the same
                // row, so skip it.
                if (IsSameAccount(row.Username, row.Platform, linkCode.CreatedByUsername, linkCode.CreatedByPlatform))
                {
                    continue;
                }

                // The redeemer's own pair is re-pointed in place by SaveLinkedAccountAsync
                // below (upsert by Id), so re-pointing it here would duplicate the row.
                if (IsSameAccount(row.Username, row.Platform, username, platform))
                {
                    continue;
                }

                await _dataService.UpdateLinkedAccountViewerAsync(row.Id, linkCode.ViewerId);

                // Follow the identity merge into the per-platform status row so
                // odds boosts and goal aggregates see this account's sub/follow
                // history under the code's viewer identity.
                await RepointViewerPlatformStatusAsync(row.Username, row.Platform, linkCode.ViewerId);
            }
        }

        // Save the redeemer's own LinkedAccount under the code's ViewerId. Reuse the
        // existing row (SaveLinkedAccountAsync upserts by Id) so no duplicate is created.
        var redeemerRow = await _dataService.GetLinkedAccountAsync(username, platform);
        if (redeemerRow == null)
        {
            redeemerRow = new LinkedAccount
            {
                ViewerId = linkCode.ViewerId,
                Username = username,
                Platform = platform,
                IsVerified = true,
                VerifiedAt = DateTime.UtcNow
            };
        }
        else
        {
            redeemerRow.ViewerId = linkCode.ViewerId;
            redeemerRow.IsVerified = true;
            redeemerRow.VerifiedAt = DateTime.UtcNow;
        }

        await _dataService.SaveLinkedAccountAsync(redeemerRow);

        // The redeemer's own status row must follow the merge too, or its
        // sub/follow history stays invisible to the target viewer identity.
        await RepointViewerPlatformStatusAsync(username, platform, linkCode.ViewerId);

        linkCode.RedeemedByUsername = username;
        linkCode.RedeemedByPlatform = platform;
        linkCode.RedeemedAt = DateTime.UtcNow;
        await _dataService.SaveLinkCodeAsync(linkCode);

        var linkedPlatforms = (await _dataService.GetLinkedAccountsByViewerIdAsync(linkCode.ViewerId))
            .Select(a => a.Platform)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var names = string.Join(", ", linkedPlatforms);

        try
        {
            await _eventBus.PublishAsync(new AccountsLinkedEvent
            {
                Source = platform,
                ViewerId = linkCode.ViewerId,
                Username = username,
                Platform = platform,
                LinkedPlatformNames = names
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish AccountsLinkedEvent for {Username} ({Platform})", username, platform);
        }

        // UNIFIED BALANCES: merge every moved account's points into the
        // canonical account so the viewer has one spendable total.
        if (_currencyService != null)
        {
            try
            {
                await _currencyService.ConsolidateViewerBalancesAsync(linkCode.ViewerId);
            }
            catch (Exception consolidateEx)
            {
                _logger.LogWarning(consolidateEx, "Balance consolidation failed after linking for viewer {ViewerId}", linkCode.ViewerId);
            }
        }

        _logger.LogInformation("Link code redeemed by {Username} ({Platform}); viewer {ViewerId} now spans {Count} platform(s)",
            username, platform, linkCode.ViewerId, linkedPlatforms.Count);

        var message = $"✅ Linked! This account is now connected to {linkedPlatforms.Count} platform(s): {names}";
        return new LinkRedeemResult(true, message, linkCode.ViewerId, linkedPlatforms);
    }

    public async Task<(string Code, DateTime ExpiresAt)> GenerateDelinkCodeAsync(string username, string platform, string targetPlatform)
    {
        var viewerId = await _linkedAccounts.GetViewerIdAsync(username, platform);
        if (viewerId == null)
        {
            throw new InvalidOperationException(LinkMessages.NoLinkedAccounts);
        }

        var canonicalTarget = NormalizePlatformName(targetPlatform);
        if (canonicalTarget == null)
        {
            throw new InvalidOperationException("Unknown platform — supported: Twitch, Kick, YouTube, Discord, TikTok, Facebook, Rumble");
        }

        var identityRows = (await _dataService.GetLinkedAccountsByViewerIdAsync(viewerId)).ToList();

        if (string.Equals(canonicalTarget, platform, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(LinkMessages.CannotRemoveAccountYouAreTypingFrom);
        }

        if (!identityRows.Any(row => string.Equals(row.Platform, canonicalTarget, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(LinkMessages.NoAccountOnTargetPlatform.Replace("{platform}", canonicalTarget));
        }

        if (identityRows.Count == 1)
        {
            throw new InvalidOperationException(LinkMessages.OnlyOneLinkedAccount);
        }

        var expiryMinutes = ParsePositiveInt(await _dataService.GetConfigValueAsync(ExpiryMinutesKey), DefaultExpiryMinutes);
        var expiresAt = DateTime.UtcNow.AddMinutes(expiryMinutes);

        // A fresh code invalidates prior unredeemed Delink codes for the same
        // viewer+target, so at most one live removal request exists per pair.
        // GetUnredeemedLinkCodesByViewerAsync is keyed by IX_LinkCodes_ViewerId —
        // no recent-1000 table scan on this chat-command path.
        var now = DateTime.UtcNow;
        var priorCodes = await _dataService.GetUnredeemedLinkCodesByViewerAsync(viewerId);
        foreach (var prior in priorCodes.Where(c =>
                string.Equals(c.Purpose, DelinkPurpose, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(c.TargetPlatform, canonicalTarget, StringComparison.OrdinalIgnoreCase)))
        {
            prior.ExpiresAt = now;
            await _dataService.SaveLinkCodeAsync(prior);
        }

        var code = await GenerateUniqueCodeAsync();

        await _dataService.SaveLinkCodeAsync(new LinkCode
        {
            Code = code,
            ViewerId = viewerId,
            CreatedByUsername = username,
            CreatedByPlatform = platform,
            Purpose = DelinkPurpose,
            TargetPlatform = canonicalTarget,
            ExpiresAt = expiresAt
        });

        _logger.LogInformation(
            "Generated delink code for {Username} ({Platform}) targeting {TargetPlatform}, expires at {ExpiresAt:O}",
            username, platform, canonicalTarget, expiresAt);

        return (code, expiresAt);
    }

    public async Task<DelinkResult> RedeemDelinkCodeAsync(string code, string username, string platform)
    {
        // Normalize: trim and keep digits only, mirroring RedeemCodeAsync.
        var normalized = new string((code ?? string.Empty).Trim().Where(char.IsDigit).ToArray());

        var linkCode = normalized.Length == 0 ? null : await _dataService.GetLinkCodeAsync(normalized);
        if (linkCode == null)
        {
            return new DelinkResult(false, "Invalid or expired code. Request a new one with !delink <platform>.");
        }

        if (linkCode.RedeemedAt != null)
        {
            return new DelinkResult(false, "That code was already used.");
        }

        if (linkCode.ExpiresAt < DateTime.UtcNow)
        {
            return new DelinkResult(false, "That code has expired — request a new one with !delink <platform>.");
        }

        if (!string.Equals(linkCode.Purpose, DelinkPurpose, StringComparison.OrdinalIgnoreCase))
        {
            return new DelinkResult(false, "That code isn't a delink code.");
        }

        var redeemerViewerId = await _linkedAccounts.GetViewerIdAsync(username, platform);
        if (redeemerViewerId == null || redeemerViewerId != linkCode.ViewerId)
        {
            return new DelinkResult(false, LinkMessages.DelinkCodeNotForThisGroup);
        }

        if (!string.Equals(platform, linkCode.TargetPlatform, StringComparison.OrdinalIgnoreCase))
        {
            return new DelinkResult(false, LinkMessages.DelinkCodeWrongPlatform.Replace("{platform}", linkCode.TargetPlatform ?? platform));
        }

        // Remove the redeeming account from the unified viewer identity.
        var row = await _dataService.GetLinkedAccountAsync(username, platform);
        if (row != null)
        {
            await _dataService.DeleteLinkedAccountAsync(row.Id);
        }

        // The per-platform status row keeps its follow/sub history but returns to
        // the unlinked pool (null ViewerId) so it no longer counts toward the group.
        var status = await _dataService.GetViewerPlatformStatusAsync(username, platform);
        if (status != null && status.ViewerId != null)
        {
            status.ViewerId = null;
            await _dataService.SaveViewerPlatformStatusAsync(status);
        }

        linkCode.RedeemedByUsername = username;
        linkCode.RedeemedByPlatform = platform;
        linkCode.RedeemedAt = DateTime.UtcNow;
        await _dataService.SaveLinkCodeAsync(linkCode);

        var remainingPlatforms = (await _dataService.GetLinkedAccountsByViewerIdAsync(linkCode.ViewerId))
            .Select(a => a.Platform)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var names = string.Join(", ", remainingPlatforms);

        try
        {
            await _eventBus.PublishAsync(new AccountsLinkedEvent
            {
                Source = platform,
                ViewerId = linkCode.ViewerId,
                Username = username,
                Platform = platform,
                LinkedPlatformNames = names
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish AccountsLinkedEvent after delink for {Username} ({Platform})", username, platform);
        }

        _logger.LogInformation("Delink code redeemed by {Username} ({Platform}); viewer {ViewerId} now spans {Count} platform(s)",
            username, platform, linkCode.ViewerId, remainingPlatforms.Count);

        var message = remainingPlatforms.Count > 0
            ? LinkMessages.DelinkSuccessPlatformsRemain
                .Replace("{platform}", platform)
                .Replace("{N}", remainingPlatforms.Count.ToString())
                .Replace("{names}", names)
            : LinkMessages.DelinkSuccessNothingRemains;
        return new DelinkResult(true, message);
    }

    public async Task<IEnumerable<LinkedAccount>> GetLinkedAccountsForAsync(string username, string platform)
    {
        var viewerId = await _linkedAccounts.GetViewerIdAsync(username, platform);
        if (viewerId == null)
        {
            return Array.Empty<LinkedAccount>();
        }

        return await _linkedAccounts.GetLinkedAccountsAsync(viewerId);
    }

    public Task<bool> UnlinkAsync(string username, string platform)
    {
        return _linkedAccounts.UnlinkAccountAsync(username, platform);
    }

    public async Task CleanupExpiredAsync()
    {
        var cutoff = DateTime.UtcNow - CleanupGrace;
        var recent = await _dataService.GetRecentLinkCodesAsync(1000);

        var removed = 0;
        foreach (var expiredCode in recent.Where(c => c.ExpiresAt < cutoff))
        {
            await _dataService.DeleteLinkCodeAsync(expiredCode.Id);
            removed++;
        }

        if (removed > 0)
        {
            _logger.LogInformation("Cleaned up {Count} link code(s) expired more than 24 hours ago", removed);
        }
    }

    /// <summary>
    /// Re-points the (username, platform) ViewerPlatformStatus row to
    /// <paramref name="targetViewerId"/> when it exists under a different or
    /// null viewer id. Keeps odds boosts and goal aggregates working across a
    /// merge; a no-op when the row is missing or already correct.
    /// </summary>
    private async Task RepointViewerPlatformStatusAsync(string username, string platform, string targetViewerId)
    {
        var status = await _dataService.GetViewerPlatformStatusAsync(username, platform);
        if (status == null || status.ViewerId == targetViewerId)
        {
            return;
        }

        status.ViewerId = targetViewerId;
        await _dataService.SaveViewerPlatformStatusAsync(status);

        _logger.LogInformation("Re-pointed ViewerPlatformStatus for {Username} ({Platform}) to viewer {ViewerId}",
            username, platform, targetViewerId);
    }

    /// <summary>
    /// Generates a 6-digit code, retrying on the (unlikely) collision with an
    /// existing stored code. Shared by link and delink generation.
    /// </summary>
    private async Task<string> GenerateUniqueCodeAsync()
    {
        string code;
        do
        {
            code = RandomNumberGenerator.GetInt32(100000, 999999).ToString();
        }
        while (await _dataService.GetLinkCodeAsync(code) != null);

        return code;
    }

    /// <summary>
    /// Maps a raw chat token to one of the 7 canonical platform identity
    /// strings, or null when unknown.
    /// </summary>
    private static string? NormalizePlatformName(string raw) => raw?.Trim().ToLowerInvariant() switch
    {
        "twitch" => "Twitch",
        "kick" => "Kick",
        "youtube" => "YouTube",
        "discord" => "Discord",
        "tiktok" => "TikTok",
        "facebook" => "Facebook",
        "rumble" => "Rumble",
        _ => null
    };

    /// <summary>Config default: linking enabled unless explicitly "false".</summary>
    private static bool IsLinkingEnabled(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }

        return bool.TryParse(raw.Trim(), out var enabled) ? enabled : true;
    }

    private static int ParsePositiveInt(string? raw, int fallback)
    {
        return int.TryParse(raw, out var value) && value > 0 ? value : fallback;
    }

    private static bool IsSameAccount(string usernameA, string platformA, string usernameB, string platformB)
    {
        return string.Equals(usernameA, usernameB, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(platformA, platformB, StringComparison.OrdinalIgnoreCase);
    }
}
