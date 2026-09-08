using System.IO;
using System.Text;
using VyralBot.Integrations.Obs.Engine;
using Xunit;

namespace VyralBot.UI.Tests;

/// <summary>
/// Embedded-OBS Task 2: INI parser, FPS mapping, profile reader, secret
/// masking and install locator. All fixtures are inline strings / temp dirs —
/// no assert depends on a real OBS install or the machine's %APPDATA%.
/// </summary>
public class ObsIniParserTests
{
    [Fact]
    public void Parse_SectionsAndKeys_LookupCaseInsensitively()
    {
        var parsed = ObsIniParser.Parse("[Video]\nBaseCX=1920\nOUTPUTCY=1080\n");

        Assert.Equal(1, parsed.Count);
        Assert.True(parsed.ContainsKey("VIDEO"));
        Assert.Equal("1920", parsed["video"]["basecx"]);
        Assert.Equal("1080", parsed["Video"]["OutputCy"]);
        // original key casing is stored
        Assert.True(parsed["Video"].ContainsKey("OUTPUTCY"));
    }

    [Fact]
    public void Parse_FirstEqualsSplits_ValueKeptVerbatim()
    {
        // value = everything after the first '=' — the leading space and the
        // embedded '=' belong to the value.
        var parsed = ObsIniParser.Parse("[General]\nName = My = Show\nPath=C:\\a=b\n");

        Assert.Equal(" My = Show", parsed["General"]["Name"]);
        Assert.Equal("C:\\a=b", parsed["General"]["Path"]);
    }

    [Fact]
    public void Parse_CommentLines_AreIgnored()
    {
        var parsed = ObsIniParser.Parse("; header comment\n# another\n[General]\n;c\nName=X\n");

        Assert.Single(parsed);
        Assert.Single(parsed["General"]);
        Assert.Equal("X", parsed["General"]["Name"]);
    }

    [Fact]
    public void Parse_CRLFAndBlankLines_Tolerated()
    {
        var crlf = ObsIniParser.Parse("[Video]\r\nBaseCX=1920\r\n\r\nBaseCY=1080\r\n");
        var lf = ObsIniParser.Parse("[Video]\nBaseCX=1920\n\nBaseCY=1080\n");

        Assert.Equal(lf["Video"]["BaseCX"], crlf["Video"]["BaseCX"]);
        Assert.Equal(lf["Video"]["BaseCY"], crlf["Video"]["BaseCY"]);
    }

    [Fact]
    public void Parse_MalformedLines_Tolerated()
    {
        var parsed = ObsIniParser.Parse("orphan=1\n[Broken\n[General]\nnovalue\nName=X\n");

        // keys before any section, unclosed headers and lines without '=' are dropped
        Assert.False(parsed.ContainsKey("orphan"));
        Assert.Single(parsed);
        Assert.Equal("X", parsed["General"]["Name"]);
    }
}

public class ObsIniFileTests
{
    private static string WriteTempIni(byte[] bytes)
    {
        var path = Path.Combine(Path.GetTempPath(), $"vyral-obs-ini-{Guid.NewGuid():N}.ini");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static void DeleteBestEffort(params string[] paths)
    {
        foreach (var path in paths)
        {
            try { File.Delete(path); } catch { /* best-effort cleanup */ }
        }
    }

    [Fact]
    public void Load_HonorsUtf8Bom()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("[General]\r\nName=Test\r\n")).ToArray();
        var path = WriteTempIni(bytes);
        try
        {
            var file = ObsIniFile.Load(path);

            Assert.True(file.TryGetString("GENERAL", "name", out var value));
            Assert.Equal("Test", value);
        }
        finally { DeleteBestEffort(path); }
    }

    [Fact]
    public void Load_HonorsUtf16Bom()
    {
        var bytes = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("[Video]\nBaseCX=2560\n")).ToArray();
        var path = WriteTempIni(bytes);
        try
        {
            var file = ObsIniFile.Load(path);

            Assert.NotNull(file.GetSection("video"));
            Assert.Equal("2560", file.GetSection("video")!["BaseCX"]);
        }
        finally { DeleteBestEffort(path); }
    }

    [Fact]
    public void Load_WithoutBom_AssumesUtf8()
    {
        var path = WriteTempIni(Encoding.UTF8.GetBytes("[General]\nName=Ünicöde\n"));
        try
        {
            var file = ObsIniFile.Load(path);

            Assert.Equal("Ünicöde", file.GetSection("General")!["Name"]);
        }
        finally { DeleteBestEffort(path); }
    }

    [Fact]
    public void TryGetString_MissingSectionOrKey_ReturnsFalseWithEmptyValue()
    {
        var file = new ObsIniFile(ObsIniParser.Parse("[A]\nk=v\n"));

        Assert.False(file.TryGetString("B", "k", out var missingSection));
        Assert.Equal(string.Empty, missingSection);
        Assert.False(file.TryGetString("A", "other", out var missingKey));
        Assert.Equal(string.Empty, missingKey);
    }

    [Fact]
    public void GetInt_ParsesOrFallsBack()
    {
        var file = new ObsIniFile(ObsIniParser.Parse("[SimpleOutput]\nVBitrate=6000\nABitrate=abc\n"));

        Assert.Equal(6000, file.GetInt("simpleoutput", "vbitrate", 2500));
        Assert.Equal(2500, file.GetInt("SimpleOutput", "MissingKey", 2500));
        Assert.Equal(160, file.GetInt("SimpleOutput", "ABitrate", 160)); // garbage → fallback
    }
}

public class ObsProfileReaderTests
{
    private static string CreateTempDir() => Directory.CreateTempSubdirectory("vyral-obs-profile-").FullName;

    private static void DeleteDirBestEffort(string path)
    {
        try { Directory.Delete(path, recursive: true); } catch { /* best-effort cleanup */ }
    }

    private static string CreateProfileDir(string configDir, string name, string basicIni)
    {
        var dir = Path.Combine(configDir, "basic", "profiles", name);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "basic.ini"), basicIni, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        return dir;
    }

    private static void WriteConfigIni(string configDir, string fileName, string content)
    {
        File.WriteAllText(Path.Combine(configDir, fileName), content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    private const string SimpleModeIni = """
        [General]
        Name=My Profile

        [Video]
        BaseCX=2560
        BaseCY=1440
        OutputCX=1920
        OutputCY=1080
        FPSType=Common
        FPSCommon=60

        [Output]
        Mode=Simple

        [SimpleOutput]
        VBitrate=6000
        ABitrate=160
        FilePath=C:\\Videos
        RecFormat2=mkv
        StreamEncoder=x264
        """;

    private const string AdvancedModeIni = """
        [General]
        Name=Adv

        [Output]
        Mode=Advanced

        [AdvOut]
        Encoder=obs_x264
        RecEncoder=ffmpeg_muxer
        Track1Bitrate=160
        RecFilePath=D:\\Recordings
        RecFormat2=mp4
        """;

    [Fact]
    public void Read_SimpleMode_MapsVideoOutputAndService()
    {
        var dir = CreateTempDir();
        try
        {
            var profileDir = CreateProfileDir(dir, "Untitled", SimpleModeIni);
            File.WriteAllText(Path.Combine(profileDir, "service.json"),
                """{"type":"rtmp_custom","settings":{"server":"rtmp://example/live","key":"live_key_12345"}}""");
            var reader = new ObsProfileReader(ObsProfileTestTree.InstallFor(dir));

            var result = reader.ReadCurrentProfile();

            Assert.True(result.Success, result.Error);
            var settings = result.Settings!;
            Assert.Equal("My Profile", settings.ProfileName);
            Assert.Equal(2560u, settings.Video.BaseWidth);
            Assert.Equal(1440u, settings.Video.BaseHeight);
            Assert.Equal(1920u, settings.Video.OutputWidth);
            Assert.Equal(1080u, settings.Video.OutputHeight);
            Assert.Equal(60u, settings.Video.FpsNum);
            Assert.Equal(1u, settings.Video.FpsDen);
            Assert.Equal("Simple", settings.Output.OutputMode);
            Assert.Equal("x264", settings.Output.StreamEncoderId);
            Assert.Equal(6000, settings.Output.StreamVideoBitrateKbps);
            Assert.Equal(160, settings.Output.AudioBitrateKbps);
            // OBS inis store the path as C:\\Videos — reader normalizes separators
            Assert.Equal(@"C:\Videos", settings.Output.RecordDirectory);
            Assert.Equal("mkv", settings.Output.RecordFormat);
            Assert.Equal("rtmp_custom", settings.Service.ServiceType);
            Assert.Equal("rtmp://example/live", settings.Service.Server);
            Assert.Equal("live_key_12345", settings.Service.StreamKey);
        }
        finally { DeleteDirBestEffort(dir); }
    }

    [Fact]
    public void Read_AdvancedMode_PicksAdvOutValues()
    {
        var dir = CreateTempDir();
        try
        {
            CreateProfileDir(dir, "Untitled", AdvancedModeIni);
            var reader = new ObsProfileReader(ObsProfileTestTree.InstallFor(dir));

            var result = reader.ReadCurrentProfile();

            Assert.True(result.Success, result.Error);
            var output = result.Settings!.Output;
            Assert.Equal("Advanced", output.OutputMode);
            Assert.Equal("obs_x264", output.StreamEncoderId);
            Assert.Equal("ffmpeg_muxer", output.RecordEncoderId);
            Assert.Equal(160, output.StreamVideoBitrateKbps);
            Assert.Equal(160, output.AudioBitrateKbps);
            // escaped separators normalized
            Assert.Equal(@"D:\Recordings", output.RecordDirectory);
            Assert.Equal("mp4", output.RecordFormat);
        }
        finally { DeleteDirBestEffort(dir); }
    }

    [Fact]
    public void Read_MissingServiceJson_SuccessWithNullService()
    {
        var dir = CreateTempDir();
        try
        {
            CreateProfileDir(dir, "Untitled", SimpleModeIni);
            var reader = new ObsProfileReader(ObsProfileTestTree.InstallFor(dir));

            var result = reader.ReadCurrentProfile();

            Assert.True(result.Success, result.Error);
            Assert.Equal(string.Empty, result.Settings!.Service.ServiceType);
            Assert.Null(result.Settings.Service.Server);
            Assert.Null(result.Settings.Service.StreamKey);
        }
        finally { DeleteDirBestEffort(dir); }
    }

    [Fact]
    public void Read_MalformedServiceJson_SuccessWithNullService()
    {
        var dir = CreateTempDir();
        try
        {
            var profileDir = CreateProfileDir(dir, "Untitled", SimpleModeIni);
            File.WriteAllText(Path.Combine(profileDir, "service.json"), """{"type":"rtmp_custom","settings":{""");
            var reader = new ObsProfileReader(ObsProfileTestTree.InstallFor(dir));

            var result = reader.ReadCurrentProfile();

            Assert.True(result.Success, result.Error);
            Assert.Null(result.Settings!.Service.Server);
            Assert.Null(result.Settings.Service.StreamKey);
        }
        finally { DeleteDirBestEffort(dir); }
    }

    [Fact]
    public void Read_UserIniProfileDir_WinsOverAlphabeticalOrder()
    {
        var dir = CreateTempDir();
        try
        {
            CreateProfileDir(dir, "AAlpha", "[General]\nName=AAlphaProfile\n");
            CreateProfileDir(dir, "BSecond", "[General]\nName=BSecondProfile\n");
            WriteConfigIni(dir, "user.ini", "[Basic]\nProfileDir=BSecond\n");
            var reader = new ObsProfileReader(ObsProfileTestTree.InstallFor(dir));

            var result = reader.ReadCurrentProfile();

            Assert.True(result.Success, result.Error);
            Assert.Equal("BSecondProfile", result.Settings!.ProfileName);
        }
        finally { DeleteDirBestEffort(dir); }
    }

    [Fact]
    public void Read_GlobalIniProfileDir_UsedWhenNoUserIni()
    {
        var dir = CreateTempDir();
        try
        {
            CreateProfileDir(dir, "AAlpha", "[General]\nName=AAlphaProfile\n");
            CreateProfileDir(dir, "BSecond", "[General]\nName=BSecondProfile\n");
            WriteConfigIni(dir, "global.ini", "[Basic]\nProfileDir=BSecond\n");
            var reader = new ObsProfileReader(ObsProfileTestTree.InstallFor(dir));

            var result = reader.ReadCurrentProfile();

            Assert.True(result.Success, result.Error);
            Assert.Equal("BSecondProfile", result.Settings!.ProfileName);
        }
        finally { DeleteDirBestEffort(dir); }
    }

    [Fact]
    public void Read_NoCurrentProfileKey_FallsBackToFirstAlphabeticalProfile()
    {
        var dir = CreateTempDir();
        try
        {
            CreateProfileDir(dir, "BSecond", "[General]\nName=BSecondProfile\n");
            CreateProfileDir(dir, "AAlpha", "[General]\nName=AAlphaProfile\n");
            var reader = new ObsProfileReader(ObsProfileTestTree.InstallFor(dir));

            var result = reader.ReadCurrentProfile();

            Assert.True(result.Success, result.Error);
            Assert.Equal("AAlphaProfile", result.Settings!.ProfileName);
        }
        finally { DeleteDirBestEffort(dir); }
    }

    [Fact]
    public void Read_MissingVideoSection_UsesFullHd60Defaults()
    {
        var dir = CreateTempDir();
        try
        {
            CreateProfileDir(dir, "Untitled", "[General]\nName=Bare\n");
            var reader = new ObsProfileReader(ObsProfileTestTree.InstallFor(dir));

            var result = reader.ReadCurrentProfile();

            Assert.True(result.Success, result.Error);
            var video = result.Settings!.Video;
            Assert.Equal(1920u, video.BaseWidth);
            Assert.Equal(1080u, video.BaseHeight);
            Assert.Equal(1920u, video.OutputWidth);
            Assert.Equal(1080u, video.OutputHeight);
            Assert.Equal(60u, video.FpsNum);
            Assert.Equal(1u, video.FpsDen);
            // output defaults
            Assert.Equal("Simple", result.Settings.Output.OutputMode);
            Assert.Equal(2500, result.Settings.Output.StreamVideoBitrateKbps);
            Assert.Equal(160, result.Settings.Output.AudioBitrateKbps);
            Assert.Equal("mkv", result.Settings.Output.RecordFormat);
        }
        finally { DeleteDirBestEffort(dir); }
    }

    [Fact]
    public void Read_NoProfilesAtAll_FailsFriendly()
    {
        var dir = CreateTempDir();
        var reader = new ObsProfileReader(ObsProfileTestTree.InstallFor(dir));
        try
        {
            var result = reader.ReadCurrentProfile();

            Assert.False(result.Success);
            Assert.False(string.IsNullOrWhiteSpace(result.Error));
            Assert.Null(result.Settings);
        }
        finally { DeleteDirBestEffort(dir); }
    }

    [Theory]
    [InlineData("Common", "60", "", "", "", 60u, 1u)]
    [InlineData("Common", "59.94", "", "", "", 60000u, 1001u)]
    [InlineData("Common", "29.97", "", "", "", 30000u, 1001u)]
    [InlineData("Common", "23.976", "", "", "", 24000u, 1001u)]
    [InlineData("Common", "50", "", "", "", 50u, 1u)]
    [InlineData("Common", "24", "", "", "", 24u, 1u)]
    [InlineData("Common", "nonsense", "", "", "", 60u, 1u)]
    [InlineData("Fractional", "", "", "24000", "1001", 24000u, 1001u)]
    [InlineData("Integer", "", "30", "", "", 30u, 1u)]
    [InlineData("", "", "", "", "", 60u, 1u)]
    public void Read_MapsFpsToExactRational(string fpsType, string fpsCommon, string fpsInt, string fpsNum, string fpsDen, uint expectedNum, uint expectedDen)
    {
        var dir = CreateTempDir();
        try
        {
            var ini = $"""
                [General]
                Name=Fps

                [Video]
                FPSType={fpsType}
                FPSCommon={fpsCommon}
                FPSInt={fpsInt}
                FPSNum={fpsNum}
                FPSDen={fpsDen}
                """;
            CreateProfileDir(dir, "Untitled", ini);
            var reader = new ObsProfileReader(ObsProfileTestTree.InstallFor(dir));

            var result = reader.ReadCurrentProfile();

            Assert.True(result.Success, result.Error);
            Assert.Equal(expectedNum, result.Settings!.Video.FpsNum);
            Assert.Equal(expectedDen, result.Settings.Video.FpsDen);
        }
        finally { DeleteDirBestEffort(dir); }
    }
}

public class ObsMaskingTests
{
    [Fact]
    public void ToMaskedString_ReplacesStreamKeyWithStars()
    {
        var dir = Directory.CreateTempSubdirectory("vyral-obs-mask-").FullName;
        try
        {
            var profileDir = Path.Combine(dir, "basic", "profiles", "Untitled");
            Directory.CreateDirectory(profileDir);
            File.WriteAllText(Path.Combine(profileDir, "basic.ini"), "[General]\nName=My Profile\n");
            File.WriteAllText(Path.Combine(profileDir, "service.json"),
                """{"type":"rtmp_custom","settings":{"server":"rtmp://example/live","key":"super_secret_stream_key_42"}}""");
            var reader = new ObsProfileReader(ObsProfileTestTree.InstallFor(dir));

            var masked = reader.ReadCurrentProfile().Settings!.ToMaskedString();

            Assert.Contains("***", masked);
            Assert.DoesNotContain("super_secret_stream_key_42", masked);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort cleanup */ }
        }
    }

    [Fact]
    public void ToMaskedString_KeepsNonSecretsReadable()
    {
        var dir = Directory.CreateTempSubdirectory("vyral-obs-mask-").FullName;
        try
        {
            var profileDir = Path.Combine(dir, "basic", "profiles", "Untitled");
            Directory.CreateDirectory(profileDir);
            File.WriteAllText(Path.Combine(profileDir, "basic.ini"), "[General]\nName=My Profile\n");
            File.WriteAllText(Path.Combine(profileDir, "service.json"),
                """{"type":"rtmp_custom","settings":{"server":"rtmp://example/live","key":"super_secret_stream_key_42"}}""");
            var reader = new ObsProfileReader(ObsProfileTestTree.InstallFor(dir));

            var masked = reader.ReadCurrentProfile().Settings!.ToMaskedString();

            Assert.Contains("My Profile", masked);
            Assert.Contains("rtmp://example/live", masked);
            Assert.Contains("rtmp_custom", masked);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort cleanup */ }
        }
    }
}

public class ObsInstallLocatorTests
{
    private static string CreateTempDir() => Directory.CreateTempSubdirectory("vyral-obs-install-").FullName;

    [Fact]
    public void Locate_OverrideWithBin64Layout_ReturnsInstall()
    {
        var root = CreateTempDir();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "bin", "64bit"));
            File.WriteAllText(Path.Combine(root, "bin", "64bit", "obs.dll"), "stub");
            Directory.CreateDirectory(Path.Combine(root, "obs-plugins", "64bit"));
            Directory.CreateDirectory(Path.Combine(root, "data", "obs-plugins"));

            var install = new ObsInstallLocator(root).Locate();

            Assert.NotNull(install);
            Assert.Equal(root, install!.Root);
            Assert.Equal(Path.Combine(root, "bin", "64bit"), install.BinDir);
            Assert.Equal(Path.Combine(root, "bin", "64bit", "obs.dll"), install.ObsDllPath);
            Assert.Equal(Path.Combine(root, "obs-plugins", "64bit"), install.PluginsBinDir);
            Assert.Equal(Path.Combine(root, "data", "obs-plugins"), install.PluginsDataDir);
            Assert.EndsWith("obs-studio", install.ConfigDir);
        }
        finally { DeleteDirBestEffort(root); }
    }

    [Fact]
    public void Locate_OverridePointingAtFlatLayout_UsesRootAsBinDir()
    {
        var root = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(root, "obs.dll"), "stub");

            var install = new ObsInstallLocator(root).Locate();

            Assert.NotNull(install);
            Assert.Equal(root, install!.BinDir);
            Assert.Equal(Path.Combine(root, "obs.dll"), install.ObsDllPath);
        }
        finally { DeleteDirBestEffort(root); }
    }

    [Fact]
    public void Locate_EmptyOverrideDir_ReturnsNullWithoutFallback()
    {
        var empty = CreateTempDir();
        try
        {
            Assert.Null(new ObsInstallLocator(empty).Locate());
        }
        finally { DeleteDirBestEffort(empty); }
    }

    private static void DeleteDirBestEffort(string path)
    {
        try { Directory.Delete(path, recursive: true); } catch { /* best-effort cleanup */ }
    }
}

/// <summary>Builds ObsInstall records for temp config trees used by the reader tests.</summary>
internal static class ObsProfileTestTree
{
    public static ObsInstall InstallFor(string configDir)
    {
        var root = Path.Combine(configDir, "_install");
        return new ObsInstall(
            Root: root,
            BinDir: Path.Combine(root, "bin", "64bit"),
            ObsDllPath: Path.Combine(root, "bin", "64bit", "obs.dll"),
            PluginsBinDir: Path.Combine(root, "obs-plugins", "64bit"),
            PluginsDataDir: Path.Combine(root, "data", "obs-plugins"),
            UserPluginsBinDir: null,
            UserPluginsDataDir: null,
            ConfigDir: configDir,
            SceneCollectionsDir: Path.Combine(configDir, "basic", "scenes"),
            ProfilesDir: Path.Combine(configDir, "basic", "profiles"));
    }
}
