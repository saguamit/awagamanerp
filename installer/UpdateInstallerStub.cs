using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Principal;
using System.Windows.Forms;

namespace AwagamanERPInstaller
{
    internal static class Program
    {
        [STAThread]
        private static int Main()
        {
            try
            {
                if (!IsAdministrator())
                {
                    RelaunchElevated();
                    return 0;
                }

                var extractRoot = Path.Combine(Path.GetTempPath(), "AwagamanERP_Update_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(extractRoot);
                ExtractEmbeddedPayload(extractRoot);
                InstallPayload(extractRoot);
                LaunchInstalledApp();
                return 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Awagaman ERP Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        private static bool IsAdministrator()
        {
            var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        private static void RelaunchElevated()
        {
            var psi = new ProcessStartInfo
            {
                FileName = Assembly.GetExecutingAssembly().Location,
                UseShellExecute = true,
                Verb = "runas"
            };
            Process.Start(psi);
        }

        private static void ExtractEmbeddedPayload(string extractRoot)
        {
            var resourceName = Assembly.GetExecutingAssembly()
                .GetManifestResourceNames()
                .FirstOrDefault(name => name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));

            if (string.IsNullOrWhiteSpace(resourceName))
            {
                throw new InvalidOperationException("Installer payload is missing.");
            }

            var resourceStream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
            if (resourceStream == null)
            {
                throw new InvalidOperationException("Installer payload could not be loaded.");
            }

            var payloadZip = Path.Combine(extractRoot, "payload.zip");
            using (resourceStream)
            using (var fileStream = File.Create(payloadZip))
            {
                resourceStream.CopyTo(fileStream);
            }

            ZipFile.ExtractToDirectory(payloadZip, extractRoot);
        }

        private static void InstallPayload(string extractRoot)
        {
            var installLocation = FindInstallLocation();
            if (string.IsNullOrWhiteSpace(installLocation))
            {
                installLocation = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Awagaman ERP");
            }

            var appSource = Path.Combine(extractRoot, "app");
            var apiSource = Path.Combine(extractRoot, "api");
            var appTarget = installLocation;
            var apiTarget = Path.Combine(installLocation, "ApiServer");

            Directory.CreateDirectory(appTarget);
            Directory.CreateDirectory(apiTarget);

            CopyDirectory(appSource, appTarget);
            CopyDirectory(apiSource, apiTarget);
        }

        private static string FindInstallLocation()
        {
            var uninstallRoots = new[]
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            };

            foreach (var root in uninstallRoots)
            {
                using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(root))
                {
                    if (key == null) continue;
                    foreach (var subName in key.GetSubKeyNames())
                    {
                        using (var subKey = key.OpenSubKey(subName))
                        {
                            var displayName = Convert.ToString(subKey?.GetValue("DisplayName"));
                            if (!string.Equals(displayName, "Awagaman ERP", StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            var installLocation = Convert.ToString(subKey.GetValue("InstallLocation"));
                            if (!string.IsNullOrWhiteSpace(installLocation))
                            {
                                return installLocation;
                            }
                        }
                    }
                }
            }

            return null;
        }

        private static void CopyDirectory(string source, string target)
        {
            if (!Directory.Exists(source))
            {
                return;
            }

            foreach (var directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            {
                Directory.CreateDirectory(directory.Replace(source, target));
            }

            foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                var destination = file.Replace(source, target);
                Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? target);
                File.Copy(file, destination, true);
            }
        }

        private static void LaunchInstalledApp()
        {
            var installLocation = FindInstallLocation();
            if (string.IsNullOrWhiteSpace(installLocation))
            {
                installLocation = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Awagaman ERP");
            }

            var exe = Path.Combine(installLocation, "Awagaman ERP.exe");
            if (!File.Exists(exe))
            {
                throw new FileNotFoundException("Installed application could not be found.", exe);
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                WorkingDirectory = installLocation,
                UseShellExecute = true
            });
        }
    }
}
