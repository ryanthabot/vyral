namespace VyralBot.Integrations.Lights;

/// <summary>Controls configured smart lights. Every method returns a user-facing result string and never throws to the caller (mod commands run in chat).</summary>
public interface ILightsClient
{
    Task<string> FlashAsync(string? hexColor, int times, int intervalMs, CancellationToken ct = default);
    Task<string> SetPowerAsync(bool on, CancellationToken ct = default);
    Task<List<LightInfo>> GetLightsAsync(CancellationToken ct = default);
}
