using System;
using System.Collections.Generic;
using System.Configuration;
using System.Reflection;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Diagnostics;

namespace Awagaman_ERP
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            TryRegisterSyncfusionLicense();
            _ = CheckForUpdatesAsync(showUpToDateMessage: false);
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
                    $"A newer version is available.\n\nCurrent: {current}\nLatest: {latest.Version}\n\nDo you want to download and install the update now?",
                    "Update Available",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (result != MessageBoxResult.Yes) return;

                Process.Start(new ProcessStartInfo
                {
                    FileName = latest.DownloadUrl,
                    UseShellExecute = true
                });
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
        }

        private static async Task<ReleaseInfo> GetLatestReleaseAsync()
        {
            const string apiUrl = "https://api.github.com/repos/saguamit/awagamanerp/releases/latest";
            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("AwagamanERP-Updater");
                var json = await client.GetStringAsync(apiUrl).ConfigureAwait(false);
                var serializer = new JavaScriptSerializer();
                var root = serializer.Deserialize<Dictionary<string, object>>(json);
                if (root == null) return null;

                var tag = root.ContainsKey("tag_name") ? Convert.ToString(root["tag_name"]) : string.Empty;
                var version = ParseVersion(tag);
                if (version == null) return null;

                string downloadUrl = null;
                if (root.ContainsKey("assets"))
                {
                    var assets = root["assets"] as System.Collections.IEnumerable;
                    if (assets != null)
                    {
                        foreach (var assetObj in assets)
                        {
                            var asset = assetObj as Dictionary<string, object>;
                            if (asset == null) continue;
                            var name = asset.ContainsKey("name") ? Convert.ToString(asset["name"]) : string.Empty;
                            var url = asset.ContainsKey("browser_download_url") ? Convert.ToString(asset["browser_download_url"]) : string.Empty;
                            if (string.Equals(name, "AwagamanERP-Setup.exe", StringComparison.OrdinalIgnoreCase))
                            {
                                downloadUrl = url;
                                break;
                            }
                        }
                    }
                }

                if (string.IsNullOrWhiteSpace(downloadUrl) && root.ContainsKey("html_url"))
                    downloadUrl = Convert.ToString(root["html_url"]);

                return new ReleaseInfo { Version = version, DownloadUrl = downloadUrl };
            }
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
