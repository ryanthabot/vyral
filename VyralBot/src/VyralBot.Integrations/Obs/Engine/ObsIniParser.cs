using System.Globalization;

namespace VyralBot.Integrations.Obs.Engine;

/// <summary>
/// Minimal INI parser for OBS config files (basic.ini, user.ini, global.ini).
/// Sections and keys are case-insensitive for lookups while keeping their
/// original casing. The value is everything after the first '=' — verbatim,
/// including spaces and any further '=' characters.
/// </summary>
public static class ObsIniParser
{
    public static IReadOnlyDictionary<string, Dictionary<string, string>> Parse(string text)
    {
        var sections = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(text))
        {
            return sections;
        }

        Dictionary<string, string>? current = null;
        foreach (var line in text.Split(["\r\n", "\n"], StringSplitOptions.None))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith(';') || trimmed.StartsWith('#'))
            {
                continue;
            }

            if (trimmed.StartsWith('['))
            {
                var end = trimmed.IndexOf(']');
                if (end < 0)
                {
                    current = null; // malformed header — drop keys until the next valid section
                    continue;
                }

                var name = trimmed[1..end].Trim();
                if (!sections.TryGetValue(name, out current))
                {
                    current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    sections[name] = current;
                }

                continue;
            }

            var equals = line.IndexOf('=');
            if (equals <= 0 || current is null)
            {
                continue; // line without '=', or a key before any section
            }

            var key = line[..equals].Trim();
            if (key.Length > 0)
            {
                current[key] = line[(equals + 1)..];
            }
        }

        return sections;
    }
}

/// <summary>
/// Case-insensitive lookup helper over a parsed OBS ini. Real OBS files
/// (basic.ini, user.ini, global.ini) are UTF-8 with BOM and LF line endings —
/// Load honors the BOM (UTF-8/UTF-16) and otherwise assumes UTF-8.
/// </summary>
public sealed class ObsIniFile
{
    private readonly IReadOnlyDictionary<string, Dictionary<string, string>> sections;

    public ObsIniFile(IReadOnlyDictionary<string, Dictionary<string, string>> sections)
    {
        this.sections = sections;
    }

    public static ObsIniFile Load(string path)
    {
        // File.ReadAllText sniffs the BOM and falls back to UTF-8 when absent.
        return new ObsIniFile(ObsIniParser.Parse(File.ReadAllText(path)));
    }

    public IReadOnlyDictionary<string, string>? GetSection(string name)
    {
        return sections.TryGetValue(name, out var section) ? section : null;
    }

    public bool TryGetString(string section, string key, out string value)
    {
        // assign explicitly: TryGetValue writes default (null) on failure
        if (sections.TryGetValue(section, out var entries) && entries.TryGetValue(key, out var found))
        {
            value = found;
            return true;
        }

        value = string.Empty;
        return false;
    }

    public int GetInt(string section, string key, int fallback)
    {
        return TryGetString(section, key, out var raw)
            && int.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;
    }
}
