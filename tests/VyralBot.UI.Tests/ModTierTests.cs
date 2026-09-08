using Xunit;

namespace VyralBot.UI.Tests;

/// <summary>
/// W5B mod tier rules:
///
///   1. There are exactly four mod tiers with a fixed rank order:
///      TrialMod (0) &lt; Mod (1) &lt; SeniorMod (2) &lt; Admin (3). Tier names are
///      matched case-insensitively and stored canonically ("TrialMod", "Mod",
///      "SeniorMod", "Admin" — matching the ModRoleAssignment.Tier doc).
///   2. Every tier inherits the commands of every lower tier and adds its own:
///        TrialMod: kick, timeout, purge
///        Mod:      + unban, mute, addcurrency, removecurrency
///        SeniorMod:+ ban, startgiveaway, cancelgiveaway, draw, startpoll, endpoll
///        Admin:    + setbalance, assignmod (Admin is the ceiling, so these run
///                    for Admin only)
///   3. Unknown commands never run for any tier; unknown tiers never run anything.
///
/// <see cref="ModRoleServiceTests"/> additionally covers ModRoleService, which
/// persists the tier on the ModRoleAssignment row (upsert on DiscordUserId),
/// normalizes tier casing and grants the backing Discord role best-effort via
/// <see cref="VyralBot.Core.Services.Moderation.IModDiscordBot"/> — a Core-side
/// mirror of the Discord bot surface (VyralBot.Integrations cannot be referenced
/// from Core, so the DI composition wraps IDiscordService in a small adapter).
///
/// The shared in-memory IDataService test double (<c>FakeDataService</c>) and
/// the Discord fakes live in ModCommandEngineTests.cs (same namespace/project).
///
/// Note: the helpers are public (not internal) because VyralBot.Core does not
/// grant InternalsVisibleTo to the test project.
/// </summary>
public class ModTierTests
{
    // ------------------------------------------------------------------
    // Rule 1: rank order
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("TrialMod", 0)]
    [InlineData("Mod", 1)]
    [InlineData("SeniorMod", 2)]
    [InlineData("Admin", 3)]
    public void Rank_Maps_The_Four_Tiers(string tier, int expected)
    {
        Assert.Equal(expected, VyralBot.Core.Services.Moderation.ModTiers.Rank(tier));
    }

    [Fact]
    public void Rank_IsCaseInsensitive()
    {
        Assert.Equal(0, VyralBot.Core.Services.Moderation.ModTiers.Rank("trialmod"));
        Assert.Equal(1, VyralBot.Core.Services.Moderation.ModTiers.Rank("MOD"));
        Assert.Equal(2, VyralBot.Core.Services.Moderation.ModTiers.Rank("seniormod"));
        Assert.Equal(3, VyralBot.Core.Services.Moderation.ModTiers.Rank("admin"));
    }

    [Fact]
    public void Rank_UnknownTier_IsMinusOne()
    {
        Assert.Equal(-1, VyralBot.Core.Services.Moderation.ModTiers.Rank("Owner"));
        Assert.Equal(-1, VyralBot.Core.Services.Moderation.ModTiers.Rank(""));
        Assert.Equal(-1, VyralBot.Core.Services.Moderation.ModTiers.Rank("SuperAdmin"));
    }

    [Fact]
    public void IsValidTier_True_ForTheFourTiers_Only()
    {
        Assert.True(VyralBot.Core.Services.Moderation.ModTiers.IsValidTier("TrialMod"));
        Assert.True(VyralBot.Core.Services.Moderation.ModTiers.IsValidTier("Mod"));
        Assert.True(VyralBot.Core.Services.Moderation.ModTiers.IsValidTier("SeniorMod"));
        Assert.True(VyralBot.Core.Services.Moderation.ModTiers.IsValidTier("Admin"));
        Assert.True(VyralBot.Core.Services.Moderation.ModTiers.IsValidTier("seniorMOD"));

        Assert.False(VyralBot.Core.Services.Moderation.ModTiers.IsValidTier("Owner"));
        Assert.False(VyralBot.Core.Services.Moderation.ModTiers.IsValidTier(""));
    }

    [Fact]
    public void NormalizeTier_ReturnsCanonicalCasing()
    {
        Assert.Equal("TrialMod", VyralBot.Core.Services.Moderation.ModTiers.NormalizeTier("trialmod"));
        Assert.Equal("Mod", VyralBot.Core.Services.Moderation.ModTiers.NormalizeTier("MOD"));
        Assert.Equal("SeniorMod", VyralBot.Core.Services.Moderation.ModTiers.NormalizeTier("SeniorMod"));
        Assert.Equal("Admin", VyralBot.Core.Services.Moderation.ModTiers.NormalizeTier(" admin "));
        Assert.Null(VyralBot.Core.Services.Moderation.ModTiers.NormalizeTier("Owner"));
        Assert.Null(VyralBot.Core.Services.Moderation.ModTiers.NormalizeTier(null));
    }

    // ------------------------------------------------------------------
    // Rule 2: the tier command ladder (each tier inherits the lower ones)
    // ------------------------------------------------------------------

    [Fact]
    public void TrialMod_CanRun_TrialCommands()
    {
        Assert.True(VyralBot.Core.Services.Moderation.ModTiers.CanRun("TrialMod", "kick"));
        Assert.True(VyralBot.Core.Services.Moderation.ModTiers.CanRun("TrialMod", "timeout"));
        Assert.True(VyralBot.Core.Services.Moderation.ModTiers.CanRun("TrialMod", "purge"));
    }

    [Fact]
    public void TrialMod_CannotRun_HigherTierCommands()
    {
        Assert.False(VyralBot.Core.Services.Moderation.ModTiers.CanRun("TrialMod", "unban"));
        Assert.False(VyralBot.Core.Services.Moderation.ModTiers.CanRun("TrialMod", "mute"));
        Assert.False(VyralBot.Core.Services.Moderation.ModTiers.CanRun("TrialMod", "addcurrency"));
        Assert.False(VyralBot.Core.Services.Moderation.ModTiers.CanRun("TrialMod", "removecurrency"));
        Assert.False(VyralBot.Core.Services.Moderation.ModTiers.CanRun("TrialMod", "ban"));
        Assert.False(VyralBot.Core.Services.Moderation.ModTiers.CanRun("TrialMod", "startgiveaway"));
        Assert.False(VyralBot.Core.Services.Moderation.ModTiers.CanRun("TrialMod", "draw"));
        Assert.False(VyralBot.Core.Services.Moderation.ModTiers.CanRun("TrialMod", "endpoll"));
        Assert.False(VyralBot.Core.Services.Moderation.ModTiers.CanRun("TrialMod", "setbalance"));
        Assert.False(VyralBot.Core.Services.Moderation.ModTiers.CanRun("TrialMod", "assignmod"));
    }

    [Fact]
    public void Mod_Inherits_TrialCommands_AndAdds_ModCommands()
    {
        // inherited
        Assert.True(VyralBot.Core.Services.Moderation.ModTiers.CanRun("Mod", "kick"));
        Assert.True(VyralBot.Core.Services.Moderation.ModTiers.CanRun("Mod", "timeout"));
        Assert.True(VyralBot.Core.Services.Moderation.ModTiers.CanRun("Mod", "purge"));

        // own tier
        Assert.True(VyralBot.Core.Services.Moderation.ModTiers.CanRun("Mod", "unban"));
        Assert.True(VyralBot.Core.Services.Moderation.ModTiers.CanRun("Mod", "mute"));
        Assert.True(VyralBot.Core.Services.Moderation.ModTiers.CanRun("Mod", "addcurrency"));
        Assert.True(VyralBot.Core.Services.Moderation.ModTiers.CanRun("Mod", "removecurrency"));

        // still out of reach
        Assert.False(VyralBot.Core.Services.Moderation.ModTiers.CanRun("Mod", "ban"));
        Assert.False(VyralBot.Core.Services.Moderation.ModTiers.CanRun("Mod", "startgiveaway"));
        Assert.False(VyralBot.Core.Services.Moderation.ModTiers.CanRun("Mod", "cancelgiveaway"));
        Assert.False(VyralBot.Core.Services.Moderation.ModTiers.CanRun("Mod", "draw"));
        Assert.False(VyralBot.Core.Services.Moderation.ModTiers.CanRun("Mod", "startpoll"));
        Assert.False(VyralBot.Core.Services.Moderation.ModTiers.CanRun("Mod", "endpoll"));
        Assert.False(VyralBot.Core.Services.Moderation.ModTiers.CanRun("Mod", "setbalance"));
        Assert.False(VyralBot.Core.Services.Moderation.ModTiers.CanRun("Mod", "assignmod"));
    }

    [Fact]
    public void SeniorMod_Inherits_LowerCommands_AndAdds_SeniorCommands()
    {
        Assert.True(VyralBot.Core.Services.Moderation.ModTiers.CanRun("SeniorMod", "kick"));
        Assert.True(VyralBot.Core.Services.Moderation.ModTiers.CanRun("SeniorMod", "addcurrency"));
        Assert.True(VyralBot.Core.Services.Moderation.ModTiers.CanRun("SeniorMod", "ban"));
        Assert.True(VyralBot.Core.Services.Moderation.ModTiers.CanRun("SeniorMod", "startgiveaway"));
        Assert.True(VyralBot.Core.Services.Moderation.ModTiers.CanRun("SeniorMod", "cancelgiveaway"));
        Assert.True(VyralBot.Core.Services.Moderation.ModTiers.CanRun("SeniorMod", "draw"));
        Assert.True(VyralBot.Core.Services.Moderation.ModTiers.CanRun("SeniorMod", "startpoll"));
        Assert.True(VyralBot.Core.Services.Moderation.ModTiers.CanRun("SeniorMod", "endpoll"));

        // Admin-only commands
        Assert.False(VyralBot.Core.Services.Moderation.ModTiers.CanRun("SeniorMod", "setbalance"));
        Assert.False(VyralBot.Core.Services.Moderation.ModTiers.CanRun("SeniorMod", "assignmod"));
    }

    [Fact]
    public void Admin_Runs_Everything()
    {
        Assert.True(VyralBot.Core.Services.Moderation.ModTiers.CanRun("Admin", "kick"));
        Assert.True(VyralBot.Core.Services.Moderation.ModTiers.CanRun("Admin", "unban"));
        Assert.True(VyralBot.Core.Services.Moderation.ModTiers.CanRun("Admin", "ban"));
        Assert.True(VyralBot.Core.Services.Moderation.ModTiers.CanRun("Admin", "draw"));
        Assert.True(VyralBot.Core.Services.Moderation.ModTiers.CanRun("Admin", "setbalance"));
        Assert.True(VyralBot.Core.Services.Moderation.ModTiers.CanRun("Admin", "assignmod"));
    }

    [Fact]
    public void CanRun_IsCaseInsensitive_OnCommandAndTier()
    {
        Assert.True(VyralBot.Core.Services.Moderation.ModTiers.CanRun("trialmod", "KICK"));
        Assert.True(VyralBot.Core.Services.Moderation.ModTiers.CanRun("ADMIN", "SetBalance"));
        Assert.False(VyralBot.Core.Services.Moderation.ModTiers.CanRun("SENIORMOD", "ASSIGNMOD"));
    }

    [Fact]
    public void TierLists_AreCumulative()
    {
        foreach (var cmd in VyralBot.Core.Services.Moderation.ModTiers.TrialMod)
        {
            Assert.Contains(cmd, VyralBot.Core.Services.Moderation.ModTiers.Mod);
            Assert.Contains(cmd, VyralBot.Core.Services.Moderation.ModTiers.SeniorMod);
            Assert.Contains(cmd, VyralBot.Core.Services.Moderation.ModTiers.Admin);
        }
        foreach (var cmd in VyralBot.Core.Services.Moderation.ModTiers.Mod)
        {
            Assert.Contains(cmd, VyralBot.Core.Services.Moderation.ModTiers.SeniorMod);
            Assert.Contains(cmd, VyralBot.Core.Services.Moderation.ModTiers.Admin);
        }
        foreach (var cmd in VyralBot.Core.Services.Moderation.ModTiers.SeniorMod)
        {
            Assert.Contains(cmd, VyralBot.Core.Services.Moderation.ModTiers.Admin);
        }
    }

    // ------------------------------------------------------------------
    // Rule 3: unknown commands / tiers never run
    // ------------------------------------------------------------------

    [Fact]
    public void UnknownCommand_NeverRuns_ForAnyTier()
    {
        Assert.False(VyralBot.Core.Services.Moderation.ModTiers.CanRun("TrialMod", "nuke"));
        Assert.False(VyralBot.Core.Services.Moderation.ModTiers.CanRun("Mod", "nuke"));
        Assert.False(VyralBot.Core.Services.Moderation.ModTiers.CanRun("SeniorMod", "nuke"));
        Assert.False(VyralBot.Core.Services.Moderation.ModTiers.CanRun("Admin", "nuke"));
        Assert.False(VyralBot.Core.Services.Moderation.ModTiers.CanRun("Admin", ""));
    }

    [Fact]
    public void UnknownTier_NeverRuns_Anything()
    {
        Assert.False(VyralBot.Core.Services.Moderation.ModTiers.CanRun("Owner", "kick"));
        Assert.False(VyralBot.Core.Services.Moderation.ModTiers.CanRun("", "kick"));
    }

    // ------------------------------------------------------------------
    // Minimum tier naming (for the "requires X or higher" replies)
    // ------------------------------------------------------------------

    [Fact]
    public void MinimumTier_Names_TheLowestTierThatCanRun()
    {
        Assert.Equal("TrialMod", VyralBot.Core.Services.Moderation.ModTiers.MinimumTier("kick"));
        Assert.Equal("TrialMod", VyralBot.Core.Services.Moderation.ModTiers.MinimumTier("purge"));
        Assert.Equal("Mod", VyralBot.Core.Services.Moderation.ModTiers.MinimumTier("addcurrency"));
        Assert.Equal("Mod", VyralBot.Core.Services.Moderation.ModTiers.MinimumTier("mute"));
        Assert.Equal("SeniorMod", VyralBot.Core.Services.Moderation.ModTiers.MinimumTier("ban"));
        Assert.Equal("SeniorMod", VyralBot.Core.Services.Moderation.ModTiers.MinimumTier("startgiveaway"));
        Assert.Equal("SeniorMod", VyralBot.Core.Services.Moderation.ModTiers.MinimumTier("draw"));
        Assert.Equal("Admin", VyralBot.Core.Services.Moderation.ModTiers.MinimumTier("setbalance"));
        Assert.Equal("Admin", VyralBot.Core.Services.Moderation.ModTiers.MinimumTier("assignmod"));
    }

    [Fact]
    public void MinimumTier_UnknownCommand_IsNull()
    {
        Assert.Null(VyralBot.Core.Services.Moderation.ModTiers.MinimumTier("nuke"));
        Assert.Null(VyralBot.Core.Services.Moderation.ModTiers.MinimumTier(""));
    }

    // ------------------------------------------------------------------
    // Stream-effect verbs (tier-gated smart lights / Stream Deck commands)
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("Mod", "flashlights", true)]
    [InlineData("TrialMod", "flashlights", false)]
    [InlineData("SeniorMod", "lightson", true)]
    [InlineData("SeniorMod", "lightsoff", true)]
    [InlineData("Mod", "lightson", false)]
    [InlineData("Admin", "streamdeck", true)]
    [InlineData("SeniorMod", "streamdeck", false)]
    public void CanRun_StreamEffectVerbs_AreTierGated(string tier, string command, bool expected)
        => Assert.Equal(expected, VyralBot.Core.Services.Moderation.ModTiers.CanRun(tier, command));

    [Fact]
    public void MinimumTier_NewVerbs_Reported()
    {
        Assert.Equal("Mod", VyralBot.Core.Services.Moderation.ModTiers.MinimumTier("flashlights"));
        Assert.Equal("SeniorMod", VyralBot.Core.Services.Moderation.ModTiers.MinimumTier("lightsoff"));
        Assert.Equal("Admin", VyralBot.Core.Services.Moderation.ModTiers.MinimumTier("streamdeck"));
    }
}

/// <summary>
/// ModRoleService persistence rules: upsert one ModRoleAssignment per
/// DiscordUserId, normalize the tier to canonical casing, and grant the backing
/// Discord role best-effort (a throwing Discord bot must never fail or corrupt
/// the persisted assignment; a missing bot must be fine too).
/// </summary>
public class ModRoleServiceTests
{
    private const string UserId = "424242";
    private const string ModChannel = "111222333";

    private static VyralBot.Core.Services.Moderation.ModRoleService MakeService(
        FakeDataService data, VyralBot.Core.Services.Moderation.IModDiscordBot? bot = null)
        => new(
            data,
            bot,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<VyralBot.Core.Services.Moderation.ModRoleService>.Instance);

    [Fact]
    public async Task Assign_Creates_Assignment_And_Grants_Discord_Role()
    {
        var data = new FakeDataService();
        var bot = new FakeDiscordBot();
        var svc = MakeService(data, bot);

        await svc.AssignAsync(UserId, "newmod", "Mod", "boss");

        Assert.Equal("Mod", await svc.GetTierAsync(UserId));
        var assignment = Assert.Single(data.ModRoleAssignments.Values);
        Assert.Equal("Mod", assignment.Tier);
        Assert.Equal("newmod", assignment.Username);
        Assert.Equal("boss", assignment.AssignedBy);

        var grant = Assert.Single(bot.GrantedRoles);
        Assert.Equal("newmod", grant.Username);
        Assert.Equal("Mod", grant.RoleIdOrName);
    }

    [Fact]
    public async Task Assign_Normalizes_Tier_Casing()
    {
        var data = new FakeDataService();
        var svc = MakeService(data, new FakeDiscordBot());

        await svc.AssignAsync(UserId, "newmod", "seniormod", "boss");

        Assert.Equal("SeniorMod", (await svc.GetAllAsync()).Single().Tier);
    }

    [Fact]
    public async Task Assign_Twice_Upserts_Single_Row()
    {
        var data = new FakeDataService();
        var svc = MakeService(data, new FakeDiscordBot());

        await svc.AssignAsync(UserId, "newmod", "TrialMod", "boss");
        await svc.AssignAsync(UserId, "newmod", "Admin", "boss");

        var assignment = Assert.Single(data.ModRoleAssignments.Values);
        Assert.Equal("Admin", assignment.Tier);
        Assert.Equal("Admin", await svc.GetTierAsync(UserId));
    }

    [Fact]
    public async Task Assign_UnknownTier_Throws()
    {
        var data = new FakeDataService();
        var svc = MakeService(data, new FakeDiscordBot());

        await Assert.ThrowsAsync<ArgumentException>(
            () => svc.AssignAsync(UserId, "newmod", "Owner", "boss"));

        Assert.Empty(data.ModRoleAssignments);
    }

    [Fact]
    public async Task Assign_Survives_Discord_Role_Failure()
    {
        var data = new FakeDataService();
        var svc = MakeService(data, new ThrowingDiscordBot());

        // The Discord grant is best-effort: a failed role grant must not
        // prevent (or corrupt) the persisted assignment.
        await svc.AssignAsync(UserId, "newmod", "Mod", "boss");

        Assert.Equal("Mod", await svc.GetTierAsync(UserId));
    }

    [Fact]
    public async Task Assign_Works_Without_Discord_Bot()
    {
        var data = new FakeDataService();
        var svc = MakeService(data, bot: null);

        await svc.AssignAsync(UserId, "newmod", "Mod", "boss");

        Assert.Equal("Mod", await svc.GetTierAsync(UserId));
    }

    [Fact]
    public async Task Unassign_Deletes_The_Assignment()
    {
        var data = new FakeDataService();
        var svc = MakeService(data, new FakeDiscordBot());
        await svc.AssignAsync(UserId, "newmod", "Mod", "boss");

        await svc.UnassignAsync(UserId);

        Assert.Null(await svc.GetTierAsync(UserId));
        Assert.Empty(data.ModRoleAssignments);
    }

    [Fact]
    public async Task GetTier_UnknownUser_IsNull()
    {
        var svc = MakeService(new FakeDataService());

        Assert.Null(await svc.GetTierAsync("nobody"));
    }

    [Fact]
    public async Task GetAll_Returns_All_Assignments()
    {
        var data = new FakeDataService();
        var svc = MakeService(data, new FakeDiscordBot());
        await svc.AssignAsync("1", "a", "Mod", "boss");
        await svc.AssignAsync("2", "b", "Admin", "boss");

        var all = await svc.GetAllAsync();

        Assert.Equal(2, all.Count);
        Assert.Contains(all, a => a.DiscordUserId == "1" && a.Tier == "Mod");
        Assert.Contains(all, a => a.DiscordUserId == "2" && a.Tier == "Admin");
    }
}
