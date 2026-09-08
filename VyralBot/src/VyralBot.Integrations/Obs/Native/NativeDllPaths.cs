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

    /// <summary>Appends a directory to the per-process DLL search path; the returned token stays valid for the process lifetime.</summary>
    [DllImport(Kernel32, EntryPoint = "AddDllDirectoryW", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern IntPtr AddDllDirectory(string lpPathName);

    /// <summary>LOAD_LIBRARY_SEARCH_DEFAULT_DIRS (libloaderapi.h) — for LoadLibraryExW when the engine needs to probe obs.dll explicitly.</summary>
    internal const uint LoadLibrarySearchDefaultDirs = 0x1000;
}
