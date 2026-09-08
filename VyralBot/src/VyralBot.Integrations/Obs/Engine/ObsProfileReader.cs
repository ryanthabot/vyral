using System.Globalization;
using System.Text.Json;

namespace VyralBot.Integrations.Obs.Engine;

public sealed record ObsProfileResult(bool Success, string? Error, ObsProfileSettings? Settings);

/// <summary>
/// Reads the OBS profile the user last selected (basic.ini + service.json)
/// from a located OBS install. Read-only: nothing is written back and secrets
/// never appear in errors or result strings — render profiles only via
/// ObsProfileSettings.ToMaskedString.
/// </summary>
public sealed class ObsProfileReader
{
    // FPSCommon entries OBS offers map to exact rationals; unknown values fall back to 60/1.
    private static readonly Dictionary<string, (uint Num, uint Den)> CommonFps = new(StringComparer.OrdinalIgnoreCase)
    {
        ["60"] = (60, 1),
        ["59.94"] = (60000, 1001),
        ["50"] = (50, 1),
        ["30"] = (30, 1),
        ["29.97"] = (30000, 1001),
        ["24"] = (24, 1),
        ["23.976"] = (24000, 1001),
    };

    private readonly ObsInstall install;

    public ObsProfileReader(ObsInstall install)
    {
        this.install = install;
    }

    public ObsProfileResult ReadCurrentProfile()
    {
        try
        {
            var profileDir = ResolveProfileDir();
            if (profileDir is null)
            {
                return new ObsProfileResult(false, $"No OBS profile found under {install.ProfilesDir}.", null);
            }

            var basicIniPath = Path.Combine(profileDir, "basic.ini");
            var ini = File.Exists(basicIniPath)
                ? ObsIniFile.Load(basicIniPath)
                : new ObsIniFile(ObsIniParser.Parse(string.Empty));

            var profileName = TrimmedOrNull(ini, "General", "Name") ?? Path.GetFileName(profileDir);
            var settings = new ObsProfileSettings(
                ProfileName: profileName,
                Video: ReadVideo(ini),
                Output: ReadOutput(ini),
                Service: ReadService(Path.Combine(profileDir, "service.json")));

            return new ObsProfileResult(true, null, settings);
        }
        catch (Exception ex)
        {
            // ex.Message can contain paths but never file contents, so no secret can leak here.
            return new ObsProfileResult(false, $"Reading the OBS profile failed: {ex.Message}", null);
        }
    }

    private string? ResolveProfileDir()
    {
        var configured = ReadConfiguredProfileDirName();
        if (configured is not null && IsSafeDirName(configured))
        {
            var dir = Path.Combine(install.ProfilesDir, configured);
            if (Directory.Exists(dir))
            {
                return dir;
            }
        }

        if (!Directory.Exists(install.ProfilesDir))
        {
            return null;
        }

        return Directory.EnumerateDirectories(install.ProfilesDir)
            .OrderBy(static d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    // Modern OBS (30.2+) stores the active profile in user.ini [Basic] ProfileDir;
    // older builds kept it in global.ini. On this machine user.ini carries
    // ProfileDir=Untitled while global.ini has no profile key at all.
    private string? ReadConfiguredProfileDirName()
    {
        foreach (var fileName in (string[])["user.ini", "global.ini"])
        {
            var path = Path.Combine(install.ConfigDir, fileName);
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                var ini = ObsIniFile.Load(path);
                if (TrimmedOrNull(ini, "Basic", "ProfileDir") is { } dirName)
                {
                    return dirName;
                }

                if (TrimmedOrNull(ini, "Basic", "Profile") is { } profileName)
                {
                    return profileName;
                }
            }
            catch
            {
                // unreadable config file — fall through to the next source
            }
        }

        return null;
    }

    private static ObsVideoSettings ReadVideo(ObsIniFile ini)
    {
        var (fpsNum, fpsDen) = ReadFps(ini);
        return new ObsVideoSettings(
            BaseWidth: GetUInt(ini, "Video", "BaseCX", 1920),
            BaseHeight: GetUInt(ini, "Video", "BaseCY", 1080),
            OutputWidth: GetUInt(ini, "Video", "OutputCX", 1920),
            OutputHeight: GetUInt(ini, "Video", "OutputCY", 1080),
            FpsNum: fpsNum,
            FpsDen: fpsDen);
    }

    private static (uint Num, uint Den) ReadFps(ObsIniFile ini)
    {
        var fpsType = TrimmedOrNull(ini, "Video", "FPSType");
        if (string.Equals(fpsType, "Fractional", StringComparison.OrdinalIgnoreCase))
        {
            var num = GetUInt(ini, "Video", "FPSNum", 60);
            var den = GetUInt(ini, "Video", "FPSDen", 1);
            return num > 0 && den > 0 ? (num, den) : (60u, 1u);
        }

        if (string.Equals(fpsType, "Integer", StringComparison.OrdinalIgnoreCase))
        {
            var whole = GetUInt(ini, "Video", "FPSInt", 60);
            return whole > 0 ? (whole, 1u) : (60u, 1u);
        }

        // "Common" (and anything unparseable) — exact rational for the well-known values.
        var common = TrimmedOrNull(ini, "Video", "FPSCommon");
        if (common is not null && CommonFps.TryGetValue(common, out var rational))
        {
            return rational;
        }

        return common is not null
            && uint.TryParse(common, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer)
            && integer > 0
                ? (integer, 1u)
                : (60u, 1u);
    }

    private static ObsOutputSettings ReadOutput(ObsIniFile ini)
    {
        var advanced = string.Equals(TrimmedOrNull(ini, "Output", "Mode"), "Advanced", StringComparison.OrdinalIgnoreCase);

        string? streamEncoderId;
        string? recordEncoderId;
        int streamVideoBitrate;
        int audioBitrate;
        string recordDirectory;
        string recordFormat;

        if (advanced)
        {
            streamEncoderId = TrimmedOrNull(ini, "AdvOut", "Encoder");
            recordEncoderId = TrimmedOrNull(ini, "AdvOut", "RecEncoder");
            // The plan maps the video encoder bitrate to Track1Bitrate in Advanced mode
            // (the per-encoder value lives in streamEncoder.json, read later by outputs).
            streamVideoBitrate = ini.GetInt("AdvOut", "Track1Bitrate", 2500);
            audioBitrate = ini.GetInt("AdvOut", "Track1Bitrate", 160);
            recordDirectory = PathValue(ini, "AdvOut", "RecFilePath");
            recordFormat = TrimmedOrNull(ini, "AdvOut", "RecFormat2") ?? "mkv";
        }
        else
        {
            streamEncoderId = TrimmedOrNull(ini, "SimpleOutput", "StreamEncoder");
            recordEncoderId = TrimmedOrNull(ini, "SimpleOutput", "RecEncoder");
            streamVideoBitrate = ini.GetInt("SimpleOutput", "VBitrate", 2500);
            audioBitrate = ini.GetInt("SimpleOutput", "ABitrate", 160);
            recordDirectory = PathValue(ini, "SimpleOutput", "FilePath");
            recordFormat = TrimmedOrNull(ini, "SimpleOutput", "RecFormat2") ?? "mkv";
        }

        return new ObsOutputSettings(
            OutputMode: advanced ? "Advanced" : "Simple",
            StreamEncoderId: streamEncoderId,
            RecordEncoderId: recordEncoderId,
            StreamVideoBitrateKbps: streamVideoBitrate,
            AudioBitrateKbps: audioBitrate,
            RecordDirectory: recordDirectory,
            RecordFormat: recordFormat);
    }

    /// <summary>
    /// Parses service.json generically (type + settings.server + settings.key).
    /// A missing or malformed file is not an error here — starting a stream will
    /// surface it later; and the file content (contains the stream key) is never
    /// included in any message.
    /// </summary>
    private static ObsStreamService ReadService(string path)
    {
        if (!File.Exists(path))
        {
            return new ObsStreamService(string.Empty, null, null);
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            var type = root.TryGetProperty("type", out var typeElement) && typeElement.ValueKind == JsonValueKind.String
                ? typeElement.GetString() ?? string.Empty
                : string.Empty;

            string? server = null;
            string? key = null;
            if (root.TryGetProperty("settings", out var settings) && settings.ValueKind == JsonValueKind.Object)
            {
                server = GetStringOrNull(settings, "server");
                key = GetStringOrNull(settings, "key");
            }

            return new ObsStreamService(type, server, key);
        }
        catch (JsonException)
        {
            return new ObsStreamService(string.Empty, null, null);
        }
    }

    private static string? GetStringOrNull(JsonElement parent, string name)
    {
        return parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    /// <summary>
    /// Path-valued ini entry. OBS stores Windows paths with escaped separators
    /// (C:\\Users\\…) — normalize them to single separators.
    /// </summary>
    private static string PathValue(ObsIniFile ini, string section, string key)
    {
        return (TrimmedOrNull(ini, section, key) ?? DefaultVideosDirectory()).Replace("\\\\", "\\");
    }

    private static string DefaultVideosDirectory()
    {
        var videos = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        return videos.Length > 0 ? videos : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }

    private static string? TrimmedOrNull(ObsIniFile ini, string section, string key)
    {
        return ini.TryGetString(section, key, out var value) && value.Trim().Length > 0
            ? value.Trim()
            : null;
    }

    private static uint GetUInt(ObsIniFile ini, string section, string key, uint fallback)
    {
        return ini.TryGetString(section, key, out var raw)
            && uint.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;
    }

    private static bool IsSafeDirName(string name)
    {
        return name.AsSpan().IndexOfAny('/', '\\', ':') < 0;
    }
}
