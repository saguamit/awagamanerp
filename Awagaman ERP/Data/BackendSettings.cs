using System;
using System.Configuration;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;
using System.Text.RegularExpressions;

namespace Awagaman_ERP.Data
{
    public static class BackendSettings
    {
        private static readonly object SyncRoot = new object();
        private static bool _loaded;
        private static bool _useRemoteApi;
        private static string _apiBaseUrl;
        private static bool _runLocalApiServer;
        private static string _localApiExecutablePath;

        public static bool UseRemoteApi
        {
            get { EnsureLoaded(); return _useRemoteApi; }
        }

        public static string ApiBaseUrl =>
            (EnsureLoadedAndGetApiBaseUrl()).TrimEnd('/');

        public static bool RunLocalApiServer
        {
            get { EnsureLoaded(); return _runLocalApiServer; }
        }

        public static string LocalApiExecutablePath
        {
            get { EnsureLoaded(); return _localApiExecutablePath; }
        }

        private static void EnsureLoaded()
        {
            if (_loaded)
            {
                return;
            }

            lock (SyncRoot)
            {
                if (_loaded)
                {
                    return;
                }

                _useRemoteApi = ReadBool("UseRemoteApi", false);
                _apiBaseUrl = ReadString("ApiBaseUrl", "http://localhost:5088");
                _runLocalApiServer = ReadBool("RunLocalApiServer", false);
                _localApiExecutablePath = ReadString("LocalApiExecutablePath", Path.Combine("ApiServer", "Awagaman.Api.exe"));

                ApplyOverridesFromMachineConfig();

                _loaded = true;
            }
        }

        private static string EnsureLoadedAndGetApiBaseUrl()
        {
            EnsureLoaded();
            return string.IsNullOrWhiteSpace(_apiBaseUrl) ? "http://localhost:5088" : _apiBaseUrl;
        }

        private static string ReadString(string key, string fallback)
        {
            var value = ConfigurationManager.AppSettings[key];
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }

        private static bool ReadBool(string key, bool fallback)
        {
            bool value;
            return bool.TryParse(ConfigurationManager.AppSettings[key], out value) ? value : fallback;
        }

        private static void ApplyOverridesFromMachineConfig()
        {
            try
            {
                var dir = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
                var path = Path.Combine(dir, "Awagaman ERP", "network.settings.json");
                if (!File.Exists(path))
                {
                    return;
                }

                var json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return;
                }

                var data = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
                if (data == null)
                {
                    ApplyLooseOverrides(json);
                    return;
                }

                object value;
                if (data.TryGetValue("UseRemoteApi", out value))
                {
                    bool parsed;
                    if (bool.TryParse(Convert.ToString(value), out parsed))
                    {
                        _useRemoteApi = parsed;
                    }
                }

                if (data.TryGetValue("ApiBaseUrl", out value))
                {
                    var url = Convert.ToString(value);
                    if (!string.IsNullOrWhiteSpace(url))
                    {
                        _apiBaseUrl = url.Trim();
                    }
                }

                if (data.TryGetValue("RunLocalApiServer", out value))
                {
                    bool parsed;
                    if (bool.TryParse(Convert.ToString(value), out parsed))
                    {
                        _runLocalApiServer = parsed;
                    }
                }

                if (data.TryGetValue("LocalApiExecutablePath", out value))
                {
                    var exe = Convert.ToString(value);
                    if (!string.IsNullOrWhiteSpace(exe))
                    {
                        _localApiExecutablePath = exe.Trim();
                    }
                }
            }
            catch
            {
                try
                {
                    var dir = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
                    var path = Path.Combine(dir, "Awagaman ERP", "network.settings.json");
                    if (File.Exists(path))
                    {
                        ApplyLooseOverrides(File.ReadAllText(path));
                    }
                }
                catch
                {
                    // Ignore malformed config and keep App.config defaults.
                }
            }
        }

        private static void ApplyLooseOverrides(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return;
            }

            var useRemote = Regex.Match(json, "\"UseRemoteApi\"\\s*:\\s*(true|false)", RegexOptions.IgnoreCase);
            if (useRemote.Success)
            {
                bool parsed;
                if (bool.TryParse(useRemote.Groups[1].Value, out parsed))
                {
                    _useRemoteApi = parsed;
                }
            }

            var apiBaseUrl = Regex.Match(json, "\"ApiBaseUrl\"\\s*:\\s*\"([^\"]*)\"", RegexOptions.IgnoreCase);
            if (apiBaseUrl.Success && !string.IsNullOrWhiteSpace(apiBaseUrl.Groups[1].Value))
            {
                _apiBaseUrl = apiBaseUrl.Groups[1].Value.Trim();
            }

            var runLocal = Regex.Match(json, "\"RunLocalApiServer\"\\s*:\\s*(true|false)", RegexOptions.IgnoreCase);
            if (runLocal.Success)
            {
                bool parsed;
                if (bool.TryParse(runLocal.Groups[1].Value, out parsed))
                {
                    _runLocalApiServer = parsed;
                }
            }

            var localExe = Regex.Match(json, "\"LocalApiExecutablePath\"\\s*:\\s*\"([^\"]*)\"", RegexOptions.IgnoreCase);
            if (localExe.Success && !string.IsNullOrWhiteSpace(localExe.Groups[1].Value))
            {
                _localApiExecutablePath = localExe.Groups[1].Value.Trim();
            }
        }
    }
}
