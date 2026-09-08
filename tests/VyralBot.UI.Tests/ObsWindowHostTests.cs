using System.IO;
using System.Text.Json;
using VyralBot.Integrations.Obs.Hosting;
using Xunit;

namespace VyralBot.UI.Tests;

/// <summary>
/// OBS-window-hosting Task 1: the pure parts — WS_* style-mask arithmetic and
/// the obs-websocket config editor (flag flip must preserve every other field,
/// including the password, which is never asserted into output/logs beyond the
/// temp file itself). Process launch, window discovery and embedding need a
/// real obs64.exe and are live-verified in the plan's Task 5.
/// </summary>
public class ObsWindowHostTests
{
    // ---------------------------------------------------------------------
    // ObsWindowStyle.StripTopLevelChrome (pure style-mask math)
    // ---------------------------------------------------------------------

    [Fact]
    public void StripTopLevelChrome_RemovesChromeKeepsPopupVisible()
    {
        long style = ObsWindowStyle.WsCaption | ObsWindowStyle.WsThickFrame
            | ObsWindowStyle.WsSysMenu | ObsWindowStyle.WsMinimizeBox
            | ObsWindowStyle.WsMaximizeBox | ObsWindowStyle.WsPopup
            | ObsWindowStyle.WsVisible;

        nint stripped = ObsWindowStyle.StripTopLevelChrome((nint)style);

        Assert.Equal(0L, stripped & ObsWindowStyle.TopLevelChromeMask);
        Assert.NotEqual(0L, stripped & ObsWindowStyle.WsPopup);
        Assert.NotEqual(0L, stripped & ObsWindowStyle.WsVisible);
    }

    [Theory]
    // Plan vector: WS_VISIBLE + WS_CLIPSIBLINGS + full chrome (0x14CF0000) →
    // both non-chrome bits survive.
    [InlineData(0x14CF_0000, 0x1400_0000)]
    // WS_POPUP|WS_VISIBLE plus chrome → both non-chrome bits survive.
    [InlineData(0x90CF_0000, 0x9000_0000)]
    // No chrome bits → value passes through untouched.
    [InlineData(0x4000_0000, 0x4000_0000)]
    public void StripTopLevelChrome_KnownStyles_LeavesNonChromeBits(long style, long expected)
    {
        Assert.Equal((nint)expected, ObsWindowStyle.StripTopLevelChrome((nint)style));
    }

    // ---------------------------------------------------------------------
    // ObsWebSocketConfigEditor (temp-file round-trips)
    // ---------------------------------------------------------------------

    [Fact]
    public void WebSocketConfigEditor_EnableServer_FlipsFlagPreservesEverything()
    {
        string dir = NewTempDir();
        try
        {
            // Exact production shape (flat JSON) plus an unknown field to
            // prove the rewrite is a round-trip, not a re-model.
            WriteConfig(dir, """
                {
                  "alerts_enabled": false,
                  "auth_required": true,
                  "first_load": false,
                  "future_key": 123,
                  "server_enabled": false,
                  "server_password": "abc123xyz",
                  "server_port": 4455
                }
                """);

            var before = ObsWebSocketConfigEditor.Read(dir);
            Assert.NotNull(before);
            Assert.False(before.ServerEnabled);
            Assert.Equal("abc123xyz", before.ServerPassword);
            Assert.Equal(4455, before.Port);
            Assert.True(before.AuthRequired);

            Assert.True(ObsWebSocketConfigEditor.EnableServer(dir));

            var after = ObsWebSocketConfigEditor.Read(dir);
            Assert.NotNull(after);
            Assert.True(after.ServerEnabled);
            Assert.Equal("abc123xyz", after.ServerPassword);
            Assert.Equal(4455, after.Port);
            Assert.True(after.AuthRequired);

            using var doc = JsonDocument.Parse(File.ReadAllText(ConfigPath(dir)));
            Assert.Equal(123, doc.RootElement.GetProperty("future_key").GetInt32());
            Assert.False(doc.RootElement.GetProperty("alerts_enabled").GetBoolean());
            Assert.False(doc.RootElement.GetProperty("first_load").GetBoolean());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void WebSocketConfigEditor_MissingFile_ReturnsFalse()
    {
        string dir = NewTempDir();
        try
        {
            Assert.False(ObsWebSocketConfigEditor.EnableServer(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void WebSocketConfigEditor_CorruptJson_ReturnsFalseWithoutThrow()
    {
        string dir = NewTempDir();
        try
        {
            WriteConfig(dir, "{ this is not json");

            Assert.False(ObsWebSocketConfigEditor.EnableServer(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Read_MissingFile_ReturnsNull()
    {
        string dir = NewTempDir();
        try
        {
            Assert.Null(ObsWebSocketConfigEditor.Read(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    private static string NewTempDir() =>
        Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "vyralbot-obs-ws-" + Guid.NewGuid().ToString("N"))).FullName;

    private static string ConfigPath(string dir) =>
        Path.Combine(dir, "plugin_config", "obs-websocket", "config.json");

    private static void WriteConfig(string dir, string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath(dir))!);
        File.WriteAllText(ConfigPath(dir), json);
    }
}
