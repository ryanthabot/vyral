using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VyralBot.Core.Services.Audio;
using VyralBot.Integrations.Kick;
using VyralBot.Integrations.Lights;
using VyralBot.Integrations.Twitch;
using VyralBot.Integrations.YouTube;
using VyralBot.Workflows.Persistence;
using VyralBot.Workflows.Runtime;
using ExecutionContext = VyralBot.Workflows.Runtime.ExecutionContext;
using KickSet = System.Func<bool, System.Threading.Tasks.Task<VyralBot.UI.Services.StreamActionResult>>;

namespace VyralBot.UI.Services.VyralDeck;

/// <summary>Outcome of one deck button press.</summary>
public sealed record DeckActionResult(bool Ok, string Message);

/// <summary>
/// Executes a VyralDeck button by dispatching to the Vyral services that
/// already exist (OBS remote control, platform chat, Helix mod actions, sounds,
/// alerts, workflow runtime, smart lights). Never throws — every failure comes
/// back as a ❌ message the desktop status line / mobile app can show.
/// </summary>
public sealed class DeckActionExecutor
{
    private const string Twitch = "twitch";
    private const string Kick = "kick";
    private const string YouTube = "youtube";

    private readonly IServiceProvider _services;
    private readonly ILogger<DeckActionExecutor> _logger;
    private readonly ObsRemoteBridge _obs;

    public DeckActionExecutor(
        IServiceProvider services,
        ILogger<DeckActionExecutor> logger,
        ObsRemoteBridge obs)
    {
        _services = services;
        _logger = logger;
        _obs = obs;
    }

    public async Task<DeckActionResult> ExecuteAsync(DeckButton button)
    {
        if (button.IsEmpty)
        {
            return new DeckActionResult(false, "This slot is empty — edit it first.");
        }

        try
        {
            return await ExecuteCoreAsync(button).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "VyralDeck action {Type} failed", button.ActionType);
            return new DeckActionResult(false, $"❌ {button.Label}: {ex.Message}");
        }
    }

    private async Task<DeckActionResult> ExecuteCoreAsync(DeckButton button)
    {
        switch (button.ActionType)
        {
            // ── OBS ──────────────────────────────────────────────────────
            case DeckActionType.ObsScene:
                return Msg(await _obs.SetSceneAsync(button.Param1).ConfigureAwait(false));
            case DeckActionType.ObsStreamStart:
                return Msg(await _obs.StartStreamAsync().ConfigureAwait(false));
            case DeckActionType.ObsStreamStop:
                return Msg(await _obs.StopStreamAsync().ConfigureAwait(false));

            // ── Chat send ────────────────────────────────────────────────
            case DeckActionType.ChatSend:
                return await SendChatAsync(button).ConfigureAwait(false);

            // ── Twitch (Helix via IStreamActionsService) ─────────────────
            case DeckActionType.TwitchClip:
                return Msg((await Actions().TwitchCreateClipAsync().ConfigureAwait(false)).Message);
            case DeckActionType.TwitchMarker:
                return Msg((await Actions().TwitchCreateMarkerAsync(NullIfEmpty(button.Param1)).ConfigureAwait(false)).Message);
            case DeckActionType.TwitchRaid:
                if (string.IsNullOrWhiteSpace(button.Param1))
                {
                    return new DeckActionResult(false, "❌ Raid needs a target channel — edit the button.");
                }
                return Msg((await Actions().TwitchRaidAsync(button.Param1.Trim()).ConfigureAwait(false)).Message);
            case DeckActionType.TwitchStopRaid:
                return Msg((await Actions().TwitchStopRaidAsync().ConfigureAwait(false)).Message);
            case DeckActionType.TwitchClearChat:
                return Msg((await Actions().TwitchClearChatAsync().ConfigureAwait(false)).Message);
            case DeckActionType.TwitchEmoteOnlyToggle:
                return Msg(await ToggleAsync(
                    () => Actions().TwitchGetChatSettingsAsync(),
                    state => Actions().TwitchSetEmoteOnlyAsync(!state.EmoteOnly),
                    "Emote-only").ConfigureAwait(false));
            case DeckActionType.TwitchFollowerOnlyToggle:
                return Msg(await ToggleAsync(
                    () => Actions().TwitchGetChatSettingsAsync(),
                    state => Actions().TwitchSetFollowerOnlyAsync(!state.FollowerOnly),
                    "Follower-only").ConfigureAwait(false));
            case DeckActionType.TwitchShieldToggle:
                return Msg(await ToggleAsync(
                    () => Actions().TwitchGetChatSettingsAsync(),
                    state => Actions().TwitchSetShieldModeAsync(!state.ShieldMode),
                    "Shield mode").ConfigureAwait(false));

            // ── Kick (mod chat commands) ─────────────────────────────────
            case DeckActionType.KickClearChat:
                return Msg((await Actions().KickClearChatAsync().ConfigureAwait(false)).Message);
            case DeckActionType.KickEmoteOnly:
                return await KickToggleAsync(s => s.KickSetEmoteOnlyAsync, button.Param2, "Emote-only").ConfigureAwait(false);
            case DeckActionType.KickFollowerOnly:
                return await KickToggleAsync(s => s.KickSetFollowerOnlyAsync, button.Param2, "Follower-only").ConfigureAwait(false);
            case DeckActionType.KickSlowMode:
                return await KickToggleAsync(s => s.KickSetSlowModeAsync, button.Param2, "Slow mode").ConfigureAwait(false);

            // ── Audio / alerts ───────────────────────────────────────────
            case DeckActionType.SoundPlay:
                return await PlaySoundAsync(button).ConfigureAwait(false);
            case DeckActionType.AlertShow:
                // Empty Param1 is valid (the default "Stream Deck" title is used),
                // so resolve the title once — button.Param1.Trim() would NRE below.
                var alertTitle = string.IsNullOrWhiteSpace(button.Param1) ? "Stream Deck" : button.Param1.Trim();
                await Alert().ShowAlertAsync(
                    alertTitle,
                    "Stream Deck",
                    NullIfEmpty(button.Param2),
                    applySettings: false).ConfigureAwait(false);
                return new DeckActionResult(true, $"✅ Alert '{alertTitle}' shown.");

            // ── Automation ───────────────────────────────────────────────
            case DeckActionType.WorkflowRun:
                return await RunWorkflowAsync(button.Param1).ConfigureAwait(false);

            // ── Smart home ───────────────────────────────────────────────
            case DeckActionType.LightsFlash:
            {
                var times = int.TryParse(button.Param2, out var parsed) && parsed is > 0 and <= 10 ? parsed : 3;
                var result = await Lights().FlashAsync(NullIfEmpty(button.Param1), times, 400).ConfigureAwait(false);
                return new DeckActionResult(true, $"💡 {result}");
            }
            case DeckActionType.LightsPower:
            {
                var on = !button.Param2.Trim().Equals("off", StringComparison.OrdinalIgnoreCase);
                var result = await Lights().SetPowerAsync(on).ConfigureAwait(false);
                return new DeckActionResult(true, $"💡 {result}");
            }

            // ── Advanced ─────────────────────────────────────────────────
            case DeckActionType.UriLaunch:
                if (string.IsNullOrWhiteSpace(button.Param1))
                {
                    return new DeckActionResult(false, "❌ This button has no URI — edit it first.");
                }
                Process.Start(new ProcessStartInfo(button.Param1.Trim()) { UseShellExecute = true });
                return new DeckActionResult(true, $"✅ Launched {button.Param1.Trim()}.");

            default:
                return new DeckActionResult(false, $"❌ Unknown action {button.ActionType}.");
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private IStreamActionsService Actions() => _services.GetRequiredService<IStreamActionsService>();

    private UI.Services.IAlertService Alert() => _services.GetRequiredService<UI.Services.IAlertService>();

    private ILightsClient Lights() => _services.GetRequiredService<ILightsClient>();

    private static DeckActionResult Msg(string message) =>
        new(message.StartsWith("✅", StringComparison.Ordinal) || message.StartsWith("💡", StringComparison.Ordinal), message);

    private static string? NullIfEmpty(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private async Task<DeckActionResult> SendChatAsync(DeckButton button)
    {
        if (string.IsNullOrWhiteSpace(button.Param1))
        {
            return new DeckActionResult(false, "❌ This button has no message — edit it first.");
        }

        var platform = string.IsNullOrWhiteSpace(button.Param2) ? Twitch : button.Param2.Trim().ToLowerInvariant();
        var message = button.Param1;

        switch (platform)
        {
            case Twitch:
            {
                var twitch = _services.GetRequiredService<ITwitchService>();
                if (!twitch.IsConnected)
                {
                    return new DeckActionResult(false, "❌ Twitch is not connected.");
                }
                await twitch.SendMessageAsync(message).ConfigureAwait(false);
                break;
            }
            case Kick:
            {
                var kick = _services.GetRequiredService<IKickService>();
                if (!kick.IsConnected)
                {
                    return new DeckActionResult(false, "❌ Kick is not connected.");
                }
                await kick.SendMessageAsync(message).ConfigureAwait(false);
                break;
            }
            case YouTube:
            {
                var youtube = _services.GetRequiredService<IYouTubeService>();
                if (!youtube.IsConnected)
                {
                    return new DeckActionResult(false, "❌ YouTube is not connected.");
                }
                await youtube.SendMessageAsync(message).ConfigureAwait(false);
                break;
            }
            default:
                return new DeckActionResult(false, $"❌ Unknown chat platform '{platform}' (use twitch, kick or youtube).");
        }

        return new DeckActionResult(true, $"✅ Sent to {char.ToUpperInvariant(platform[0])}{platform[1..]}: {message}");
    }

    /// <summary>Reads the current chat settings, then flips one mode to its opposite.</summary>
    private static async Task<string> ToggleAsync(
        Func<Task<TwitchChatSettingsState?>> read,
        Func<TwitchChatSettingsState, Task<StreamActionResult>> set,
        string label)
    {
        var state = await read().ConfigureAwait(false);
        if (state is null)
        {
            return $"❌ {label}: could not read the current chat settings (connected?).";
        }

        var result = await set(state).ConfigureAwait(false);
        return result.Message;
    }

    private async Task<DeckActionResult> KickToggleAsync(
        Func<IStreamActionsService, KickSet> pick,
        string param2,
        string label)
    {
        var on = !param2.Trim().Equals("off", StringComparison.OrdinalIgnoreCase);
        var result = await pick(Actions())(on).ConfigureAwait(false);
        return Msg(result.Message);
    }

    private async Task<DeckActionResult> PlaySoundAsync(DeckButton button)
    {
        if (string.IsNullOrWhiteSpace(button.Param1))
        {
            return new DeckActionResult(false, "❌ This button has no sound file — edit it first.");
        }

        var sound = _services.GetRequiredService<ISoundService>();
        var volume = float.TryParse(button.Param2, out var parsed) ? Math.Clamp(parsed, 0f, 1f) : 1f;
        await sound.PlaySoundFileAsync(button.Param1.Trim(), volume).ConfigureAwait(false);
        return new DeckActionResult(true, $"🔊 Played {button.Param1.Trim()}.");
    }

    private async Task<DeckActionResult> RunWorkflowAsync(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return new DeckActionResult(false, "❌ This button has no workflow name — edit it first.");
        }

        var data = _services.GetRequiredService<VyralBot.Data.IDataService>();
        var rows = await data.GetWorkflowsAsync().ConfigureAwait(false);
        var row = rows.FirstOrDefault(r => r.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase) || r.Id == name.Trim());
        if (row is null)
        {
            return new DeckActionResult(false, $"❌ No workflow named '{name.Trim()}'.");
        }

        var runtime = _services.GetRequiredService<WorkflowRuntime>();
        var workflow = WorkflowJson.FromRow(row);
        var context = new ExecutionContext(_services, _logger)
        {
            WorkflowId = row.Id,
            Platform = "StreamDeck",
        };
        var result = await runtime.ExecuteAsync(workflow, context).ConfigureAwait(false);
        var ok = result.Status.Equals("Completed", StringComparison.OrdinalIgnoreCase);
        return new DeckActionResult(
            ok,
            ok ? $"✅ Workflow '{workflow.Name}' completed." : $"❌ Workflow '{workflow.Name}': {result.Status}.");
    }
}
