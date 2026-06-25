using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Principal;
using System.Windows.Forms;

internal static class InstallerWizard
{
    private const string PayloadResource = "payload.zip";
    private static readonly string AppName = "Awagaman ERP";
    private static readonly string DefaultInstallDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), AppName);

    [STAThread]
    private static void Main()
    {
        if (!IsAdministrator())
        {
            var exe = Process.GetCurrentProcess().MainModule.FileName;
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = exe,
                    UseShellExecute = true,
                    Verb = "runas"
                });
            }
            catch
            {
            }
            return;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new WizardForm());
    }

    private static bool IsAdministrator()
    {
        using (var identity = WindowsIdentity.GetCurrent())
        {
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    private sealed class WizardForm : Form
    {
        private readonly Panel[] _pages;
        private readonly Button _backButton;
        private readonly Button _nextButton;
        private readonly Button _cancelButton;
        private readonly TextBox _installPathBox;
        private readonly CheckBox _launchAfterInstall;
        private readonly ProgressBar _progress;
        private readonly Label _status;
        private readonly Label _finishLabel;
        private int _pageIndex;
        private string _tempDir;

        public WizardForm()
        {
            Text = AppName + " Setup";
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(640, 420);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Font = new Font("Segoe UI", 9F);
            Icon = SystemIcons.Application;

            var header = new Panel { Dock = DockStyle.Top, Height = 66, BackColor = Color.FromArgb(31, 78, 121) };
            header.Controls.Add(new Label { Text = AppName, ForeColor = Color.White, Font = new Font("Segoe UI", 15F, FontStyle.Bold), AutoSize = true, Location = new Point(18, 14) });
            header.Controls.Add(new Label { Text = "Setup wizard for desktop installation", ForeColor = Color.WhiteSmoke, AutoSize = true, Location = new Point(20, 40) });
            Controls.Add(header);

            var content = new Panel { Dock = DockStyle.Fill, Padding = new Padding(18, 16, 18, 16) };
            Controls.Add(content);

            var welcomePage = new Panel { Dock = DockStyle.Fill };
            welcomePage.Controls.Add(new Label
            {
                Dock = DockStyle.Top,
                Height = 110,
                Text = "This wizard will install or update Awagaman ERP on this PC.\n\nThe desktop app is cloud-first in this build and uses the VPS data source by default.",
                Font = new Font("Segoe UI", 10F)
            });

            var pathPage = new Panel { Dock = DockStyle.Fill };
            pathPage.Controls.Add(new Label { Text = "Choose install location:", AutoSize = true, Location = new Point(0, 0) });
            _installPathBox = new TextBox { Location = new Point(0, 24), Width = 470, Text = DefaultInstallDir };
            var browse = new Button { Text = "Browse...", Location = new Point(480, 22), Width = 90 };
            browse.Click += (s, e) =>
            {
                using (var dlg = new FolderBrowserDialog())
                {
                    dlg.SelectedPath = _installPathBox.Text;
                    dlg.Description = "Choose where Awagaman ERP should be installed";
                    if (dlg.ShowDialog(this) == DialogResult.OK)
                    {
                        _installPathBox.Text = dlg.SelectedPath;
                    }
                }
            };
            pathPage.Controls.Add(_installPathBox);
            pathPage.Controls.Add(browse);
            pathPage.Controls.Add(new Label
            {
                Text = "The installer will copy the app and API files to this folder and create a shortcut on the desktop.",
                AutoSize = false,
                Width = 560,
                Height = 60,
                Location = new Point(0, 62)
            });

            var installPage = new Panel { Dock = DockStyle.Fill };
            installPage.Controls.Add(new Label { Text = "Installing...", AutoSize = true, Location = new Point(0, 0), Font = new Font("Segoe UI", 10F, FontStyle.Bold) });
            _progress = new ProgressBar { Location = new Point(0, 36), Width = 560, Height = 20, Minimum = 0, Maximum = 100 };
            _status = new Label { Location = new Point(0, 68), Width = 560, Height = 60, Text = "Ready to install." };
            installPage.Controls.Add(_progress);
            installPage.Controls.Add(_status);

            var finishPage = new Panel { Dock = DockStyle.Fill };
            _finishLabel = new Label { Dock = DockStyle.Top, Height = 120, Text = "Installation complete.", Font = new Font("Segoe UI", 10F) };
            _launchAfterInstall = new CheckBox { Text = "Launch Awagaman ERP now", Checked = true, AutoSize = true, Location = new Point(0, 126) };
            finishPage.Controls.Add(_finishLabel);
            finishPage.Controls.Add(_launchAfterInstall);

            _pages = new[] { welcomePage, pathPage, installPage, finishPage };
            foreach (var page in _pages)
            {
                page.Visible = false;
                page.Dock = DockStyle.Fill;
                content.Controls.Add(page);
            }
            _pages[0].Visible = true;

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 56, Padding = new Padding(12, 10, 12, 10) };
            _backButton = new Button { Text = "< Back", Width = 86, Left = 352, Top = 10, Enabled = false };
            _nextButton = new Button { Text = "Next >", Width = 86, Left = 444, Top = 10 };
            _cancelButton = new Button { Text = "Cancel", Width = 86, Left = 536, Top = 10 };
            _backButton.Click += (s, e) => MovePage(-1);
            _nextButton.Click += async (s, e) => await NextClickedAsync();
            _cancelButton.Click += (s, e) => Close();
            bottom.Controls.Add(_backButton);
            bottom.Controls.Add(_nextButton);
            bottom.Controls.Add(_cancelButton);
            Controls.Add(bottom);
        }

        private void MovePage(int delta)
        {
            var newIndex = _pageIndex + delta;
            if (newIndex < 0 || newIndex >= _pages.Length) return;
            _pages[_pageIndex].Visible = false;
            _pageIndex = newIndex;
            _pages[_pageIndex].Visible = true;
            _backButton.Enabled = _pageIndex > 0;
            _nextButton.Text = _pageIndex == _pages.Length - 1 ? "Finish" : (_pageIndex == 2 ? "Install" : "Next >");
        }

        private async System.Threading.Tasks.Task NextClickedAsync()
        {
            if (_pageIndex == 0)
            {
                MovePage(1);
                return;
            }

            if (_pageIndex == 1)
            {
                if (string.IsNullOrWhiteSpace(_installPathBox.Text))
                {
                    MessageBox.Show(this, "Please choose an install location.", AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                MovePage(1);
                await InstallAsync();
                _nextButton.Text = "Finish";
                return;
            }

            if (_pageIndex == 2)
            {
                MovePage(1);
                return;
            }

            if (_pageIndex == 3)
            {
                if (_launchAfterInstall.Checked)
                {
                    LaunchInstalledApp();
                }
                Close();
            }
        }

        private async System.Threading.Tasks.Task InstallAsync()
        {
            try
            {
                _backButton.Enabled = false;
                _nextButton.Enabled = false;
                _cancelButton.Enabled = false;
                _status.Text = "Preparing files...";
                _progress.Value = 5;

                _tempDir = Path.Combine(Path.GetTempPath(), "AwagamanERP_Install_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(_tempDir);
                var payloadZip = Path.Combine(_tempDir, "payload.zip");
                ExtractResourceToFile(PayloadResource, payloadZip);
                _status.Text = "Extracting package...";
                _progress.Value = 15;

                var extractRoot = Path.Combine(_tempDir, "extract");
                Directory.CreateDirectory(extractRoot);
                ZipFile.ExtractToDirectory(payloadZip, extractRoot);
                _progress.Value = 45;

                var appSource = ResolvePayloadFolder(extractRoot, "app");
                var apiSource = ResolvePayloadFolder(extractRoot, "api");
                var installLocation = _installPathBox.Text.Trim();
                var apiTarget = Path.Combine(installLocation, "ApiServer");
                Directory.CreateDirectory(installLocation);
                Directory.CreateDirectory(apiTarget);
                _status.Text = "Copying app files...";
                CopyDirectory(appSource, installLocation);
                _progress.Value = 70;
                _status.Text = "Copying API files...";
                CopyDirectory(apiSource, apiTarget);
                _progress.Value = 90;

                _status.Text = "Writing cloud settings...";
                WriteNetworkSettings();

                _status.Text = "Creating desktop shortcut...";
                CreateDesktopShortcut(Path.Combine(installLocation, "Awagaman ERP.exe"));
                _progress.Value = 100;

                _finishLabel.Text = "Installation complete.\n\nThe app is now configured for cloud data and ready to use.";
                _pages[2].Visible = false;
                _pages[3].Visible = true;
                _pageIndex = 3;
                _backButton.Visible = false;
                _nextButton.Text = "Finish";
                _nextButton.Enabled = true;
                _cancelButton.Text = "Close";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                _nextButton.Enabled = true;
                _cancelButton.Enabled = true;
            }
        }

        private void LaunchInstalledApp()
        {
            var installLocation = _installPathBox.Text.Trim();
            var exePath = Path.Combine(installLocation, "Awagaman ERP.exe");
            if (File.Exists(exePath))
            {
                Process.Start(new ProcessStartInfo { FileName = exePath, WorkingDirectory = installLocation, UseShellExecute = true });
            }
        }

        private void ExtractResourceToFile(string resourceName, string outputPath)
        {
            var assembly = Assembly.GetExecutingAssembly();
            var fullName = assembly.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(resourceName, StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(fullName))
            {
                throw new InvalidOperationException("Missing embedded resource: " + resourceName);
            }

            using (var input = assembly.GetManifestResourceStream(fullName))
            using (var output = File.Create(outputPath))
            {
                if (input == null) throw new InvalidOperationException("Unable to open installer payload.");
                input.CopyTo(output);
            }
        }

        private static void CopyDirectory(string sourceDir, string targetDir)
        {
            foreach (var dir in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
            {
                Directory.CreateDirectory(dir.Replace(sourceDir, targetDir));
            }

            foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                File.Copy(file, file.Replace(sourceDir, targetDir), true);
            }
        }

        private static string ResolvePayloadFolder(string root, string expectedName)
        {
            var named = Path.Combine(root, expectedName);
            if (Directory.Exists(named))
            {
                return named;
            }

            var fallback = Directory.GetDirectories(root, expectedName + "*", SearchOption.TopDirectoryOnly).FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(fallback))
            {
                return fallback;
            }

            return root;
        }

        private void CreateDesktopShortcut(string exePath)
        {
            try
            {
                var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                var shortcut = Path.Combine(desktop, AppName + ".lnk");
                var shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) return;
                dynamic shell = Activator.CreateInstance(shellType);
                dynamic link = shell.CreateShortcut(shortcut);
                link.TargetPath = exePath;
                link.WorkingDirectory = Path.GetDirectoryName(exePath);
                link.Description = AppName;
                link.IconLocation = exePath + ",0";
                link.Save();
            }
            catch
            {
            }
        }

        private void WriteNetworkSettings()
        {
            var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            var configDir = Path.Combine(programData, AppName);
            Directory.CreateDirectory(configDir);
            var settingsPath = Path.Combine(configDir, "network.settings.json");
            var json =
                "{\r\n" +
                "  \"UseRemoteApi\": true,\r\n" +
                "  \"ApiBaseUrl\": \"http://187.127.153.124:5088\",\r\n" +
                "  \"RunLocalApiServer\": false,\r\n" +
                "  \"LocalApiExecutablePath\": \"ApiServer\\\\Awagaman.Api.exe\"\r\n" +
                "}\r\n";
            File.WriteAllText(settingsPath, json);
        }
    }
}

