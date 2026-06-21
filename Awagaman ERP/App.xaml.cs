using System;
using System.Collections.Generic;
using System.Configuration;
using System.Reflection;
using System.Net.Http;
using System.Net;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Awagaman_ERP.Data;

namespace Awagaman_ERP
{
    public partial class App : Application
    {
        private static Process _localApiProcess;

        protected override void OnStartup(StartupEventArgs e)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            base.OnStartup(e);
            TryRegisterSyncfusionLicense();
            // Remote/shared mode should not spawn a local API process during desktop startup.
            // The API can be hosted separately on the server machine.
            if (!BackendSettings.UseRemoteApi)
            {
                TryStartLocalApiServer();
            }
            _ = CheckForUpdatesAsync(showUpToDateMessage: false);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            TryStopLocalApiServer();
            base.OnExit(e);
        }

        private static void TryRegisterSyncfusionLicense()
        {
            try
            {
                var key = ConfigurationManager.AppSettings["SyncfusionLicenseKey"];
                if (string.IsNullOrWhiteSpace(key))
                {
                    return;
                }

                // Reflection keeps startup safe when Syncfusion packages are not installed yet.
                var providerType = Type.GetType("Syncfusion.Licensing.SyncfusionLicenseProvider, Syncfusion.Licensing");
                var registerMethod = providerType?.GetMethod("RegisterLicense", BindingFlags.Public | BindingFlags.Static);
                registerMethod?.Invoke(null, new object[] { key });
            }
            catch
            {
                // Non-fatal by design.
            }
        }

        private static void TryStartLocalApiServer()
        {
            try
            {
                if (!BackendSettings.RunLocalApiServer)
                {
                    return;
                }

                if (IsApiHealthyAsync(BackendSettings.ApiBaseUrl).GetAwaiter().GetResult())
                {
                    return;
                }

                var path = BackendSettings.LocalApiExecutablePath;
                if (string.IsNullOrWhiteSpace(path))
                {
                    return;
                }

                if (!Path.IsPathRooted(path))
                {
                    path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, path);
                }

                if (!File.Exists(path))
                {
                    return;
                }

                if (_localApiProcess != null && !_localApiProcess.HasExited)
                {
                    return;
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = path,
                    WorkingDirectory = Path.GetDirectoryName(path) ?? AppDomain.CurrentDomain.BaseDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                _localApiProcess = Process.Start(startInfo);
                _ = WaitForLocalApiReadyAsync();
            }
            catch
            {
                // Server mode should not block app startup if API cannot launch.
            }
        }

        private static void TryStopLocalApiServer()
        {
            try
            {
                if (_localApiProcess == null)
                {
                    return;
                }

                if (!_localApiProcess.HasExited)
                {
                    _localApiProcess.Kill();
                    _localApiProcess.WaitForExit(5000);
                }
            }
            catch
            {
            }
            finally
            {
                _localApiProcess = null;
            }
        }

        private static async Task WaitForLocalApiReadyAsync()
        {
            try
            {
                using (var client = new HttpClient())
                {
                    for (var i = 0; i < 30; i++)
                    {
                        try
                        {
                            if (await IsApiHealthyAsync(BackendSettings.ApiBaseUrl, client).ConfigureAwait(false))
                            {
                                return;
                            }
                        }
                        catch
                        {
                        }

                        await Task.Delay(500).ConfigureAwait(false);
                    }
                }
            }
            catch
            {
            }
        }

        private static async Task<bool> IsApiHealthyAsync(string baseUrl, HttpClient client = null)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                return false;
            }

            var healthUrl = baseUrl.TrimEnd('/') + "/api/health";
            var ownsClient = false;
            if (client == null)
            {
                client = new HttpClient();
                ownsClient = true;
            }

            try
            {
                var response = await client.GetAsync(healthUrl).ConfigureAwait(false);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (ownsClient)
                {
                    client.Dispose();
                }
            }
        }

        internal static Task CheckForUpdatesOnDemandAsync()
        {
            return CheckForUpdatesAsync(showUpToDateMessage: true);
        }

        private static async Task CheckForUpdatesAsync(bool showUpToDateMessage)
        {
            try
            {
                var current = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0, 0);
                var latest = await GetLatestReleaseAsync();
                if (latest == null || latest.Version == null || string.IsNullOrWhiteSpace(latest.DownloadUrl))
                {
                    if (showUpToDateMessage)
                    {
                        MessageBox.Show("Unable to check updates right now.", "Update Check", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    return;
                }
                if (latest.Version <= current)
                {
                    if (showUpToDateMessage)
                    {
                        MessageBox.Show($"You are on the latest version.\nCurrent: {current}", "No Update", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    return;
                }

                var result = MessageBox.Show(
                    $"A newer version is available.\n\nCurrent: {current}\nLatest: {latest.Version}\n\nDo you want to download and install the update now?\nThe app will close and the installer will run.",
                    "Update Available",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (result != MessageBoxResult.Yes) return;

                await DownloadAndRunInstallerAsync(latest).ConfigureAwait(false);
            }
            catch
            {
                if (showUpToDateMessage)
                {
                    MessageBox.Show("Unable to check updates right now.", "Update Check", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }

        private sealed class ReleaseInfo
        {
            public Version Version { get; set; }
            public string DownloadUrl { get; set; }
            public string AssetName { get; set; }
        }

        private static async Task DownloadAndRunInstallerAsync(ReleaseInfo latest)
        {
            if (latest == null || string.IsNullOrWhiteSpace(latest.DownloadUrl))
            {
                MessageBox.Show("Update package could not be found.", "Update Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var safeName = string.IsNullOrWhiteSpace(latest.AssetName) ? "AwagamanERP-Setup.exe" : latest.AssetName;
            var tempDir = Path.Combine(Path.GetTempPath(), "AwagamanERP-Updates");
            Directory.CreateDirectory(tempDir);
            var localPath = Path.Combine(tempDir, safeName);

            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("AwagamanERP-Updater");
                var bytes = await client.GetByteArrayAsync(latest.DownloadUrl).ConfigureAwait(false);
                File.WriteAllBytes(localPath, bytes);
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = localPath,
                UseShellExecute = true,
                WorkingDirectory = tempDir
            });

            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    Application.Current?.Shutdown();
                }
                catch
                {
                }
            }));
        }

        private static async Task<ReleaseInfo> GetLatestReleaseAsync()
        {
            const string apiUrl = "https://api.github.com/repos/saguamit/awagamanerp/releases/latest";
            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("AwagamanERP-Updater");
                client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
                var json = await client.GetStringAsync(apiUrl).ConfigureAwait(false);

                var tag = MatchValue(json, "\"tag_name\"\\s*:\\s*\"([^\"]+)\"");
                var version = ParseVersion(tag);
                if (version == null) return null;

                string downloadUrl = null;
                string assetName = null;

                var assetsBlock = MatchValue(json, "\"assets\"\\s*:\\s*\\[(.*)\\]\\s*,\\s*\"assets_url\"", RegexOptions.Singleline);
                if (!string.IsNullOrWhiteSpace(assetsBlock))
                {
                    var assetMatches = Regex.Matches(assetsBlock, "\\{(.*?)\\}", RegexOptions.Singleline);
                    foreach (Match assetMatch in assetMatches)
                    {
                        var assetJson = assetMatch.Value;
                        var name = MatchValue(assetJson, "\"name\"\\s*:\\s*\"([^\"]+)\"");
                        var url = MatchValue(assetJson, "\"browser_download_url\"\\s*:\\s*\"([^\"]+)\"");
                        if (!string.IsNullOrWhiteSpace(name) &&
                            !string.IsNullOrWhiteSpace(url) &&
                            (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase)) &&
                            (string.IsNullOrWhiteSpace(downloadUrl) || name.IndexOf("setup", StringComparison.OrdinalIgnoreCase) >= 0))
                        {
                            downloadUrl = url;
                            assetName = name;
                            if (name.IndexOf("setup", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                break;
                            }
                        }
                    }
                }

                return new ReleaseInfo { Version = version, DownloadUrl = downloadUrl, AssetName = assetName };
            }
        }

        private static string MatchValue(string input, string pattern, RegexOptions options = RegexOptions.None)
        {
            var match = Regex.Match(input ?? string.Empty, pattern, options);
            return match.Success ? match.Groups[1].Value : string.Empty;
        }

        private static Version ParseVersion(string tag)
        {
            var raw = (tag ?? string.Empty).Trim();
            if (raw.StartsWith("v", StringComparison.OrdinalIgnoreCase)) raw = raw.Substring(1);
            Version v;
            if (Version.TryParse(raw, out v)) return v;
            return null;
        }
    }
}
