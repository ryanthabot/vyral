using System.Runtime.InteropServices;
using VyralBot.Integrations.Obs.Native;
using Xunit;

namespace VyralBot.UI.Tests;

/// <summary>
/// Managed-side layout asserts for the libobs P/Invoke layer, derived from the
/// installed OBS version's headers (verified against tag 32.1.1 — the version
/// reported by C:\Program Files\obs-studio\bin\64bit\obs64.exe).
///
/// These are pure managed asserts: they pin Marshal.SizeOf/OffsetOf to the
/// values computed from the C headers so any drift (struct field reorder,
/// type widen, Pack mistake) fails a test instead of corrupting native memory.
/// </summary>
public class ObsNativeLayoutTests
{
    private static void RequireX64()
    {
        // obs.dll is 64-bit only; pointer-sized fields make every assert here
        // meaningless (and the interop unusable) in a 32-bit test process.
        Assert.True(Environment.Is64BitProcess,
            "ObsNative layout tests must run in a 64-bit process (libobs interop is x64-only). " +
            "Check PlatformTarget on the test/UI projects.");
    }

    [Fact]
    public void ObsVideoInfo_Matches32_1_1_HeaderLayout()
    {
        RequireX64();

        // obs.h @ 32.1.1 (struct obs_video_info, lines 191-219), x64 math:
        //   graphics_module  char*            offset  0, 8 bytes
        //   fps_num          uint32           offset  8
        //   fps_den          uint32           offset 12
        //   base_width       uint32           offset 16
        //   base_height      uint32           offset 20
        //   output_width     uint32           offset 24
        //   output_height    uint32           offset 28
        //   output_format    enum (int)       offset 32
        //   adapter          uint32           offset 36
        //   gpu_conversion   bool (1 byte)    offset 40
        //   colorspace       enum (int)       offset 44  (3 bytes pad after the bool)
        //   range            enum (int)       offset 48
        //   scale_type       enum (int)       offset 52
        //   → sizeof = 56 (max alignment 8 already satisfied)
        // NOTE: the pre-28.0 d3d11_adapters[16] tail is GONE at 32.1.1 — no 320-byte layout.
        Assert.Equal(56, Marshal.SizeOf<ObsVideoInfo>());
    }

    [Fact]
    public void ObsVideoInfo_FieldOffsets_MatchHeaderOrder()
    {
        RequireX64();

        Assert.Equal(32, (int)Marshal.OffsetOf<ObsVideoInfo>(nameof(ObsVideoInfo.OutputFormat)));
        Assert.Equal(36, (int)Marshal.OffsetOf<ObsVideoInfo>(nameof(ObsVideoInfo.Adapter)));
        Assert.Equal(40, (int)Marshal.OffsetOf<ObsVideoInfo>(nameof(ObsVideoInfo.GpuConversion)));
        // The byte→enum transition must land on the 3-byte pad, not overwrite it.
        Assert.Equal(44, (int)Marshal.OffsetOf<ObsVideoInfo>(nameof(ObsVideoInfo.Colorspace)));
        Assert.Equal(48, (int)Marshal.OffsetOf<ObsVideoInfo>(nameof(ObsVideoInfo.Range)));
        Assert.Equal(52, (int)Marshal.OffsetOf<ObsVideoInfo>(nameof(ObsVideoInfo.ScaleType)));
    }

    [Fact]
    public void ObsAudioInfo_Is8Bytes()
    {
        RequireX64();

        // obs.h @ 32.1.1: { uint32_t samples_per_sec; enum speaker_layout speakers; } → 8.
        Assert.Equal(8, Marshal.SizeOf<ObsAudioInfo>());
    }

    [Fact]
    public void ObsInitData_MatchesGsInitData32_1_1()
    {
        RequireX64();

        // graphics.h @ 32.1.1 (struct gs_init_data, lines 492-499), x64 math:
        //   window          { void* hwnd }   offset  0, 8 bytes
        //   cx              uint32           offset  8
        //   cy              uint32           offset 12
        //   num_backbuffers uint32           offset 16
        //   format          enum (int)       offset 20
        //   zsformat        enum (int)       offset 24
        //   adapter         uint32           offset 28
        //   → sizeof = 32
        Assert.Equal(32, Marshal.SizeOf<ObsInitData>());
    }

    [Fact]
    public void VerifiedEnumConstants_MatchHeaders()
    {
        // Every value below was read off the raw headers at tag 32.1.1.
        // video-io.h: enum video_format — NONE=0, I420=1, NV12=2 (lines 34-39).
        Assert.Equal(2, ObsNativeConstants.VideoFormatNv12);
        // video-io.h: enum video_colorspace — VIDEO_CS_DEFAULT = 0 (line 107).
        Assert.Equal(0, ObsNativeConstants.ColorspaceDefault);
        // video-io.h: enum video_range_type — VIDEO_RANGE_DEFAULT = 0 (line 116).
        Assert.Equal(0, ObsNativeConstants.RangeDefault);
        // video-io.h: enum video_scale_type — VIDEO_SCALE_DEFAULT = 0 (line 271).
        Assert.Equal(0, ObsNativeConstants.ScaleTypeDefault);
        // audio-io.h: enum speaker_layout — UNKNOWN=0, MONO=1, STEREO=2 (lines 66-69).
        Assert.Equal(2, ObsNativeConstants.SpeakersStereo);
        // graphics.h: enum gs_color_format — UNKNOWN=0, A8=1, R8=2, RGBA=3 (lines 56-60).
        Assert.Equal(3, ObsNativeConstants.ColorFormatRgba);
        // graphics.h: enum gs_zstencil_format — GS_ZS_NONE = 0 (line 90).
        Assert.Equal(0, ObsNativeConstants.ZStencilNone);
    }
}
