using VyralBot.Core.Services.EventBus.Events;
using Xunit;

namespace VyralBot.UI.Tests;

public class EntryRateWindowTests
{
    [Fact]
    public void Record_FiveEntries_CountInLastMinute_ReturnsFive()
    {
        var window = new EntryRateWindow();
        var now = new DateTime(2026, 9, 4, 12, 0, 0, DateTimeKind.Utc);

        for (var i = 0; i < 5; i++)
        {
            window.Record(now);
        }

        Assert.Equal(5, window.CountInLastMinute(now));
    }

    [Fact]
    public void CountInLastMinute_EntriesOlderThanSixtySeconds_ArePruned()
    {
        var window = new EntryRateWindow();
        var t0 = new DateTime(2026, 9, 4, 12, 0, 0, DateTimeKind.Utc);

        for (var i = 0; i < 3; i++)
        {
            window.Record(t0);
        }

        for (var i = 0; i < 2; i++)
        {
            window.Record(t0.AddSeconds(30));
        }

        // Past the 60s window the three oldest entries drop; the two at +30s remain.
        Assert.Equal(2, window.CountInLastMinute(t0.AddSeconds(61)));
    }

    [Fact]
    public void CountInLastMinute_EntriesExactlyAtWindowBoundary_ArePruned()
    {
        var window = new EntryRateWindow();
        var t0 = new DateTime(2026, 9, 4, 12, 0, 0, DateTimeKind.Utc);

        window.Record(t0);

        // Just inside the window the entry still counts (59s < 60s).
        Assert.Equal(1, window.CountInLastMinute(t0.AddSeconds(59)));

        // At exactly 60s (>= window) the entry is pruned.
        Assert.Equal(0, window.CountInLastMinute(t0.AddSeconds(60)));
    }

    [Fact]
    public void Record_ConcurrentRecordsFromMultipleTasks_CountsMatchTotal()
    {
        var window = new EntryRateWindow();
        var now = new DateTime(2026, 9, 4, 12, 0, 0, DateTimeKind.Utc);
        const int tasks = 4;
        const int recordsPerTask = 1000;

        var tasksList = new List<Task>();
        for (var t = 0; t < tasks; t++)
        {
            tasksList.Add(Task.Run(() =>
            {
                for (var i = 0; i < recordsPerTask; i++)
                {
                    window.Record(now);
                }
            }));
        }

        Task.WaitAll(tasksList.ToArray());

        Assert.Equal(tasks * recordsPerTask, window.CountInLastMinute(now.AddSeconds(1)));
    }
}
