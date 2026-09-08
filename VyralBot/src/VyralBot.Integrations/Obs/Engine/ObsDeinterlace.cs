namespace VyralBot.Integrations.Obs.Engine;

/// <summary>
/// Maps the string values OBS writes into scene-collection JSON onto the int
/// enums behind obs_source_set_deinterlace_mode / _field_order
/// (obs.h @ 32.1.1: OBS_DEINTERLACE_MODE_DISABLE=0 … YADIF_2X=8;
/// FIELD_ORDER_TOP=0, BOTTOM=1). Absent or unknown values return null so the
/// executor skips the native call instead of passing a bogus enum value.
/// </summary>
internal static class ObsDeinterlace
{
    public static int? ParseMode(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "disable" => 0,
        "discard" => 1,
        "retro" => 2,
        "blend" => 3,
        "blend_2x" => 4,
        "linear" => 5,
        "linear_2x" => 6,
        "yadif" => 7,
        "yadif_2x" => 8,
        _ => null,
    };

    public static int? ParseFieldOrder(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "top" => 0,
        "bottom" => 1,
        _ => null,
    };
}
