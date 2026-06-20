using System;
using System.Configuration;

namespace Awagaman_ERP.Data
{
    public static class BackendSettings
    {
        public static bool UseRemoteApi
        {
            get
            {
                bool value;
                return bool.TryParse(ConfigurationManager.AppSettings["UseRemoteApi"], out value) && value;
            }
        }

        public static string ApiBaseUrl =>
            (ConfigurationManager.AppSettings["ApiBaseUrl"] ?? "http://localhost:5088").TrimEnd('/');
    }
}
