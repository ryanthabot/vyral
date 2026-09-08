using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using VyralBot.Integrations.Obs.Engine;
using VyralBot.Integrations.Obs.Hosting;
using Xunit;

namespace VyralBot.UI.Tests;

/// <summary>
/// OBS-window-hosting Task 3a: the pure parts of installer acquisition — the
/// release-feed asset pick and the progress sequence with an injected feed and
/// process runner. The temp-file download itself stays real (served by a local
/// loopback listener); running the real NSIS installer is live-verified in the
/// plan's Task 5.
/// </summary>
public class ObsAcquisitionServiceTests
{
    private const string ReleaseJson = """
        {
          "tag_name": "31.1.2",
          "assets": [
            {
              "name": "OBS-Studio-31.1.2-Windows-Installer-x64.exe",
              "browser_download_url": "https://github.com/obsproject/obs-studio/releases/download/31.1.2/OBS-Studio-31.1.2-Windows-Installer-x64.exe",
              "size": 154323968
            },
            {
              "name": "OBS-Studio-31.1.2-Windows-Updater-x64.exe",
              "browser_download_url": "https://github.com/obsproject/obs-studio/releases/download/31.1.2/OBS-Studio-31.1.2-Windows-Updater-x64.exe",
              "size": 91234
            }
          ]
        }
        """;

    [Fact]
    public async Task ReleaseFeed_PicksWindowsInstallerAsset_AndSendsUserAgent()
    {
        string? userAgent = null;
        string? requestUrl = null;
        var handler = new StubHandler(_ =>
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(ReleaseJson, Encoding.UTF8, "application/json"),
            };
        })
        {
            OnSent = request =>
            {
                requestUrl = request.RequestUri?.ToString();
                userAgent = string.Join(" ", request.Headers.UserAgent);
            },
        };
        var feed = new GitHubReleaseFeed(new HttpClient(handler));

        (string Url, long Size)? installer = await feed.GetLatestWindowsInstallerAsync();

        Assert.NotNull(installer);
        Assert.Equal(
            "https://github.com/obsproject/obs-studio/releases/download/31.1.2/OBS-Studio-31.1.2-Windows-Installer-x64.exe",
            installer.Value.Url);
        Assert.Equal(154323968, installer.Value.Size);
        Assert.Equal(GitHubReleaseFeed.LatestReleaseUrl, requestUrl);
        Assert.Equal("VyralBot", userAgent); // the GitHub API rejects requests without a User-Agent
    }

    [Fact]
    public async Task DownloadAndInstall_InstallerSucceeds_YieldsDownloadingInstallingDone()
    {
        string installRoot = NewFakeInstallRoot(withObsDll: true);
        using var server = new LocalByteServer(4096);
        string? installerPath = null;
        try
        {
            var service = new ObsAcquisitionService(new ObsInstallLocator(installRoot));
            string? runnerPath = null;
            bool fileExistedAtRun = false;
            var progress = new List<ObsAcquireProgress>();

            await foreach (ObsAcquireProgress p in service.DownloadAndInstallAsync(
                               CancellationToken.None,
                               new StubFeed(server.Url, server.PayloadLength),
                               path =>
                               {
                                   runnerPath = path;
                                   fileExistedAtRun = File.Exists(path);
                                   return Task.FromResult(0);
                               }))
            {
                progress.Add(p);
            }

            installerPath = runnerPath;
            Assert.NotEmpty(progress);
            Assert.Equal(ObsAcquirePhase.Downloading, progress[0].Phase);
            Assert.Contains(progress, p => p.Phase == ObsAcquirePhase.Downloading && p.Percent == 100);
            Assert.Equal(ObsAcquirePhase.Installing, progress[^2].Phase);
            Assert.Equal(ObsAcquirePhase.Done, progress[^1].Phase);
            Assert.Equal("OBS Studio installed", progress[^1].Detail);
            Assert.NotNull(runnerPath);
            Assert.Equal(Path.GetTempPath(), Path.GetDirectoryName(runnerPath) + Path.DirectorySeparatorChar);
            Assert.StartsWith("obs-studio-installer-", Path.GetFileName(runnerPath), StringComparison.Ordinal);
            Assert.True(fileExistedAtRun);
        }
        finally
        {
            SafeDelete(installerPath!);
            SafeDeleteDir(installRoot);
        }
    }

    [Fact]
    public async Task DownloadAndInstall_InstallerExitNonzero_YieldsFailed()
    {
        // Override root without obs.dll — the locator is authoritative, so the
        // post-install detection must fail.
        string installRoot = NewFakeInstallRoot(withObsDll: false);
        using var server = new LocalByteServer(512);
        string? installerPath = null;
        try
        {
            var service = new ObsAcquisitionService(new ObsInstallLocator(installRoot));
            string? runnerPath = null;
            var progress = new List<ObsAcquireProgress>();

            await foreach (ObsAcquireProgress p in service.DownloadAndInstallAsync(
                               CancellationToken.None,
                               new StubFeed(server.Url, server.PayloadLength),
                               path =>
                               {
                                   runnerPath = path;
                                   return Task.FromResult(3);
                               }))
            {
                progress.Add(p);
            }

            installerPath = runnerPath;
            Assert.Contains(progress, p => p.Phase == ObsAcquirePhase.Installing);
            Assert.DoesNotContain(progress, p => p.Phase == ObsAcquirePhase.Done);
            Assert.Equal(ObsAcquirePhase.Failed, progress[^1].Phase);
            Assert.Contains("OBS was not detected", progress[^1].Detail, StringComparison.Ordinal);
        }
        finally
        {
            SafeDelete(installerPath!);
            SafeDeleteDir(installRoot);
        }
    }

    [Fact]
    public async Task DownloadAndInstall_TempInstallerPath_IsUniquePerRun()
    {
        string installRoot = NewFakeInstallRoot(withObsDll: true);
        using var server = new LocalByteServer(512);
        string? firstPath = null;
        string? secondPath = null;
        try
        {
            for (int run = 0; run < 2; run++)
            {
                var service = new ObsAcquisitionService(new ObsInstallLocator(installRoot));
                string? runnerPath = null;
                await foreach (ObsAcquireProgress p in service.DownloadAndInstallAsync(
                                   CancellationToken.None,
                                   new StubFeed(server.Url, server.PayloadLength),
                                   path =>
                                   {
                                       runnerPath = path;
                                       return Task.FromResult(0);
                                   }))
                {
                    // Drain the progress sequence.
                }

                if (run == 0)
                {
                    firstPath = runnerPath;
                }
                else
                {
                    secondPath = runnerPath;
                }
            }

            Assert.NotNull(firstPath);
            Assert.NotNull(secondPath);
            Assert.Equal(Path.GetTempPath(), Path.GetDirectoryName(firstPath) + Path.DirectorySeparatorChar);
            Assert.StartsWith("obs-studio-installer-", Path.GetFileName(firstPath), StringComparison.Ordinal);
            Assert.EndsWith(".exe", Path.GetFileName(firstPath), StringComparison.Ordinal);
            Assert.NotEqual(firstPath, secondPath); // no cross-run collision in %TEMP%
        }
        finally
        {
            SafeDelete(firstPath!);
            SafeDelete(secondPath!);
            SafeDeleteDir(installRoot);
        }
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    private static string NewFakeInstallRoot(bool withObsDll)
    {
        string root = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "vyralbot-obs-acq-" + Guid.NewGuid().ToString("N"))).FullName;
        if (withObsDll)
        {
            Directory.CreateDirectory(Path.Combine(root, "bin", "64bit"));
            File.WriteAllText(Path.Combine(root, "bin", "64bit", "obs.dll"), string.Empty);
        }

        return root;
    }

    private static void SafeDelete(string? path)
    {
        if (path is null)
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
    }

    private static void SafeDeleteDir(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public Action<HttpRequestMessage>? OnSent { get; init; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            OnSent?.Invoke(request);
            return Task.FromResult(respond(request));
        }
    }

    private sealed class StubFeed(string url, long size) : IReleaseFeed
    {
        public Task<(string Url, long Size)?> GetLatestWindowsInstallerAsync(CancellationToken ct = default) =>
            Task.FromResult<(string Url, long Size)?>((url, size));
    }

    /// <summary>
    /// Minimal loopback HTTP server standing in for the GitHub download host:
    /// one static payload served with an exact Content-Length so the percent
    /// math is exercised against a real HttpClient download.
    /// </summary>
    private sealed class LocalByteServer : IDisposable
    {
        private readonly TcpListener listener = new(IPAddress.Loopback, 0);
        private readonly byte[] payload;

        public LocalByteServer(int payloadLength)
        {
            payload = new byte[payloadLength];
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            Url = $"http://127.0.0.1:{port}/installer.exe";
            _ = Task.Run(AcceptLoopAsync);
        }

        public int PayloadLength => payload.Length;

        public string Url { get; }

        private async Task AcceptLoopAsync()
        {
            while (true)
            {
                TcpClient client;
                try
                {
                    client = await listener.AcceptTcpClientAsync();
                }
                catch (Exception)
                {
                    return; // listener stopped — test teardown
                }

                _ = Task.Run(() => ServeAsync(client));
            }
        }

        private async Task ServeAsync(TcpClient client)
        {
            using (client)
            await using (NetworkStream stream = client.GetStream())
            {
                var request = new byte[8192];
                int total = 0;
                while (total < request.Length)
                {
                    int read = await stream.ReadAsync(request.AsMemory(total));
                    if (read == 0)
                    {
                        return;
                    }

                    total += read;
                    if (Encoding.ASCII.GetString(request, 0, total).Contains("\r\n\r\n", StringComparison.Ordinal))
                    {
                        break;
                    }
                }

                byte[] head = Encoding.ASCII.GetBytes(
                    "HTTP/1.1 200 OK\r\nContent-Type: application/octet-stream\r\nConnection: close\r\n"
                    + $"Content-Length: {payload.Length}\r\n\r\n");
                await stream.WriteAsync(head);
                await stream.WriteAsync(payload);
                await stream.FlushAsync();
            }
        }

        public void Dispose() => listener.Stop();
    }
}
