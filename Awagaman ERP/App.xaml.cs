using System;
using System.Collections.Generic;
using System.Configuration;
using System.Reflection;
using System.Net.Http;
using System.Net;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using System.Windows;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;
using Awagaman_ERP.Data;

namespace Awagaman_ERP
{
    public partial class App : Application
    {
        private const string SingleInstanceMutexName = "AwagamanERP.SingleInstance";
        private const string SingleInstanceRestoreEventName = "AwagamanERP.SingleInstance.Restore";
        private static Process _localApiProcess;
        private static Mutex _singleInstanceMutex;
        private static bool _ownsSingleInstanceMutex;
        private static EventWaitHandle _restoreRequestEvent;
        private static Thread _restoreRequestThread;
        private Forms.NotifyIcon _trayIcon;
        private bool _allowRealShutdown;
        private bool _trayHintShown;
        private WindowState _restoreWindowState = WindowState.Maximized;

        protected override void OnStartup(StartupEventArgs e)
        {
            AppLogger.LogMessage("Startup", "OnStartup begin");
            if (!AcquireSingleInstance())
            {
                AppLogger.LogMessage("Startup", "Another instance is already running");
                Shutdown();
                return;
            }

            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            base.OnStartup(e);
            AppLogger.LogMessage("Startup", "Base OnStartup complete");
            TryRegisterSyncfusionLicense();
            AppLogger.LogMessage("Startup", "Syncfusion license step complete");
            // Remote/shared mode should not spawn a local API process during desktop startup.
            // The API can be hosted separately on the server machine.
            if (!BackendSettings.UseRemoteApi)
            {
                AppLogger.LogMessage("Startup", "Starting local API server");
                TryStartLocalApiServer();
            }
            AppLogger.LogMessage("Startup", "Checking updates");
            _ = CheckForUpdatesAsync(showUpToDateMessage: false);

            if (BackendSettings.UseRemoteApi)
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                AppLogger.LogMessage("Startup", "Remote mode login window opening");
                var login = new LoginWindow();
                var loginOk = login.ShowDialog();
                AppLogger.LogMessage("Startup", $"Remote mode login result: {loginOk}");
                if (loginOk != true)
                {
                    AppLogger.LogMessage("Startup", "Login cancelled or failed, shutting down");
                    Shutdown();
                    return;
                }
            }
            else
            {
                AuthSession.Clear();
            }

            try
            {
                AppLogger.LogMessage("Startup", "Creating MainWindow");
                MainWindow = new MainWindow();
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                InitializeTrayIcon();
                AttachMainWindowHooks(MainWindow);
                MainWindow.Show();
                RestoreMainWindow();
                AppLogger.LogMessage("Startup", "MainWindow shown");
            }
            catch (Exception ex)
            {
                AppLogger.LogException("Startup MainWindow", ex);
                MessageBox.Show(
                    ex.Message,
                    "Startup Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Shutdown();
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            DisposeTrayIcon();
            TryStopLocalApiServer();
            ReleaseSingleInstance();
            base.OnExit(e);
        }

        internal void RequestExit()
        {
            _allowRealShutdown = true;
            try
            {
                if (MainWindow != null)
                {
                    MainWindow.Close();
                }
                else
                {
                    Shutdown();
                }
            }
            catch
            {
                Shutdown();
            }
        }

        private static bool AcquireSingleInstance()
        {
            try
            {
                _singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out _ownsSingleInstanceMutex);
                if (_ownsSingleInstanceMutex)
                {
                    StartRestoreRequestListener();
                    return true;
                }

                SignalExistingInstanceRestore();
                BringExistingInstanceToFront();
                return false;
            }
            catch
            {
                return true;
            }
        }

        private static void ReleaseSingleInstance()
        {
            try
            {
                if (_ownsSingleInstanceMutex && _singleInstanceMutex != null)
                {
                    _singleInstanceMutex.ReleaseMutex();
                }
            }
            catch
            {
            }
            finally
            {
                try
                {
                    _restoreRequestEvent?.Dispose();
                }
                catch
                {
                }

                try
                {
                    _singleInstanceMutex?.Dispose();
                }
                catch
                {
                }

                _restoreRequestEvent = null;
                _singleInstanceMutex = null;
                _ownsSingleInstanceMutex = false;
            }
        }

        private static void StartRestoreRequestListener()
        {
            try
            {
                if (_restoreRequestEvent == null)
                {
                    _restoreRequestEvent = new EventWaitHandle(false, EventResetMode.AutoReset, SingleInstanceRestoreEventName);
                }

                if (_restoreRequestThread != null && _restoreRequestThread.IsAlive)
                {
                    return;
                }

                _restoreRequestThread = new Thread(() =>
                {
                    while (_ownsSingleInstanceMutex && _restoreRequestEvent != null)
                    {
                        try
                        {
                            _restoreRequestEvent.WaitOne();
                            Current?.Dispatcher.BeginInvoke(new Action(() =>
                            {
                                try
                                {
                                    var app = Current as App;
                                    app?.RestoreMainWindow();
                                }
                                catch
                                {
                                }
                            }));
                        }
                        catch
                        {
                            break;
                        }
                    }
                })
                {
                    IsBackground = true,
                    Name = "AwagamanERP-RestoreListener"
                };
                _restoreRequestThread.Start();
            }
            catch
            {
            }
        }

        private static void SignalExistingInstanceRestore()
        {
            try
            {
                using (var restoreEvent = EventWaitHandle.OpenExisting(SingleInstanceRestoreEventName))
                {
                    restoreEvent.Set();
                }
            }
            catch
            {
            }
        }

        private static void BringExistingInstanceToFront()
        {
            try
            {
                var current = Process.GetCurrentProcess();
                var existing = Process.GetProcessesByName(current.ProcessName)
                    .FirstOrDefault(process => process.Id != current.Id);

                if (existing == null)
                {
                    return;
                }

                existing.Refresh();
                var handle = existing.MainWindowHandle;
                if (handle == IntPtr.Zero)
                {
                    return;
                }

                NativeMethods.ShowWindowAsync(handle, NativeMethods.SW_RESTORE);
                NativeMethods.ShowWindowAsync(handle, NativeMethods.SW_SHOW);
                NativeMethods.SetForegroundWindow(handle);
            }
            catch
            {
            }
        }

        private void InitializeTrayIcon()
        {
            try
            {
                if (_trayIcon != null)
                {
                    return;
                }

                var icon = LoadTrayIcon();
                if (icon == null)
                {
                    return;
                }

                var menu = new Forms.ContextMenuStrip();
                menu.Items.Add("Open", null, (_, __) => RestoreMainWindow());
                menu.Items.Add("Exit", null, (_, __) => RequestExit());

                _trayIcon = new Forms.NotifyIcon
                {
                    Icon = icon,
                    Text = "Awagaman ERP",
                    Visible = true,
                    ContextMenuStrip = menu
                };

                _trayIcon.DoubleClick += (_, __) => RestoreMainWindow();
            }
            catch (Exception ex)
            {
                AppLogger.LogException("InitializeTrayIcon", ex);
            }
        }

        private Drawing.Icon LoadTrayIcon()
        {
            try
            {
                var iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logo.ico");
                if (!File.Exists(iconPath))
                {
                    iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Awagaman ERP.exe");
                }

                if (!File.Exists(iconPath))
                {
                    return null;
                }

                return new Drawing.Icon(iconPath);
            }
            catch
            {
                return null;
            }
        }

        private void DisposeTrayIcon()
        {
            try
            {
                if (_trayIcon == null)
                {
                    return;
                }

                _trayIcon.Visible = false;
                _trayIcon.Dispose();
            }
            catch
            {
            }
            finally
            {
                _trayIcon = null;
            }
        }

        private void AttachMainWindowHooks(Window window)
        {
            if (window == null)
            {
                return;
            }

            window.Closing += MainWindow_Closing;
        }

        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_allowRealShutdown)
            {
                return;
            }

            e.Cancel = true;
            HideMainWindowToTray();
        }

        private bool HasVisibleOwnedWindows()
        {
            try
            {
                if (MainWindow == null)
                {
                    return false;
                }

                foreach (Window window in Current.Windows)
                {
                    if (window == null || ReferenceEquals(window, MainWindow))
                    {
                        continue;
                    }

                    if (ReferenceEquals(window.Owner, MainWindow) && window.IsVisible)
                    {
                        return true;
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        private void HideMainWindowToTray()
        {
            try
            {
                if (MainWindow == null)
                {
                    return;
                }

                if (MainWindow.WindowState != WindowState.Minimized)
                {
                    _restoreWindowState = MainWindow.WindowState;
                }

                if (HasVisibleOwnedWindows())
                {
                    MainWindow.ShowInTaskbar = true;
                    MainWindow.WindowState = WindowState.Minimized;
                    return;
                }

                MainWindow.ShowInTaskbar = false;
                MainWindow.Hide();
                if (_trayIcon != null && !_trayHintShown)
                {
                    _trayIcon.ShowBalloonTip(2000, "Awagaman ERP", "App is still running in the tray.", Forms.ToolTipIcon.Info);
                    _trayHintShown = true;
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogException("HideMainWindowToTray", ex);
            }
        }

        private void RestoreMainWindow()
        {
            try
            {
                if (MainWindow == null)
                {
                    return;
                }

                if (!MainWindow.IsVisible)
                {
                    MainWindow.Show();
                }

                MainWindow.ShowInTaskbar = true;
                MainWindow.WindowState = _restoreWindowState == WindowState.Minimized
                    ? WindowState.Normal
                    : _restoreWindowState;
                MainWindow.Activate();
                MainWindow.Focus();
            }
            catch (Exception ex)
            {
                AppLogger.LogException("RestoreMainWindow", ex);
            }
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
                    $"A newer version is available.\n\nCurrent: {current}\nLatest: {latest.Version}\n\nDo you want to update now?\nThe app will close, update files in-place, and open again. Your data/settings will be retained.",
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
                    var app = Application.Current as App;
                    if (app != null)
                    {
                        app.RequestExit();
                        return;
                    }

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
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("AwagamanERP-Updater");
                client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
                var json = await client.GetStringAsync(apiUrl).ConfigureAwait(false);
                var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                var payload = serializer.DeserializeObject(json) as Dictionary<string, object>;
                if (payload == null) return null;

                object rawTag;
                if (!payload.TryGetValue("tag_name", out rawTag)) return null;

                var version = ParseVersion(Convert.ToString(rawTag));
                if (version == null) return null;

                string downloadUrl = null;
                string assetName = null;
                string setupDownloadUrl = null;
                string setupAssetName = null;

                object assetsObject;
                if (payload.TryGetValue("assets", out assetsObject))
                {
                    var assets = assetsObject as object[];
                    if (assets != null)
                    {
                        foreach (var assetObject in assets)
                        {
                            var asset = assetObject as Dictionary<string, object>;
                            if (asset == null) continue;

                            var name = Convert.ToString(asset.ContainsKey("name") ? asset["name"] : null);
                            var url = Convert.ToString(asset.ContainsKey("browser_download_url") ? asset["browser_download_url"] : null);
                            if (string.IsNullOrWhiteSpace(name) ||
                                string.IsNullOrWhiteSpace(url) ||
                                !(name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase)))
                            {
                                continue;
                            }

                            if (name.IndexOf("update", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                downloadUrl = url;
                                assetName = name;
                                break;
                            }

                            if (name.IndexOf("setup", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                setupDownloadUrl = url;
                                setupAssetName = name;
                            }
                            else if (string.IsNullOrWhiteSpace(setupDownloadUrl))
                            {
                                setupDownloadUrl = url;
                                setupAssetName = name;
                            }
                        }
                    }
                }

                if (string.IsNullOrWhiteSpace(downloadUrl))
                {
                    downloadUrl = setupDownloadUrl;
                    assetName = setupAssetName;
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

        private static class NativeMethods
        {
            internal const int SW_RESTORE = 9;
            internal const int SW_SHOW = 5;

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool SetForegroundWindow(IntPtr hWnd);
        }
    }
}
