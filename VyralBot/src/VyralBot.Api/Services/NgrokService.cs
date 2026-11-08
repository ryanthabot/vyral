using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using VyralBot.Core.Services.ProcessManagement;
using VyralBot.Core.Services.Profiles;

namespace VyralBot.Api.Services;

public class NgrokService : IDisposable
{
    private readonly ILogger<NgrokService> _logger;
    private readonly IProfileService _profileService;
    private readonly IProcessManager _processManager;
    private readonly HttpClient _httpClient;
    private Process? _ngrokProcess;
    private string? _currentPublicUrl;
    private Timer? _monitorTimer;
    private bool _isConnected;
    private int _consecutiveHealthFailures;
    private int _healthCheckRunning;
    private readonly string _ngrokApiUrl = "http://127.0.0.1:4040/api/tunnels";

    public bool IsConnected => _isConnected;
    public string? PublicUrl => _currentPublicUrl;
    public string? AuthToken { get; private set; }

    public event EventHandler<string>? PublicUrlChanged;

    public NgrokService(ILogger<NgrokService> logger, IProfileService profileService, IProcessManager processManager)
    {
        _logger = logger;
        _profileService = profileService;
        _processManager = processManager;
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
    }

    public async Task<bool> StartTunnelAsync(int localPort, string? authToken = null, string region = "us")
    {
        if (_isConnected)
        {
            _logger.LogWarning("Ngrok tunnel is already running");
            return true;
        }

        try
        {
            _logger.LogInformation("Starting ngrok tunnel for port {Port}...", localPort);

            // Require auth token
            if (string.IsNullOrEmpty(authToken))
            {
                _logger.LogError("Ngrok auth token is required. Please complete ngrok setup first.");
                return false;
            }

            // Check if ngrok is installed
            if (!IsNgrokInstalled())
            {
                _logger.LogError("Ngrok executable not found. Please install ngrok.");
                return false;
            }

            // Auth token travels via environment variable, never the command
            // line (args are visible to any local process listing processes).
            var args = $"http {localPort} --region {region}";

            // Start ngrok through the tracked ProcessManager (zero-orphans rule): the
            // shutdown kill-all backstop then covers ngrok even if this service is
            // never disposed cleanly. StartProcessAsync throws on failure, which the
            // catch below logs. Its "Process started" log deliberately omits args —
            // the ngrok command line carries the auth token.
            var startInfo = new ProcessStartInfo
            {
                FileName = "ngrok",
                Arguments = args,
                EnvironmentVariables = { ["NGROK_AUTHTOKEN"] = authToken },
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            _ngrokProcess = await _processManager.StartProcessAsync(startInfo);

            // Wait for ngrok to start and get the public URL (retry a few times)
            string? publicUrl = null;
            for (int attempt = 0; attempt < 5; attempt++)
            {
                await Task.Delay(2000);
                publicUrl = await GetPublicUrlAsync();
                if (!string.IsNullOrEmpty(publicUrl))
                    break;
                _logger.LogDebug("Ngrok URL not available yet, retrying ({Attempt}/5)...", attempt + 1);
            }
            if (string.IsNullOrEmpty(publicUrl))
            {
                _logger.LogError("Failed to retrieve ngrok public URL");
                await StopTunnelAsync();
                return false;
            }

            _currentPublicUrl = publicUrl;
            _isConnected = true;

            _logger.LogInformation("Ngrok tunnel started successfully: {PublicUrl}", publicUrl);

            // Start monitoring for URL changes
            StartMonitoring();

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start ngrok tunnel");
            return false;
        }
    }

    public async Task StopTunnelAsync()
    {
        // Gate on the PROCESS, not on _isConnected: the failed-start path calls this
        // while _isConnected is still false, and returning early there would leak the
        // ngrok process (zero-orphans rule). An orphaned ngrok keeps port 4040 warm,
        // and the next StartTunnelAsync would silently adopt its stale tunnel URL.
        if (_ngrokProcess == null)
        {
            _logger.LogWarning("Ngrok tunnel is not running");
            return;
        }

        try
        {
            _logger.LogInformation("Stopping ngrok tunnel...");

            // Stop monitoring
            _monitorTimer?.Dispose();
            _monitorTimer = null;

            // Kill via the tracked ProcessManager (entireProcessTree) so the ngrok
            // agent and helpers die together and the tracking entry is removed. It
            // disposes the Process object, so no local Dispose is needed.
            if (!HasNgrokProcessExited(_ngrokProcess))
            {
                await _processManager.KillProcessAsync(_ngrokProcess.Id);
            }

            _ngrokProcess = null;
            _currentPublicUrl = null;
            _isConnected = false;
            _consecutiveHealthFailures = 0;

            _logger.LogInformation("Ngrok tunnel stopped successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping ngrok tunnel");
        }
    }

    public async Task<string?> GetPublicUrlAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync(_ngrokApiUrl);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            var tunnels = json.GetProperty("tunnels");

            foreach (var tunnel in tunnels.EnumerateArray())
            {
                if (tunnel.TryGetProperty("proto", out var proto) && proto.GetString() == "https")
                {
                    if (tunnel.TryGetProperty("public_url", out var publicUrl))
                    {
                        return publicUrl.GetString();
                    }
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to retrieve ngrok public URL");
            return null;
        }
    }

    private void StartMonitoring()
    {
        // Replace any previous timer so a restart can never run two monitors
        _monitorTimer?.Dispose();
        _consecutiveHealthFailures = 0;

        // Check for URL changes every 30 seconds
        _monitorTimer = new Timer(async _ => await CheckForUrlChangeAsync(), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        _logger.LogInformation("Started monitoring ngrok tunnel for URL changes");
    }

    private async Task CheckForUrlChangeAsync()
    {
        // Reentrancy guard: skip if the previous poll is still in flight
        if (Interlocked.CompareExchange(ref _healthCheckRunning, 1, 0) != 0)
        {
            return;
        }

        try
        {
            // If the ngrok process itself died there is nothing left to poll: stop
            // the monitor and report the tunnel as disconnected.
            if (HasNgrokProcessExited(_ngrokProcess))
            {
                _logger.LogError("Ngrok process has exited - tunnel is down");
                _monitorTimer?.Dispose();
                _monitorTimer = null;
                if (_isConnected)
                {
                    _isConnected = false;
                    _currentPublicUrl = null;
                    PublicUrlChanged?.Invoke(this, string.Empty);
                }
                return;
            }

            var publicUrl = await GetPublicUrlAsync();

            if (string.IsNullOrEmpty(publicUrl))
        {
                // Tunnel might be down - require consecutive failures before declaring
                // a disconnect: the local 4040 API can transiently hiccup while the
                // tunnel is fine, and a false disconnect cascades into an unnecessary
                // Kick unsubscribe/resubscribe cycle in the webhook coordinator.
                var failures = Interlocked.Increment(ref _consecutiveHealthFailures);
                if (_isConnected && failures >= 3)
                {
                    _logger.LogWarning("Ngrok tunnel appears to be disconnected ({Failures} consecutive failed checks)", failures);
                    _isConnected = false;
                    _currentPublicUrl = null;
                    PublicUrlChanged?.Invoke(this, string.Empty);
                }
                return;
            }

            Interlocked.Exchange(ref _consecutiveHealthFailures, 0);

            // Check if URL changed
            if (_currentPublicUrl != publicUrl)
            {
                _logger.LogInformation("Ngrok public URL changed from {OldUrl} to {NewUrl}", _currentPublicUrl ?? "none", publicUrl);
                _currentPublicUrl = publicUrl;
                _isConnected = true;

                // Notify listeners about URL change
                PublicUrlChanged?.Invoke(this, publicUrl);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking for ngrok URL changes");
        }
        finally
        {
            Volatile.Write(ref _healthCheckRunning, 0);
        }
    }

    /// <summary>
    /// True when the tracked ngrok process is gone. Reading Process state after the
    /// ProcessManager disposed it (it auto-disposes exited processes) throws, so any
    /// failure reading state counts as exited.
    /// </summary>
    private static bool HasNgrokProcessExited(Process? process)
    {
        if (process == null)
        {
            return true;
        }

        try
        {
            return process.HasExited;
        }
        catch
        {
            return true;
        }
    }

    private static bool IsNgrokInstalled()
    {
        try
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = "ngrok",
                Arguments = "version",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true
            });

            process?.WaitForExit(5000);
            return process?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Save auth token to JSON file in profile folder.
    /// </summary>
    public async Task SaveAuthTokenAsync(string authToken)
    {
        try
        {
            var profile = await _profileService.GetCurrentProfileAsync();
            var credentialsFolder = _profileService.GetProfileCredentialsPath(profile.Name);
            var credentialsPath = Path.Combine(credentialsFolder, "ngrok_credentials.json");

            Directory.CreateDirectory(Path.GetDirectoryName(credentialsPath)!);

            var credentials = new { AuthToken = authToken };
            var json = JsonSerializer.Serialize(credentials, new JsonSerializerOptions { WriteIndented = true });

            await File.WriteAllTextAsync(credentialsPath, json);
            // Credentials file locations are never logged (secrets rule); log the outcome only.
            _logger.LogInformation("Ngrok auth token saved");

            // Update current instance
            AuthToken = authToken;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save ngrok auth token to file");
            throw;
        }
    }

    /// <summary>
    /// Load auth token from JSON file in profile folder.
    /// </summary>
    public async Task LoadAuthTokenAsync()
    {
        try
        {
            var profile = await _profileService.GetCurrentProfileAsync();
            var credentialsFolder = _profileService.GetProfileCredentialsPath(profile.Name);
            var credentialsPath = Path.Combine(credentialsFolder, "ngrok_credentials.json");

            // Credentials file locations are never logged (secrets rule).
            _logger.LogInformation("Looking for ngrok credentials file");

            if (!File.Exists(credentialsPath))
            {
                _logger.LogWarning("Ngrok credentials file not found");
                return;
            }

            var json = await File.ReadAllTextAsync(credentialsPath);

            var credentials = JsonSerializer.Deserialize<JsonElement>(json);

            AuthToken = credentials.GetProperty("AuthToken").GetString() ?? string.Empty;

            _logger.LogInformation("Ngrok auth token loaded successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load ngrok auth token from file");
        }
    }

    public void Dispose()
    {
        _monitorTimer?.Dispose();
        StopTunnelAsync().GetAwaiter().GetResult();
        _httpClient.Dispose();
        GC.SuppressFinalize(this);
    }
}
