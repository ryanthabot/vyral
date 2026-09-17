using System.Text.Json;
using Microsoft.Extensions.Logging;
using VyralBot.Data;

namespace VyralBot.UI.Services.VyralDeck;

/// <summary>
/// Owns the deck layout (pages of buttons): the source of truth lives in the
/// profile config KV store; an in-memory copy is shared by the desktop page and
/// the LAN server. Every save raises <see cref="LayoutChanged"/> so the server
/// rebroadcasts the new layout to connected phones and the desktop page
/// refreshes when a phone edited it.
/// </summary>
public sealed class DeckLayoutStore
{
    private const string LayoutKey = "VyralDeck.Layout";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly IDataService _data;
    private readonly ILogger<DeckLayoutStore> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DeckLayout? _cache;

    /// <summary>Raised after a layout was persisted (from the desktop page or a phone).</summary>
    public event Action<DeckLayout>? LayoutChanged;

    public DeckLayoutStore(IDataService data, ILogger<DeckLayoutStore> logger)
    {
        _data = data;
        _logger = logger;
    }

    public async Task<DeckLayout> LoadAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_cache is { } cached)
            {
                return cached;
            }

            var json = await _data.GetConfigValueAsync(LayoutKey).ConfigureAwait(false);
            var layout = string.IsNullOrWhiteSpace(json)
                ? null
                : TryDeserialize(json);
            layout ??= DeckLayout.CreateDefault();
            Normalize(layout);
            _cache = layout;
            return layout;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(DeckLayout layout)
    {
        Normalize(layout);
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await _data.SetConfigValueAsync(
                LayoutKey,
                JsonSerializer.Serialize(layout, Json),
                "VyralDeck").ConfigureAwait(false);
            _cache = layout;
        }
        finally
        {
            _gate.Release();
        }

        LayoutChanged?.Invoke(layout);
    }

    /// <summary>Reloads from disk and rebroadcasts — used after another process (or a phone) saved.</summary>
    public async Task<DeckLayout> ReloadAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var json = await _data.GetConfigValueAsync(LayoutKey).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(json) && TryDeserialize(json) is { } fresh)
            {
                Normalize(fresh);
                _cache = fresh;
            }
            return _cache ??= DeckLayout.CreateDefault();
        }
        finally
        {
            _gate.Release();
        }
    }

    public DeckLayout Cached => _cache ?? DeckLayout.CreateDefault();

    private DeckLayout? TryDeserialize(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<DeckLayout>(json, Json);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "VyralDeck layout JSON was invalid; starting from defaults");
            return null;
        }
    }

    /// <summary>Guarantees every page has exactly <see cref="DeckPage.SlotsPerPage"/> slots.</summary>
    private static void Normalize(DeckLayout layout)
    {
        foreach (var page in layout.Pages)
        {
            page.Name = string.IsNullOrWhiteSpace(page.Name) ? "Page" : page.Name.Trim();
            if (page.Buttons is null)
            {
                page.Buttons = new List<DeckButton>();
            }

            while (page.Buttons.Count < DeckPage.SlotsPerPage)
            {
                page.Buttons.Add(new DeckButton());
            }

            if (page.Buttons.Count > DeckPage.SlotsPerPage)
            {
                page.Buttons.RemoveRange(DeckPage.SlotsPerPage, page.Buttons.Count - DeckPage.SlotsPerPage);
            }
        }
    }
}
