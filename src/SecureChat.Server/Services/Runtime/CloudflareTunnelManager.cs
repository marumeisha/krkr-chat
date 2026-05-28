using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Options;

namespace SecureChat.Server.Services.Runtime;

public sealed class CloudflareTunnelManager : IDisposable
{
    private readonly CloudflareTunnelOptions _options;
    private readonly object _lock = new();
    private readonly ConcurrentQueue<string> _recentLogs = new();
    private Process? _managedProcess;

    public CloudflareTunnelManager(IOptions<CloudflareTunnelOptions> options)
    {
        _options = options.Value;
    }

    public CloudflareTunnelStatus GetStatus()
    {
        lock (_lock)
        {
            RefreshManagedProcessState();

            var executablePath = ResolveExecutablePath();
            var configPath = ResolveConfigPath();
            var isInstalled = !string.IsNullOrWhiteSpace(executablePath);
            var configExists = File.Exists(configPath);
            var configMatches = configExists && ConfigMatches(File.ReadAllText(configPath), _options.ExpectedHostname, _options.ExpectedServiceUrl);
            var isManagedRunning = _managedProcess is { HasExited: false };
            var anyCloudflaredRunning = isManagedRunning || Process.GetProcessesByName("cloudflared").Any();

            return new CloudflareTunnelStatus
            {
                IsInstalled = isInstalled,
                ConfigExists = configExists,
                ConfigMatches = configMatches,
                IsRunning = anyCloudflaredRunning,
                IsManagedByServer = isManagedRunning,
                TunnelName = _options.TunnelName,
                ExecutablePath = executablePath ?? _options.ExecutablePath,
                ConfigPath = configPath,
                Hostname = _options.ExpectedHostname,
                ServiceUrl = _options.ExpectedServiceUrl,
                Message = BuildStatusMessage(isInstalled, configExists, configMatches, isManagedRunning, anyCloudflaredRunning),
                RecentLogs = _recentLogs.ToArray()
            };
        }
    }

    public CloudflareTunnelStatus Start()
    {
        lock (_lock)
        {
            RefreshManagedProcessState();
            if (_managedProcess is { HasExited: false })
            {
                return GetStatus();
            }

            var executablePath = ResolveExecutablePath();
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                AppendLog("cloudflared.exe not found in PATH or configured executable path.");
                return GetStatus();
            }

            var configPath = ResolveConfigPath();
            if (!File.Exists(configPath))
            {
                AppendLog($"Tunnel config not found: {configPath}");
                return GetStatus();
            }

            var configText = File.ReadAllText(configPath);
            if (!ConfigMatches(configText, _options.ExpectedHostname, _options.ExpectedServiceUrl))
            {
                AppendLog($"Tunnel config must contain hostname '{_options.ExpectedHostname}' and service '{_options.ExpectedServiceUrl}'.");
                return GetStatus();
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = $"tunnel run {_options.TunnelName}",
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(executablePath) ?? Environment.CurrentDirectory
            };

            var process = new Process
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true
            };
            process.OutputDataReceived += (_, args) => OnProcessOutput(args.Data);
            process.ErrorDataReceived += (_, args) => OnProcessOutput(args.Data);
            process.Exited += (_, _) =>
            {
                lock (_lock)
                {
                    AppendLog($"cloudflared exited with code {process.ExitCode}.");
                    if (ReferenceEquals(_managedProcess, process))
                    {
                        _managedProcess = null;
                    }
                }

                process.Dispose();
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            _managedProcess = process;
            AppendLog($"cloudflared started for tunnel '{_options.TunnelName}'.");
            return GetStatus();
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_managedProcess is { HasExited: false })
            {
                _managedProcess.Kill(entireProcessTree: true);
                _managedProcess.Dispose();
                _managedProcess = null;
            }
        }
    }

    private void OnProcessOutput(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        lock (_lock)
        {
            AppendLog(line.Trim());
        }
    }

    private void AppendLog(string message)
    {
        _recentLogs.Enqueue($"[{DateTimeOffset.Now:HH:mm:ss}] {message}");
        while (_recentLogs.Count > 40 && _recentLogs.TryDequeue(out _))
        {
        }
    }

    private void RefreshManagedProcessState()
    {
        if (_managedProcess is { HasExited: true })
        {
            _managedProcess.Dispose();
            _managedProcess = null;
        }
    }

    private string ResolveConfigPath()
    {
        if (!string.IsNullOrWhiteSpace(_options.ConfigPath))
        {
            return _options.ConfigPath;
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cloudflared", "config.yml");
    }

    private string? ResolveExecutablePath()
    {
        var executableFromPath = FindExecutableInPath("cloudflared.exe");
        if (!string.IsNullOrWhiteSpace(executableFromPath))
        {
            return executableFromPath;
        }

        return File.Exists(_options.ExecutablePath) ? _options.ExecutablePath : null;
    }

    private static string? FindExecutableInPath(string fileName)
    {
        var pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var segment in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                var candidate = Path.Combine(segment, fileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch
            {
                // Ignore malformed PATH entries.
            }
        }

        return null;
    }

    private static bool ConfigMatches(string configText, string expectedHostname, string expectedServiceUrl)
    {
        return configText.Contains($"hostname: {expectedHostname}", StringComparison.OrdinalIgnoreCase)
               && configText.Contains($"service: {expectedServiceUrl}", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildStatusMessage(bool isInstalled, bool configExists, bool configMatches, bool isManagedRunning, bool isAnyRunning)
    {
        if (!isInstalled)
        {
            return "cloudflared 未安装或不在 PATH。";
        }

        if (!configExists)
        {
            return "未找到 Cloudflare Tunnel 配置文件。";
        }

        if (!configMatches)
        {
            return "Cloudflare Tunnel 配置存在，但目标主机名或服务地址不匹配。";
        }

        if (isManagedRunning)
        {
            return "Cloudflare Tunnel 已由当前服务托管运行。";
        }

        if (isAnyRunning)
        {
            return "检测到 cloudflared 正在运行，但不是当前服务启动的实例。";
        }

        return "Cloudflare Tunnel 已就绪，可以启动。";
    }
}
