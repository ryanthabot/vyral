using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VyralBot.Data;

namespace VyralBot.Core.Services.Counters;

/// <summary>
/// Handles the !death command family on top of two per-profile config rows
/// (no new table needed): "DeathCounter.Count" (plain int) and
/// "DeathCounter.History" (JSON, newest-first, capped at 50 events). Invoked
/// from the ChatViewModel chat pipeline exactly like QuoteService: the
/// returned string is sent on the ORIGINATING platform by the caller;
/// returning null lets other handlers process the message.
///
/// Commands:
///   !death                — current count (everyone)
///   !death+ / !death add  — +1 (moderator+)
///   !death add &lt;n&gt;         — +n (moderator+)
///   !death-               — −1, floored at 0 (moderator+)
///   !death set &lt;n&gt;         — set the count (moderator+)
///   !death reset          — back to 0, no confirmation but logged (moderator+)
///   !death help           — usage summary (everyone)
/// </summary>
public class DeathCounterService : IDeathCounterService
{
    private const string CountKey = "DeathCounter.Count";
    private const string HistoryKey = "DeathCounter.History";
    private const string Category = "DeathCounter";
    private const int HistoryCap = 50;

    private const int MaxCount = 999_999;
    private const int MaxAddPerCommand = 10_000;

    /// <summary>Serializes read-modify-write so two chat messages cannot lose an update.</summary>
    private static readonly SemaphoreSlim MutationGate = new(1, 1);

    /// <summary>History JSON uses { at, delta, by } camelCase like every widget payload.</summary>
    private static readonly JsonSerializerOptions HistoryJson = new(JsonSerializerDefaults.Web);

    private readonly ILogger<DeathCounterService> _logger;
    private readonly IDataService _dataService;

    public DeathCounterService(ILogger<DeathCounterService> logger, IDataService dataService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
    }

    /// <inheritdoc />
    public event EventHandler<int>? CountChanged;

    /// <inheritdoc />
    public async Task<string?> ProcessCommandAsync(string username, string platform, bool isModerator, string message)
    {
        if (string.IsNullOrWhiteSpace(message) || !message.StartsWith("!"))
            return null;

        var parts = message.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var cmd = parts[0].ToLowerInvariant();

        if (cmd != "!death" && cmd != "!death+" && cmd != "!death-")
            return null; // Not a death command — let the pipeline continue

        try
        {
            switch (cmd)
            {
                case "!death+":
                    return isModerator
                        ? FormatCount(await AddAsync(1, username))
                        : "Only moderators can change the death counter";

                case "!death-":
                    return isModerator
                        ? FormatCount(await SubtractAsync(1, username))
                        : "Only moderators can change the death counter";

                default:
                    if (parts.Length == 1)
                        return FormatCount(await GetCountAsync());

                    var sub = parts[1].ToLowerInvariant();

                    if (sub is "add" or "+")
                    {
                        if (!isModerator)
                            return "Only moderators can change the death counter";
                        var delta = 1;
                        if (parts.Length > 2 && !TryParseCount(parts[2], out delta))
                            return Usage;
                        return FormatCount(await AddAsync(delta, username));
                    }

                    if (sub is "-" or "sub" or "remove")
                    {
                        if (!isModerator)
                            return "Only moderators can change the death counter";
                        var delta = 1;
                        if (parts.Length > 2 && !TryParseCount(parts[2], out delta))
                            return Usage;
                        return FormatCount(await SubtractAsync(delta, username));
                    }

                    if (sub == "set")
                    {
                        if (!isModerator)
                            return "Only moderators can change the death counter";
                        if (parts.Length < 3 || !TryParseCount(parts[2], out var value))
                            return Usage;
                        await SetAsync(value, username);
                        return $"💀 Death counter set to {value}";
                    }

                    if (sub == "reset")
                    {
                        if (!isModerator)
                            return "Only moderators can change the death counter";
                        await ResetAsync(username);
                        return "💀 Death counter reset to 0";
                    }

                    if (sub == "help")
                        return Usage;

                    return Usage;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing death counter command: {Command}", message);
            return "An error occurred while processing the death counter command.";
        }
    }

    /// <inheritdoc />
    public async Task<int> GetCountAsync()
    {
        var raw = await _dataService.GetConfigValueAsync(CountKey);
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0
            ? value
            : 0;
    }

    /// <inheritdoc />
    public async Task<int> AddAsync(int delta, string by)
    {
        delta = Math.Clamp(delta, 1, MaxAddPerCommand);
        return await MutateAsync(by, count => Math.Min(count + delta, MaxCount));
    }

    /// <inheritdoc />
    public async Task<int> SubtractAsync(int delta, string by)
    {
        delta = Math.Clamp(delta, 1, MaxAddPerCommand);
        return await MutateAsync(by, count => Math.Max(count - delta, 0));
    }

    /// <inheritdoc />
    public Task<int> SetAsync(int value, string by) =>
        MutateAsync(by, _ => Math.Clamp(value, 0, MaxCount));

    /// <inheritdoc />
    public Task<int> ResetAsync(string by) => MutateAsync(by, _ => 0);

    /// <inheritdoc />
    public async Task<IReadOnlyList<DeathCounterEvent>> GetHistoryAsync(int limit = 10)
    {
        limit = Math.Clamp(limit, 1, HistoryCap);
        var events = await ReadHistoryAsync();
        return events.Take(limit).ToArray();
    }

    // ------------------------------------------------------------------
    // Shared mutation path (serialized, persists count + history, raises event)
    // ------------------------------------------------------------------

    private async Task<int> MutateAsync(string by, Func<int, int> apply)
    {
        await MutationGate.WaitAsync();
        try
        {
            var current = await GetCountAsync();
            var next = apply(current);
            await _dataService.SetConfigValueAsync(CountKey, next.ToString(CultureInfo.InvariantCulture), Category);

            var history = await ReadHistoryAsync();
            history.Insert(0, new DeathCounterEvent(DateTimeOffset.UtcNow, next - current, by));
            if (history.Count > HistoryCap)
            {
                history.RemoveRange(HistoryCap, history.Count - HistoryCap);
            }

            await _dataService.SetConfigValueAsync(HistoryKey, JsonSerializer.Serialize(history, HistoryJson), Category);

            _logger.LogInformation("Death counter {Delta:+#;-#;0} by {By} (was {Old}, now {New})",
                next - current, by, current, next);
            CountChanged?.Invoke(this, next);
            return next;
        }
        finally
        {
            MutationGate.Release();
        }
    }

    private async Task<List<DeathCounterEvent>> ReadHistoryAsync()
    {
        var raw = await _dataService.GetConfigValueAsync(HistoryKey);
        if (string.IsNullOrEmpty(raw))
            return new List<DeathCounterEvent>(capacity: 8);

        try
        {
            var parsed = JsonSerializer.Deserialize<List<DeathCounterEvent>>(raw, HistoryJson);
            return parsed ?? new List<DeathCounterEvent>(capacity: 8);
        }
        catch (JsonException)
        {
            // Corrupt/partial JSON (e.g. pre-capping write) — start over rather than fail every command.
            _logger.LogInformation("Death counter history was unreadable - starting a fresh history");
            return new List<DeathCounterEvent>(capacity: 8);
        }
    }

    private static string FormatCount(int count) => $"💀 Deaths: {count}";

    private const string Usage =
        "Usage: !death (count) • mods: !death+ / !death add <n>, !death-, !death set <n>, !death reset";

    /// <summary>Accepts plain non-negative integers only (no signs/exponents).</summary>
    private static bool TryParseCount(string text, out int value) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value >= 0;
}
