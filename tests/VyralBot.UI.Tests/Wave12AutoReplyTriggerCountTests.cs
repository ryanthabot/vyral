using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using VyralBot.Data;
using VyralBot.Data.Entities;
using Xunit;

namespace VyralBot.UI.Tests;

/// <summary>
/// Wave-12 coverage for the wave-11 conditional-rules fix:
/// <see cref="DapperDataService.SaveAutoReplyRuleAsync"/> no longer writes
/// TriggerCount on the UPDATE path, because the row the ViewModel re-saves
/// typically carries a stale in-memory count. The live counter is owned by
/// <see cref="IDataService.IncrementRuleTriggerCountAsync"/> (TriggerCount+1
/// plus a LastTriggeredAt stamp); editing a rule must never reset or
/// fabricate it.
///
/// Runs against a real <see cref="DapperDataService"/> on a temp SQLite file
/// (the DataLayerNewTablesTests pattern), driving exactly the
/// insert/edit/increment paths ConditionalRulesViewModel uses.
/// </summary>
public class Wave12AutoReplyTriggerCountTests
{
    private static async Task<DapperDataService> CreateServiceAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vyral-wave12-rules-{Guid.NewGuid():N}.db");
        var svc = new DapperDataService($"Data Source={path}", NullLogger<DapperDataService>.Instance);
        await svc.EnsureDatabaseCreatedAsync();
        return svc;
    }

    private static AutoReplyRule MakeRule(string name, int triggerCount = 0) => new()
    {
        Name = name,
        Platform = "All",
        EventType = "Follow",
        ReplyTemplate = "thanks {username}",
        TriggerCount = triggerCount
    };

    [Fact]
    public async Task Insert_StoresInitialTriggerCount()
    {
        var svc = await CreateServiceAsync();

        var id = await svc.SaveAutoReplyRuleAsync(MakeRule("wave12-rule", triggerCount: 5));

        var stored = await svc.GetAutoReplyRuleAsync(id);
        Assert.NotNull(stored);
        Assert.Equal(5, stored!.TriggerCount);
    }

    [Fact]
    public async Task Update_WithStaleInMemoryCount_PreservesDbCount()
    {
        // THE wave-11 fix: the VM re-saves the rule it loaded earlier; any
        // triggers that happened since that load are only in the DB, so the
        // in-memory TriggerCount must not be written back.
        var svc = await CreateServiceAsync();
        var id = await svc.SaveAutoReplyRuleAsync(MakeRule("wave12-rule", triggerCount: 5));
        await svc.IncrementRuleTriggerCountAsync(id);
        await svc.IncrementRuleTriggerCountAsync(id);

        // Simulate the VM edit: the row it holds was fetched before the two
        // increments, and its copy claims 0.
        var staleEdit = await svc.GetAutoReplyRuleAsync(id);
        Assert.NotNull(staleEdit);
        staleEdit!.TriggerCount = 0;
        staleEdit.ReplyTemplate = "edited {username}";
        var savedId = await svc.SaveAutoReplyRuleAsync(staleEdit);

        Assert.Equal(id, savedId);
        var stored = await svc.GetAutoReplyRuleAsync(id);
        Assert.NotNull(stored);
        Assert.Equal(7, stored!.TriggerCount); // 5 + 2 increments, edit did not reset
        Assert.Equal("edited {username}", stored.ReplyTemplate); // the edit DID land
    }

    [Fact]
    public async Task Update_WithLargerInMemoryCount_DoesNotOverwriteDbCount()
    {
        // The column is absent from the UPDATE SET in both directions: a
        // fabricated higher count must not overwrite the live one either.
        var svc = await CreateServiceAsync();
        var id = await svc.SaveAutoReplyRuleAsync(MakeRule("wave12-rule", triggerCount: 5));

        var edit = await svc.GetAutoReplyRuleAsync(id);
        Assert.NotNull(edit);
        edit!.TriggerCount = 999;
        await svc.SaveAutoReplyRuleAsync(edit);

        Assert.Equal(5, (await svc.GetAutoReplyRuleAsync(id))!.TriggerCount);
    }

    [Fact]
    public async Task IncrementRuleTriggerCount_BumpsCountAndStampsLastTriggeredAt()
    {
        var svc = await CreateServiceAsync();
        var id = await svc.SaveAutoReplyRuleAsync(MakeRule("wave12-rule", triggerCount: 5));
        var before = await svc.GetAutoReplyRuleAsync(id);
        Assert.NotNull(before);
        Assert.Null(before!.LastTriggeredAt);

        await svc.IncrementRuleTriggerCountAsync(id);

        var after = await svc.GetAutoReplyRuleAsync(id);
        Assert.NotNull(after);
        Assert.Equal(6, after!.TriggerCount);
        Assert.NotNull(after.LastTriggeredAt);
    }

    [Fact]
    public async Task Lifecycle_InsertIncrementEditSaveIncrement_CountNeverResets()
    {
        var svc = await CreateServiceAsync();

        // Fresh rule, live counting, editor with a stale snapshot mid-flight.
        var id = await svc.SaveAutoReplyRuleAsync(MakeRule("wave12-rule", triggerCount: 0));
        await svc.IncrementRuleTriggerCountAsync(id);
        await svc.IncrementRuleTriggerCountAsync(id);
        Assert.Equal(2, (await svc.GetAutoReplyRuleAsync(id))!.TriggerCount);

        var staleEdit = await svc.GetAutoReplyRuleAsync(id);
        Assert.NotNull(staleEdit);
        staleEdit!.TriggerCount = 0; // snapshot from before the increments
        staleEdit.IsEnabled = false;
        await svc.SaveAutoReplyRuleAsync(staleEdit);

        var afterEdit = await svc.GetAutoReplyRuleAsync(id);
        Assert.NotNull(afterEdit);
        Assert.Equal(2, afterEdit!.TriggerCount);
        Assert.False(afterEdit.IsEnabled);

        await svc.IncrementRuleTriggerCountAsync(id);
        Assert.Equal(3, (await svc.GetAutoReplyRuleAsync(id))!.TriggerCount);
    }

    [Fact]
    public async Task Delete_RuleIsGone()
    {
        var svc = await CreateServiceAsync();
        var id = await svc.SaveAutoReplyRuleAsync(MakeRule("wave12-rule"));

        await svc.DeleteAutoReplyRuleAsync(id);

        Assert.Null(await svc.GetAutoReplyRuleAsync(id));
    }
}
