namespace VyralBot.Integrations.Obs.Engine;

public sealed record ObsVideoSettings(
    uint BaseWidth,
    uint BaseHeight,
    uint OutputWidth,
    uint OutputHeight,
    uint FpsNum,
    uint FpsDen);

public sealed record ObsOutputSettings(
    string OutputMode,
    string? StreamEncoderId,
    string? RecordEncoderId,
    int StreamVideoBitrateKbps,
    int AudioBitrateKbps,
    string RecordDirectory,
    string RecordFormat);

/// <summary>Stream service from the profile's service.json. StreamKey is a secret.</summary>
public sealed record ObsStreamService(string ServiceType, string? Server, string? StreamKey);

/// <summary>
/// Snapshot of the OBS profile the user last selected. StreamKey/token values
/// are secrets: any rendering of this record must go through
/// <see cref="ToMaskedString"/> so nothing sensitive reaches logs or UI text.
/// </summary>
public sealed record ObsProfileSettings(
    string ProfileName,
    ObsVideoSettings Video,
    ObsOutputSettings Output,
    ObsStreamService Service)
{
    /// <summary>Safe-for-logs rendering — secret values are replaced with "***".</summary>
    public string ToMaskedString()
    {
        return $"Profile '{ProfileName}': {Video.BaseWidth}x{Video.BaseHeight} → {Video.OutputWidth}x{Video.OutputHeight} @ {Video.FpsNum}/{Video.FpsDen} fps | " +
               $"Output: {Output.OutputMode}, stream encoder {Output.StreamEncoderId ?? "default"}, " +
               $"video {Output.StreamVideoBitrateKbps} kbps, audio {Output.AudioBitrateKbps} kbps, " +
               $"record to {Output.RecordDirectory} ({Output.RecordFormat}) | " +
               $"Service: {(string.IsNullOrEmpty(Service.ServiceType) ? "not configured" : Service.ServiceType)}, " +
               $"server {Service.Server ?? "not set"}, key ***";
    }
}
