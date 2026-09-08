namespace VyralBot.Data.Entities;

/// <summary>An imported OBS scene collection. RawJsonBytes is the VERBATIM file — the canonical store; everything else is a derived projection.</summary>
public class ObsSceneCollection
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public byte[] RawJsonBytes { get; set; } = Array.Empty<byte>();
    public string SourcePath { get; set; } = "";
    public int SceneCount { get; set; }
    public int SourceCount { get; set; }
    public DateTime ImportedAtUtc { get; set; } = DateTime.UtcNow;
}
