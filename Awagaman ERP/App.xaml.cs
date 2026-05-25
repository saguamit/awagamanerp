using System;
using System.Configuration;
using System.Reflection;
using System.Windows;

namespace Awagaman_ERP
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            TryRegisterSyncfusionLicense();
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
    }
}
