using System.Runtime.InteropServices;

namespace VyralBot.Integrations.Obs.Native;

/// <summary>
/// kernel32 DLL search-path controls needed before loading obs.dll from the
/// user's OBS install. obs.dll's dependencies (avcodec, libobs-d3d11, …) live
/// in the same bin\64bit directory, so the engine adds that directory to the
/// per-process DLL search path instead of mutating the machine state.
/// </summary>
internal static class NativeDllPaths
{
    private const string Kernel32 = "kernel32";

    /// <summary>Replaces the per-process "user directories" searched for DLL dependencies.</summary>
    [DllImport(Kernel32, EntryPoint = "SetDllDirectoryW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetDllDirectory(string lpPathName);

    /// <summary>Appends a directory to the per-process DLL search path; the returned token stays valid for the process lifetime.
    /// Unicode-only API — kernel32 exports a single name WITHOUT the W suffix
    /// (verified via GetProcAddress on the machine's kernel32).</summary>
    [DllImport(Kernel32, EntryPoint = "AddDllDirectory", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern IntPtr AddDllDirectory(string lpPathName);

    /// <summary>LOAD_LIBRARY_SEARCH_DEFAULT_DIRS (libloaderapi.h) — for LoadLibraryExW when the engine needs to probe obs.dll explicitly.</summary>
    internal const uint LoadLibrarySearchDefaultDirs = 0x1000;

    /// <summary>LOAD_WITH_ALTERED_SEARCH_PATH (libloaderapi.h) — with a full path, the DLL's own directory is searched first for its dependencies.</summary>
    internal const uint LoadWithAlteredSearchPath = 0x8;

    /// <summary>Loads a library explicitly. With a full path + LoadWithAlteredSearchPath, obs.dll's dependencies (avcodec, …) resolve from its own directory.</summary>
    [DllImport(Kernel32, EntryPoint = "LoadLibraryExW", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern IntPtr LoadLibraryEx(string lpLibFileName, IntPtr hFile, uint dwFlags);
}
