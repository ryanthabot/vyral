using System.IO;
using VyralBot.Core.Services.Moderation;
using VyralBot.Data.Entities;
using VyralBot.UI.Services;
using Xunit;

namespace VyralBot.UI.Tests;

/// <summary>
/// ModTierResolver: the effective tier is the higher-ranked of the explicit
/// ModRoleAssignment and the Discord-role→tier mapping. Rules:
///
///   1. A higher-ranked role→tier mapping outranks a lower explicit assignment
///      (and a higher explicit assignment outranks a lower role mapping).
///   2. No guild id on the message → the Discord role lookup is skipped
///      entirely (the stored assignment is the answer).
///   3. A Discord failure falls back to the stored assignment — never throws.
///   4. Nothing known → null.
/// </summary>
public class ModTierResolverTests
{
    private const string BotId = "bot-1";

    // Snowflake-shaped so the resolver's gateway lookup gate passes.
    private const string UserId = "424242";
    private const string GuildId = "999888777";

    [Fact]
    public async Task Resolve_RoleTierBeatsLowerAssignment()
    {
        var roles = new FakeRoleService("TrialMod", new Dictionary<string, string> { ["55"] = "SeniorMod" });
        var resolver = new ModTierResolver(roles, new FakeBot("55"));

        var tier = await resolver.ResolveTierAsync(BotId, GuildId, UserId, "modder");

        Assert.Equal("SeniorMod", tier);
    }

    [Fact]
    public async Task Resolve_AssignmentBeatsLowerRole()
    {
        var roles = new FakeRoleService("Admin", new Dictionary<string, string> { ["55"] = "Mod" });
        var resolver = new ModTierResolver(roles, new FakeBot("55"));

        var tier = await resolver.ResolveTierAsync(BotId, GuildId, UserId, "modder");

        Assert.Equal("Admin", tier);
    }

    [Fact]
    public async Task Resolve_NoGuild_SkipsRoleLookup()
    {
        var roles = new FakeRoleService("Mod", new Dictionary<string, string> { ["55"] = "Admin" });
        var bot = new FakeBot("55");
        var resolver = new ModTierResolver(roles, bot);

        var tier = await resolver.ResolveTierAsync(BotId, guildId: null, UserId, "modder");

        Assert.Equal("Mod", tier);
        Assert.Equal(0, bot.RoleLookupCalls);
    }

    [Fact]
    public async Task Resolve_RoleLookupThrows_FallsBackToAssignment()
    {
        var roles = new FakeRoleService("SeniorMod");
        var resolver = new ModTierResolver(roles, new ThrowingBot());

        var tier = await resolver.ResolveTierAsync(BotId, GuildId, UserId, "modder");

        Assert.Equal("SeniorMod", tier);
    }

    [Fact]
    public async Task Resolve_NothingKnown_ReturnsNull()
    {
        var roles = new FakeRoleService(null);
        var resolver = new ModTierResolver(roles, new FakeBot());

        var tier = await resolver.ResolveTierAsync(BotId, GuildId, UserId, "modder");

        Assert.Null(tier);
    }

    // ------------------------------------------------------------------
    // hand-written fakes for the two small Core seams
    // ------------------------------------------------------------------

    /// <summary>Only the two tier reads are real; writes are out of scope.</summary>
    private sealed class FakeRoleService : IModRoleService
    {
        private readonly string? _assignedTier;
        private readonly Dictionary<string, string> _roleTiers;

        public FakeRoleService(string? assignedTier, Dictionary<string, string>? roleTiers = null)
        {
            _assignedTier = assignedTier;
            _roleTiers = roleTiers ?? new Dictionary<string, string>();
        }

        public Task<string?> GetTierAsync(string discordUserId) => Task.FromResult(_assignedTier);

        public Task<string?> GetTierFromRolesAsync(string botId, IReadOnlyList<string> discordRoleIds)
            => Task.FromResult(discordRoleIds
                .Where(_roleTiers.ContainsKey)
                .Select(id => _roleTiers[id])
                .OrderByDescending(t => ModTiers.Rank(t))
                .Select(ModTiers.NormalizeTier)
                .FirstOrDefault());

        public Task AssignAsync(string discordUserId, string username, string tier, string assignedBy)
            => throw new NotSupportedException("Not needed for resolver tests.");

        public Task UnassignAsync(string discordUserId)
            => throw new NotSupportedException("Not needed for resolver tests.");

        public Task<IReadOnlyList<ModRoleAssignment>> GetAllAsync()
            => throw new NotSupportedException("Not needed for resolver tests.");
    }

    /// <summary>Returns a fixed role-id list and counts the lookups.</summary>
    private sealed class FakeBot : IModDiscordBot
    {
        private readonly string[] _roleIds;

        public FakeBot(params string[] roleIds) => _roleIds = roleIds;

        public int RoleLookupCalls { get; private set; }

        public Task<List<string>> GetMemberRoleIdsAsync(string guildId, string userId)
        {
            RoleLookupCalls++;
            return Task.FromResult(_roleIds.ToList());
        }

        public Task<bool> SendToChannelAsync(string channelNameOrId, string content, string? embedJson, string? guildName = null)
            => throw new NotSupportedException("Not needed for resolver tests.");

        public Task<bool> SendDmAsync(string username, string content, string? embedJson, string? guildName = null)
            => throw new NotSupportedException("Not needed for resolver tests.");

        public Task<bool> AddRoleToUserAsync(string username, string roleIdOrName, string? reason, string? guildName = null)
            => throw new NotSupportedException("Not needed for resolver tests.");
    }

    /// <summary>A bot whose member-role lookup always throws (Discord outage).</summary>
    private sealed class ThrowingBot : IModDiscordBot
    {
        public Task<List<string>> GetMemberRoleIdsAsync(string guildId, string userId)
            => throw new InvalidOperationException("Discord is down");

        public Task<bool> SendToChannelAsync(string channelNameOrId, string content, string? embedJson, string? guildName = null)
            => throw new NotSupportedException("Not needed for resolver tests.");

        public Task<bool> SendDmAsync(string username, string content, string? embedJson, string? guildName = null)
            => throw new NotSupportedException("Not needed for resolver tests.");

        public Task<bool> AddRoleToUserAsync(string username, string roleIdOrName, string? reason, string? guildName = null)
            => throw new NotSupportedException("Not needed for resolver tests.");
    }
}
