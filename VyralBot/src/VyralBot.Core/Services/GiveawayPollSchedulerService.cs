using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VyralBot.Data;
using VyralBot.Data.Entities;
using VyralBot.Core.Services.EventBus;
using VyralBot.Core.Services.EventBus.Events;

namespace VyralBot.Core.Services;

/// <summary>
/// Background service that auto-ends giveaways and polls at their
/// ScheduledEndTime. The UI lets streamers schedule an end time, but nothing
/// enforced it — this ticks every 30 seconds, completes the active row when
/// its schedule passes (drawing winners / collecting poll results via the
/// existing services), and publishes an announcement event per platform
/// routing in the UI.
/// </summary>
public class GiveawayPollSchedulerService : BackgroundService
{
    private readonly ILogger<GiveawayPollSchedulerService> _logger;
    private readonly IDataService _dataService;
    private readonly GiveawayService _giveawayService;
    private readonly PollService _pollService;
    private readonly IEventBus _eventBus;
    private readonly TimeSpan _checkInterval = TimeSpan.FromSeconds(30);

    public GiveawayPollSchedulerService(
        ILogger<GiveawayPollSchedulerService> logger,
        IDataService dataService,
        GiveawayService giveawayService,
        PollService pollService,
        IEventBus eventBus)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        _giveawayService = giveawayService ?? throw new ArgumentNullException(nameof(giveawayService));
        _pollService = pollService ?? throw new ArgumentNullException(nameof(pollService));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("GiveawayPollSchedulerService started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessScheduledEndsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in giveaway/poll scheduler background service");
            }

            try
            {
                await Task.Delay(_checkInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("GiveawayPollSchedulerService stopped");
    }

    private async Task ProcessScheduledEndsAsync(CancellationToken ct)
    {
        try
        {
            await ProcessGiveawayAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error auto-ending the scheduled giveaway");
        }

        try
        {
            await ProcessPollAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error auto-ending the scheduled poll");
        }
    }

    private async Task ProcessGiveawayAsync(CancellationToken ct)
    {
        var giveaway = await _dataService.GetActiveGiveawayAsync();

        // Skip silently when there is no active giveaway or no scheduled end
        // time (or the schedule has not passed yet).
        if (giveaway == null || giveaway.Status != GiveawayStatus.Active)
            return;

        if (giveaway.ScheduledEndTime == null || giveaway.ScheduledEndTime.Value > DateTime.UtcNow)
            return;

        string summaryText;
        var entries = (await _dataService.GetGiveawayEntriesAsync(giveaway.Id)).ToList();

        if (entries.Count == 0)
        {
            // Re-read right before completing so a concurrent draw/cancel is
            // never double-processed.
            var fresh = await _dataService.GetGiveawayAsync(giveaway.Id);
            if (fresh == null || fresh.Status != GiveawayStatus.Active)
                return;

            fresh.Status = GiveawayStatus.Completed;
            fresh.EndedAt = DateTime.UtcNow;
            fresh.UpdatedAt = DateTime.UtcNow;
            await _dataService.SaveGiveawayAsync(fresh);

            _logger.LogInformation("Giveaway auto-ended with no entries: {Name} (ID: {Id})", fresh.Name, fresh.Id);
            summaryText = $"🎁 Giveaway '{fresh.Name}' has ended — no entries this time.";
        }
        else
        {
            // DrawWinnersAsync re-reads the giveaway by ID and only proceeds
            // while it is still Active, completing and saving it in one step.
            var (success, winners, _) = await _giveawayService.DrawWinnersAsync(giveaway.Id);
            if (!success)
            {
                // e.g. entries vanished between the count and the draw; the
                // next tick picks the row up again as a no-entries completion.
                _logger.LogWarning("Scheduled giveaway end skipped (draw failed): {Name}", giveaway.Name);
                return;
            }

            summaryText = $"🎁 Giveaway '{giveaway.Name}' has ended! Congratulations {string.Join(", ", winners)}!";
        }

        await _eventBus.PublishAsync(new GiveawayAutoEndedEvent
        {
            Source = "Scheduler",
            Title = giveaway.Name,
            SummaryText = summaryText,
            AllowedPlatforms = giveaway.AllowedPlatforms
        }, ct);
    }

    private async Task ProcessPollAsync(CancellationToken ct)
    {
        var poll = await _dataService.GetActivePollAsync();

        // Skip silently when there is no active poll or no scheduled end time
        // (or the schedule has not passed yet).
        if (poll == null || poll.Status != PollStatus.Active)
            return;

        if (poll.ScheduledEndTime == null || poll.ScheduledEndTime.Value > DateTime.UtcNow)
            return;

        // EndPollAsync re-reads the poll by ID and only proceeds while it is
        // still Active, then computes results, completes and saves it.
        var (success, results, _) = await _pollService.EndPollAsync(poll.Id);
        if (!success)
        {
            _logger.LogWarning("Scheduled poll end skipped (end failed): {Question}", poll.Question);
            return;
        }

        // Results keys are the option labels in the poll's option order.
        var resultsText = string.Join(", ", results.Select(r => $"{r.Key}: {r.Value}"));

        await _eventBus.PublishAsync(new PollAutoEndedEvent
        {
            Source = "Scheduler",
            Question = poll.Question,
            SummaryText = $"📊 Poll '{poll.Question}' has ended! Results: {resultsText}",
            AllowedPlatforms = poll.AllowedPlatforms
        }, ct);
    }
}
